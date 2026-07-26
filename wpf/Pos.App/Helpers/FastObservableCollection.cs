using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Pos.App.Helpers;

public class FastObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public FastObservableCollection() { }

    public FastObservableCollection(IEnumerable<T> collection) : base(collection) { }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnPropertyChanged(e);
        }
    }

    /// <summary>
    /// Replaces the entire collection in memory and fires a single Reset notification to WPF bindings.
    /// This prevents N individual CollectionChanged events on every keypress during search and backspace.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
