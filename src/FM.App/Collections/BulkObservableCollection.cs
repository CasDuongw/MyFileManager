using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FM.App.Collections;

/// <summary>
/// An ObservableCollection that supports batch insertions without firing 
/// CollectionChanged notifications per individual item, keeping UI rendering at 60fps.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountPropertyChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs ResetNotification = new(NotifyCollectionChangedAction.Reset);

    public void AddRange(IEnumerable<T> collection)
    {
        if (collection == null) return;
        CheckReentrancy();

        var list = collection as IList<T> ?? collection.ToList();
        if (list.Count == 0) return;

        int startIndex = Items.Count;
        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);

        for (int i = 0; i < list.Count; i++)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                list[i],
                startIndex + i));
        }
    }

    public void ReplaceAll(IEnumerable<T> collection)
    {
        CheckReentrancy();
        var list = collection as IList<T> ?? collection?.ToList() ?? new List<T>();
        if (Items.Count == 0 && list.Count == 0) return;

        Items.Clear();
        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
        OnCollectionChanged(ResetNotification);
    }
}
