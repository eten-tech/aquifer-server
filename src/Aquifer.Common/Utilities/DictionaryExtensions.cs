namespace Aquifer.Common.Utilities;

public static class DictionaryExtensions
{
    public static TValue? GetValueOrNull<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key)
        where TValue : struct
    {
        return source.TryGetValue(key, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Creates a dictionary from a sequence of values, ignoring duplicate keys (the first value for duplicate keys will win).
    /// The standard ToDictionary() method will throw on duplicate keys.
    /// </summary>
    public static Dictionary<TKey, TSource> ToDictionaryIgnoringDuplicates<TSource, TKey>(
        this IEnumerable<TSource>? source,
        Func<TSource, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        return source.ToDictionaryIgnoringDuplicates(keySelector, i => i, comparer);
    }

    /// <summary>
    /// Creates a dictionary from a sequence of values, ignoring duplicate keys (the first value for duplicate keys will win).
    /// The standard ToDictionary() method will throw on duplicate keys.
    /// </summary>
    public static Dictionary<TKey, TElement> ToDictionaryIgnoringDuplicates<TSource, TKey, TElement>(
        this IEnumerable<TSource>? source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TElement> elementSelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var dictionary = new Dictionary<TKey, TElement>(comparer);

        if (source == null)
        {
            return dictionary;
        }

        foreach (var element in source)
        {
            var key = keySelector(element);
            if (!dictionary.ContainsKey(key))
            {
                dictionary[key] = elementSelector(element);
            }
        }

        return dictionary;
    }
}