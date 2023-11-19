using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Uniquer.Core.Helpers;

public static  class Extensions
{
    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> source)
    {
        foreach (var item in source)
            collection.Add(item);
    }
}
