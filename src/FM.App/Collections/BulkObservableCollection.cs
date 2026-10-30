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

        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
        OnCollectionChanged(ResetNotification);
    }

    public void ReplaceAll(IEnumerable<T> collection)
    {
        CheckReentrancy();
        Items.Clear();

        if (collection != null)
        {
            foreach (var item in collection)
            {
                Items.Add(item);
            }
        }

        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
        OnCollectionChanged(ResetNotification);
    }
}
