using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FM.App.Services.QuickOpen;
using FM.Core.Search;

namespace FM.App.ViewModels;

public partial class QuickOpenViewModel : ObservableObject
{
    private readonly IQuickOpenSourceProvider _sourceProvider;
    private readonly Action<Action> _uiDispatcher;
    private CancellationTokenSource? _availabilityCts;

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

    public ObservableCollection<QuickOpenItemViewModel> Results { get; } = new();

    public string PlaceholderText => Mode == QuickOpenMode.Commands ? CommandPlaceholder : DefaultPlaceholder;

    public bool HasResults => Results.Count > 0;

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
        _availabilityCts?.Cancel();
        IsOpen = false;
        QueryText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
        SelectedItem = null;
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
        if (SelectedItem == null || !SelectedItem.IsAvailable) return;

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

        List<QuickOpenItemViewModel> matched;

        if (isCommandMode)
        {
            string cmdQuery = raw.Length > 1 ? raw.Substring(1).TrimStart() : string.Empty;
            var commands = _sourceProvider.GetCommands();

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
        }
        else
        {
            string placeQuery = raw.Trim();
            var places = _sourceProvider.GetPlaces();

            if (string.IsNullOrWhiteSpace(placeQuery))
            {
                // In empty query: show in natural priority order (Open Panes -> Favorites -> Recents -> Drives)
                matched = places
                    .OrderBy(p => p.Priority)
                    .Take(50)
                    .ToList();
            }
            else
            {
                matched = places
                    .Select(p =>
                    {
                        var titleMatch = FuzzyMatcher.Match(p.Title, placeQuery);
                        var subMatch = FuzzyMatcher.Match(p.Subtitle, placeQuery);

                        if (titleMatch != null)
                        {
                            p.Score = titleMatch.Score;
                        }
                        else if (subMatch != null)
                        {
                            p.Score = subMatch.Score * 0.7; // slight penalty for subtitle match
                        }
                        else
                        {
                            p.Score = -1.0;
                        }

                        return p;
                    })
                    .Where(p => p.Score >= 0)
                    .OrderByDescending(p => p.Score)
                    .ThenBy(p => p.Priority)
                    .Take(50)
                    .ToList();
            }
        }

        _uiDispatcher(() =>
        {
            Results.Clear();
            foreach (var item in matched)
            {
                Results.Add(item);
            }

            OnPropertyChanged(nameof(HasResults));

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

        // Background availability check for places
        if (!isCommandMode && matched.Count > 0)
        {
            _availabilityCts?.Cancel();
            _availabilityCts = new CancellationTokenSource();
            var token = _availabilityCts.Token;
            _ = _sourceProvider.CheckAvailabilityAsync(matched, token);
        }
    }
}
