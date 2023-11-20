namespace Uniquer.Core.Helpers;

public static  class Extensions
{
    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> source)
    {
        foreach (var item in source)
            collection.Add(item);
    }
}
