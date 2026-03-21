using System;
using System.Collections.Generic;
using System.Linq;

namespace KSR.Collections;

/// <summary>
/// Higher-order operations on immutable lists (KSR <c>List&lt;T&gt;</c> → <c>IReadOnlyList&lt;T&gt;</c>).
/// All methods that return lists return a new <c>IReadOnlyList&lt;T&gt;</c>; the original is unchanged.
/// Methods that need mutation return <c>List&lt;T&gt;</c> (KSR <c>MutableList&lt;T&gt;</c>).
/// </summary>
public static class Lst
{
    // ── Transformation ────────────────────────────────────────────────────────

    public static IReadOnlyList<U> Map<T, U>(IReadOnlyList<T> list, Func<T, U> f)
        => list.Select(f).ToArray();

    public static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.Where(pred).ToArray();

    public static IReadOnlyList<U> FlatMap<T, U>(IReadOnlyList<T> list, Func<T, IReadOnlyList<U>> f)
        => list.SelectMany(f).ToArray();

    public static IReadOnlyList<T> Flatten<T>(IReadOnlyList<IReadOnlyList<T>> lists)
        => lists.SelectMany(x => x).ToArray();

    // ── Aggregation ───────────────────────────────────────────────────────────

    public static U Fold<T, U>(IReadOnlyList<T> list, U init, Func<U, T, U> f)
        => list.Aggregate(init, f);

    public static void ForEach<T>(IReadOnlyList<T> list, Action<T> f)
    {
        foreach (var item in list) f(item);
    }

    public static bool Any<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.Any(pred);

    public static bool All<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.All(pred);

    public static bool None<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => !list.Any(pred);

    public static int Count<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.Count(pred);

    // ── Numeric aggregation ───────────────────────────────────────────────────

    public static int Sum(IReadOnlyList<int> list) => list.Sum();
    public static long SumLong(IReadOnlyList<long> list) => list.Sum();
    public static double SumDouble(IReadOnlyList<double> list) => list.Sum();

    public static int? Min(IReadOnlyList<int> list) => list.Any() ? list.Min() : (int?)null;
    public static int? Max(IReadOnlyList<int> list) => list.Any() ? list.Max() : (int?)null;
    public static double? MinDouble(IReadOnlyList<double> list) => list.Any() ? list.Min() : (double?)null;
    public static double? MaxDouble(IReadOnlyList<double> list) => list.Any() ? list.Max() : (double?)null;

    public static T? MinBy<T, K>(IReadOnlyList<T> list, Func<T, K> key) where K : IComparable<K>
        => list.Any() ? list.MinBy(key) : default;

    public static T? MaxBy<T, K>(IReadOnlyList<T> list, Func<T, K> key) where K : IComparable<K>
        => list.Any() ? list.MaxBy(key) : default;

    // ── Access ────────────────────────────────────────────────────────────────

    public static T First<T>(IReadOnlyList<T> list) => list[0];
    public static T Last<T>(IReadOnlyList<T> list) => list[list.Count - 1];
    public static T Get<T>(IReadOnlyList<T> list, int index) => list[index];

    public static T? Find<T>(IReadOnlyList<T> list, Func<T, bool> pred) where T : class
        => list.FirstOrDefault(pred);

    public static int Size<T>(IReadOnlyList<T> list) => list.Count;
    public static bool IsEmpty<T>(IReadOnlyList<T> list) => list.Count == 0;
    public static bool Contains<T>(IReadOnlyList<T> list, T item) => list.Contains(item);

    // ── Ordering ──────────────────────────────────────────────────────────────

    public static IReadOnlyList<T> Sorted<T>(IReadOnlyList<T> list) where T : IComparable<T>
        => list.OrderBy(x => x).ToArray();

    public static IReadOnlyList<T> SortedBy<T, K>(IReadOnlyList<T> list, Func<T, K> key)
        where K : IComparable<K>
        => list.OrderBy(key).ToArray();

    public static IReadOnlyList<T> SortedByDescending<T, K>(IReadOnlyList<T> list, Func<T, K> key)
        where K : IComparable<K>
        => list.OrderByDescending(key).ToArray();

    public static IReadOnlyList<T> Reversed<T>(IReadOnlyList<T> list)
        => list.Reverse().ToArray();

    // ── Slicing ───────────────────────────────────────────────────────────────

    public static IReadOnlyList<T> Take<T>(IReadOnlyList<T> list, int n)
        => list.Take(n).ToArray();

    public static IReadOnlyList<T> Drop<T>(IReadOnlyList<T> list, int n)
        => list.Skip(n).ToArray();

    public static IReadOnlyList<T> TakeWhile<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.TakeWhile(pred).ToArray();

    public static IReadOnlyList<T> DropWhile<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.SkipWhile(pred).ToArray();

    // ── Set operations ────────────────────────────────────────────────────────

    public static IReadOnlyList<T> Distinct<T>(IReadOnlyList<T> list)
        => list.Distinct().ToArray();

    // ── Combining ─────────────────────────────────────────────────────────────

    public static IReadOnlyList<(T, U)> Zip<T, U>(IReadOnlyList<T> a, IReadOnlyList<U> b)
        => a.Zip(b, (x, y) => (x, y)).ToArray();

    public static IReadOnlyList<T> Concat<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
        => a.Concat(b).ToArray();

    /// <summary>Returns a new list with <paramref name="item"/> appended.</summary>
    public static IReadOnlyList<T> Plus<T>(IReadOnlyList<T> list, T item)
    {
        var result = new T[list.Count + 1];
        for (int i = 0; i < list.Count; i++) result[i] = list[i];
        result[list.Count] = item;
        return result;
    }

    // ── Grouping ──────────────────────────────────────────────────────────────

    public static IReadOnlyDictionary<K, IReadOnlyList<T>> GroupBy<T, K>(
        IReadOnlyList<T> list, Func<T, K> key) where K : notnull
        => list.GroupBy(key).ToDictionary(g => g.Key, g => (IReadOnlyList<T>)g.ToArray());

    // ── String helpers ────────────────────────────────────────────────────────

    public static string JoinToString<T>(IReadOnlyList<T> list, string sep)
        => string.Join(sep, list);

    // ── Conversion ────────────────────────────────────────────────────────────

    /// <summary>Wraps an <see cref="IEnumerable{T}"/> into an immutable list.</summary>
    public static IReadOnlyList<T> ToList<T>(IEnumerable<T> items) => items.ToArray();

    /// <summary>Converts an immutable list to a mutable <see cref="List{T}"/> (KSR <c>MutableList&lt;T&gt;</c>).</summary>
    public static List<T> ToMutable<T>(IReadOnlyList<T> list) => new List<T>(list);

    // ── Indexed iteration ─────────────────────────────────────────────────────

    public static void ForEachIndexed<T>(IReadOnlyList<T> list, Action<int, T> f)
    {
        for (int i = 0; i < list.Count; i++) f(i, list[i]);
    }
}

/// <summary>
/// Higher-order operations on immutable maps (KSR <c>Map&lt;K,V&gt;</c> → <c>IReadOnlyDictionary&lt;K,V&gt;</c>).
/// </summary>
public static class Mp
{
    // ── Access ────────────────────────────────────────────────────────────────

    public static IReadOnlyList<K> Keys<K, V>(IReadOnlyDictionary<K, V> map) where K : notnull
        => map.Keys.ToArray();

    public static IReadOnlyList<V> Values<K, V>(IReadOnlyDictionary<K, V> map) where K : notnull
        => map.Values.ToArray();

    public static bool ContainsKey<K, V>(IReadOnlyDictionary<K, V> map, K key) where K : notnull
        => map.ContainsKey(key);

    public static V? Get<K, V>(IReadOnlyDictionary<K, V> map, K key) where K : notnull where V : class
        => map.TryGetValue(key, out var v) ? v : null;

    public static V GetOrDefault<K, V>(IReadOnlyDictionary<K, V> map, K key, V defaultValue)
        where K : notnull
        => map.TryGetValue(key, out var v) ? v : defaultValue;

    public static int Size<K, V>(IReadOnlyDictionary<K, V> map) where K : notnull => map.Count;
    public static bool IsEmpty<K, V>(IReadOnlyDictionary<K, V> map) where K : notnull => map.Count == 0;

    // ── Transformation ────────────────────────────────────────────────────────

    public static IReadOnlyDictionary<K, U> MapValues<K, V, U>(
        IReadOnlyDictionary<K, V> map, Func<V, U> f) where K : notnull
        => map.ToDictionary(kv => kv.Key, kv => f(kv.Value));

    public static IReadOnlyDictionary<K, V> Filter<K, V>(
        IReadOnlyDictionary<K, V> map, Func<K, V, bool> pred) where K : notnull
        => map.Where(kv => pred(kv.Key, kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);

    public static void ForEach<K, V>(IReadOnlyDictionary<K, V> map, Action<K, V> f) where K : notnull
    {
        foreach (var kv in map) f(kv.Key, kv.Value);
    }

    // ── Conversion ────────────────────────────────────────────────────────────

    /// <summary>Converts an immutable map to a mutable <see cref="Dictionary{K,V}"/> (KSR <c>MutableMap&lt;K,V&gt;</c>).</summary>
    public static Dictionary<K, V> ToMutable<K, V>(IReadOnlyDictionary<K, V> map) where K : notnull
        => new Dictionary<K, V>(map);
}
