using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Collections;
using FM.App.Services.QuickOpen;
using FM.Core.Search;

namespace FM.App.ViewModels;

public partial class QuickOpenViewModel : ObservableObject
{
    private readonly IQuickOpenSourceProvider _sourceProvider;
    private readonly Action<Action> _uiDispatcher;
    private CancellationTokenSource? _availabilityCts;
    private CancellationTokenSource? _searchCts;
    private int _searchVersion;

    public Task? CurrentSearchTask { get; private set; }

    public const string DefaultPlaceholder = "Folders, tabs and workspaces — type > for commands";
    public const string CommandPlaceholder = "Type a command name...";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private QuickOpenMode _mode = QuickOpenMode.Places;

    [ObservableProperty]
    private QuickOpenItemViewModel? _selectedItem;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _emptyStateTitle = "No matching folders";

    public BulkObservableCollection<QuickOpenItemViewModel> Results { get; } = new();

    public string PlaceholderText => Mode == QuickOpenMode.Commands ? CommandPlaceholder : DefaultPlaceholder;

    public bool HasResults => Results.Count > 0;

    public bool HasNoMatches => IsOpen && !string.IsNullOrWhiteSpace(QueryText) && Results.Count == 0;

    public QuickOpenViewModel(
        IQuickOpenSourceProvider sourceProvider,
        Action<Action>? uiDispatcher = null)
    {
        _sourceProvider = sourceProvider;
        _uiDispatcher = uiDispatcher ?? (action =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        });
    }

    [RelayCommand]
    public void Open(string? prefill = null)
    {
        IsOpen = true;
        QueryText = prefill ?? string.Empty;
        UpdateFilteredItems();
    }

    [RelayCommand]
    public void Close()
    {
        _searchCts?.Cancel();
        _availabilityCts?.Cancel();
        IsOpen = false;
        QueryText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
        SelectedItem = null;
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    public void Clear()
    {
        QueryText = string.Empty;
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            SelectedItem = null;
            return;
        }

        int newIndex = SelectedIndex + delta;
        if (newIndex < 0)
        {
            newIndex = Results.Count - 1; // Wrap around to bottom
        }
        else if (newIndex >= Results.Count)
        {
            newIndex = 0; // Wrap around to top
        }

        SelectedIndex = newIndex;
        SelectedItem = Results[newIndex];
    }

    [RelayCommand]
    public void ExecuteSelected()
    {
        if (SelectedItem == null) return;

        var action = SelectedItem.ExecuteAction;
        Close();

        try
        {
            action?.Invoke();
        }
        catch
        {
            // Best effort
        }
    }

    partial void OnQueryTextChanged(string value)
    {
        UpdateFilteredItems();
    }

    partial void OnModeChanged(QuickOpenMode value)
    {
        OnPropertyChanged(nameof(PlaceholderText));
    }

    public void UpdateFilteredItems()
    {
        string raw = QueryText ?? string.Empty;
        bool isCommandMode = raw.StartsWith('>');

        Mode = isCommandMode ? QuickOpenMode.Commands : QuickOpenMode.Places;

        if (isCommandMode)
        {
            EmptyStateTitle = "No matching commands";
            StatusMessage = "Try typing another command name or press Esc to exit";

            string cmdQuery = raw.Length > 1 ? raw.Substring(1).TrimStart() : string.Empty;
            var commands = _sourceProvider.GetCommands();

            List<QuickOpenItemViewModel> matched;
            if (string.IsNullOrWhiteSpace(cmdQuery))
            {
                matched = commands.Take(50).ToList();
            }
            else
            {
                matched = commands
                    .Select(cmd =>
                    {
                        var res = FuzzyMatcher.Match(cmd.Title, cmdQuery);
                        cmd.Score = res?.Score ?? -1.0;
                        return cmd;
                    })
                    .Where(cmd => cmd.Score >= 0)
                    .OrderByDescending(cmd => cmd.Score)
                    .ThenBy(cmd => cmd.Subtitle)
                    .ThenBy(cmd => cmd.Title)
                    .Take(50)
                    .ToList();
            }

            _uiDispatcher(() =>
            {
                Results.ReplaceAll(matched);
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(HasNoMatches));

                if (Results.Count > 0)
                {
                    SelectedIndex = 0;
                    SelectedItem = Results[0];
                }
                else
                {
                    SelectedIndex = -1;
                    SelectedItem = null;
                }
            });
        }
        else
        {
            EmptyStateTitle = "No matching folders";
            StatusMessage = _sourceProvider.GetStatusMessage();

            string placeQuery = raw.Trim();
            var places = _sourceProvider.GetPlaces();

            int version = Interlocked.Increment(ref _searchVersion);
            _searchCts?.Cancel();

            if (string.IsNullOrWhiteSpace(placeQuery))
            {
                var matched = places
                    .OrderBy(p => p.Priority)
                    .Take(50)
                    .ToList();

                _uiDispatcher(() =>
                {
                    if (version != _searchVersion) return;
                    Results.ReplaceAll(matched);
                    OnPropertyChanged(nameof(HasResults));
                    OnPropertyChanged(nameof(HasNoMatches));

                    if (Results.Count > 0)
                    {
                        SelectedIndex = 0;
                        SelectedItem = Results[0];
                    }
                    else
                    {
                        SelectedIndex = -1;
                        SelectedItem = null;
                    }
                });

                TriggerAvailabilityCheck(matched);
            }
            else
            {
                _searchCts = new CancellationTokenSource();
                var token = _searchCts.Token;

                Action runSearch = () =>
                {
                    var prepQuery = PreparedQuery.Create(placeQuery);
                    var topK = PreparedMatcher.SelectTopK(
                        places,
                        prepQuery,
                        50,
                        p => (p.Title, p.NormTitle, p.Bitmask, p.RawParent, p.NormParent),
                        p => GetPlacePriorityBoost(p));

                    if (token.IsCancellationRequested || version != _searchVersion) return;

                    var matched = topK.Select(r =>
                    {
                        r.Item.Score = r.Score;
                        return r.Item;
                    }).ToList();

                    _uiDispatcher(() =>
                    {
                        if (version != _searchVersion) return;
                        Results.ReplaceAll(matched);
                        OnPropertyChanged(nameof(HasResults));
                        OnPropertyChanged(nameof(HasNoMatches));

                        if (Results.Count > 0)
                        {
                            SelectedIndex = 0;
                            SelectedItem = Results[0];
                        }
                        else
                        {
                            SelectedIndex = -1;
                            SelectedItem = null;
                        }
                    });

                    if (matched.Count > 0 && !token.IsCancellationRequested)
                    {
                        TriggerAvailabilityCheck(matched);
                    }
                };

                if (places.Count <= 200)
                {
                    CurrentSearchTask = Task.CompletedTask;
                    runSearch();
                }
                else
                {
                    CurrentSearchTask = Task.Run(runSearch, token);
                }
            }
        }
    }

    private void TriggerAvailabilityCheck(List<QuickOpenItemViewModel> items)
    {
        if (items.Count == 0) return;
        _availabilityCts?.Cancel();
        _availabilityCts = new CancellationTokenSource();
        var token = _availabilityCts.Token;
        _ = _sourceProvider.CheckAvailabilityAsync(items, token);
    }

    private static double GetPlacePriorityBoost(QuickOpenItemViewModel p)
    {
        return p.Badge switch
        {
            "Open pane" => 50.0,
            "Folder" => 40.0,
            "Favorite" => 30.0,
            "Recent" => 20.0,
            "Drive" => 10.0,
            _ => 0.0
        };
    }
}

