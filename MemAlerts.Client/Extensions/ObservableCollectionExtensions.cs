using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MemAlerts.Client.Extensions;

public static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        if (collection == null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
