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

    // Use Enumerable.X(list, ...) explicitly throughout to avoid infinite recursion
    // when the ListExtensions class shadow-overrides LINQ extension methods on IReadOnlyList<T>.

    public static bool Any<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => Enumerable.Any(list, pred);

    public static bool All<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => Enumerable.All(list, pred);

    public static bool None<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => !Enumerable.Any(list, pred);

    public static int Count<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => Enumerable.Count(list, pred);

    // ── Numeric aggregation ───────────────────────────────────────────────────

    public static int    Sum      (IReadOnlyList<int>    list) => Enumerable.Sum(list);
    public static long   SumLong  (IReadOnlyList<long>   list) => Enumerable.Sum(list);
    public static double SumDouble(IReadOnlyList<double>  list) => Enumerable.Sum(list);

    public static int?    Min      (IReadOnlyList<int>    list) => Enumerable.Any(list) ? Enumerable.Min(list) : (int?)null;
    public static int?    Max      (IReadOnlyList<int>    list) => Enumerable.Any(list) ? Enumerable.Max(list) : (int?)null;
    public static double? MinDouble(IReadOnlyList<double>  list) => Enumerable.Any(list) ? Enumerable.Min(list) : (double?)null;
    public static double? MaxDouble(IReadOnlyList<double>  list) => Enumerable.Any(list) ? Enumerable.Max(list) : (double?)null;

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
    public static bool Contains<T>(IReadOnlyList<T> list, T item) => Enumerable.Contains(list, item);

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
        => Enumerable.Take(list, n).ToArray();

    public static IReadOnlyList<T> Drop<T>(IReadOnlyList<T> list, int n)
        => list.Skip(n).ToArray();

    public static IReadOnlyList<T> TakeWhile<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => Enumerable.TakeWhile(list, pred).ToArray();

    public static IReadOnlyList<T> DropWhile<T>(IReadOnlyList<T> list, Func<T, bool> pred)
        => list.SkipWhile(pred).ToArray();

    // ── Set operations ────────────────────────────────────────────────────────

    public static IReadOnlyList<T> Distinct<T>(IReadOnlyList<T> list)
        => Enumerable.Distinct(list).ToArray();

    // ── Combining ─────────────────────────────────────────────────────────────

    public static IReadOnlyList<(T, U)> Zip<T, U>(IReadOnlyList<T> a, IReadOnlyList<U> b)
        => a.Zip(b, (x, y) => (x, y)).ToArray();

    public static IReadOnlyList<T> Concat<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
        => Enumerable.Concat(a, b).ToArray();

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
        => Enumerable.GroupBy(list, key).ToDictionary(g => g.Key, g => (IReadOnlyList<T>)g.ToArray());

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

// ─────────────────────────────────────────────────────────────────────────────
//  Extension methods — fluent API
//
//  These mirror every static method on Lst/Mp as extension methods so that
//  KSR code can use the natural dot-chaining style:
//
//    val evens = nums.filter { n -> n % 2 == 0 }
//    val sq    = evens.map { n -> n * n }
//
//  KSR member-access compiles to Pascal-case, so `nums.filter` → `nums.Filter`.
//  C# resolves the generic type arguments automatically from context.
// ─────────────────────────────────────────────────────────────────────────────

public static class ListExtensions
{
    public static IReadOnlyList<U>  Map<T, U>    (this IReadOnlyList<T> self, Func<T, U> f)       => Lst.Map(self, f);
    public static IReadOnlyList<T>  Filter<T>    (this IReadOnlyList<T> self, Func<T, bool> pred)  => Lst.Filter(self, pred);
    public static IReadOnlyList<U>  FlatMap<T, U>(this IReadOnlyList<T> self, Func<T, IReadOnlyList<U>> f) => Lst.FlatMap(self, f);
    public static IReadOnlyList<T>  Flatten<T>   (this IReadOnlyList<IReadOnlyList<T>> self)        => Lst.Flatten(self);

    public static U    Fold<T, U>  (this IReadOnlyList<T> self, U init, Func<U, T, U> f)  => Lst.Fold(self, init, f);
    public static void ForEach<T>  (this IReadOnlyList<T> self, Action<T> f)               => Lst.ForEach(self, f);
    public static bool Any<T>      (this IReadOnlyList<T> self, Func<T, bool> pred)        => Lst.Any(self, pred);
    public static bool All<T>      (this IReadOnlyList<T> self, Func<T, bool> pred)        => Lst.All(self, pred);
    public static bool None<T>     (this IReadOnlyList<T> self, Func<T, bool> pred)        => Lst.None(self, pred);
    public static int  Count<T>    (this IReadOnlyList<T> self, Func<T, bool> pred)        => Lst.Count(self, pred);

    public static int     Sum    (this IReadOnlyList<int>    self) => Lst.Sum(self);
    public static long    SumLong(this IReadOnlyList<long>   self) => Lst.SumLong(self);
    public static double  SumDouble(this IReadOnlyList<double> self) => Lst.SumDouble(self);
    public static int?    Min    (this IReadOnlyList<int>    self) => Lst.Min(self);
    public static int?    Max    (this IReadOnlyList<int>    self) => Lst.Max(self);
    public static double? MinDouble(this IReadOnlyList<double> self) => Lst.MinDouble(self);
    public static double? MaxDouble(this IReadOnlyList<double> self) => Lst.MaxDouble(self);

    public static T? Find<T>    (this IReadOnlyList<T> self, Func<T, bool> pred) where T : class => Lst.Find(self, pred);
    public static T  First<T>   (this IReadOnlyList<T> self)       => Lst.First(self);
    public static T  Last<T>    (this IReadOnlyList<T> self)        => Lst.Last(self);
    public static T  Get<T>     (this IReadOnlyList<T> self, int i) => Lst.Get(self, i);
    public static int  Size<T>  (this IReadOnlyList<T> self)        => Lst.Size(self);
    public static bool IsEmpty<T>(this IReadOnlyList<T> self)       => Lst.IsEmpty(self);
    public static bool Contains<T>(this IReadOnlyList<T> self, T item) => Lst.Contains(self, item);

    public static IReadOnlyList<T> Sorted<T>     (this IReadOnlyList<T> self) where T : IComparable<T> => Lst.Sorted(self);
    public static IReadOnlyList<T> SortedBy<T, K>(this IReadOnlyList<T> self, Func<T, K> key) where K : IComparable<K> => Lst.SortedBy(self, key);
    public static IReadOnlyList<T> SortedByDescending<T, K>(this IReadOnlyList<T> self, Func<T, K> key) where K : IComparable<K> => Lst.SortedByDescending(self, key);
    public static IReadOnlyList<T> Reversed<T>   (this IReadOnlyList<T> self) => Lst.Reversed(self);

    public static IReadOnlyList<T> Take<T>      (this IReadOnlyList<T> self, int n)             => Lst.Take(self, n);
    public static IReadOnlyList<T> Drop<T>      (this IReadOnlyList<T> self, int n)             => Lst.Drop(self, n);
    public static IReadOnlyList<T> TakeWhile<T> (this IReadOnlyList<T> self, Func<T, bool> pred) => Lst.TakeWhile(self, pred);
    public static IReadOnlyList<T> DropWhile<T> (this IReadOnlyList<T> self, Func<T, bool> pred) => Lst.DropWhile(self, pred);
    public static IReadOnlyList<T> Distinct<T>  (this IReadOnlyList<T> self) => Lst.Distinct(self);

    public static IReadOnlyList<T>    Concat<T>  (this IReadOnlyList<T> self, IReadOnlyList<T> other) => Lst.Concat(self, other);
    public static IReadOnlyList<T>    Plus<T>    (this IReadOnlyList<T> self, T item) => Lst.Plus(self, item);
    public static IReadOnlyList<(T, U)> Zip<T, U>(this IReadOnlyList<T> self, IReadOnlyList<U> other) => Lst.Zip(self, other);

    public static IReadOnlyDictionary<K, IReadOnlyList<T>> GroupBy<T, K>(this IReadOnlyList<T> self, Func<T, K> key) where K : notnull => Lst.GroupBy(self, key);

    public static string        JoinToString<T>(this IReadOnlyList<T> self, string sep) => Lst.JoinToString(self, sep);
    public static List<T>       ToMutable<T>   (this IReadOnlyList<T> self) => Lst.ToMutable(self);
    public static void          ForEachIndexed<T>(this IReadOnlyList<T> self, Action<int, T> f) => Lst.ForEachIndexed(self, f);
}

public static class MapExtensions
{
    public static IReadOnlyList<K>              Keys<K, V>        (this IReadOnlyDictionary<K, V> self) where K : notnull => Mp.Keys(self);
    public static IReadOnlyList<V>              Values<K, V>      (this IReadOnlyDictionary<K, V> self) where K : notnull => Mp.Values(self);
    public static bool                          ContainsKey<K, V> (this IReadOnlyDictionary<K, V> self, K key) where K : notnull => Mp.ContainsKey(self, key);
    public static V?                            Get<K, V>         (this IReadOnlyDictionary<K, V> self, K key) where K : notnull where V : class => Mp.Get(self, key);
    public static V                             GetOrDefault<K, V>(this IReadOnlyDictionary<K, V> self, K key, V def) where K : notnull => Mp.GetOrDefault(self, key, def);
    public static int                           Size<K, V>        (this IReadOnlyDictionary<K, V> self) where K : notnull => Mp.Size(self);
    public static bool                          IsEmpty<K, V>     (this IReadOnlyDictionary<K, V> self) where K : notnull => Mp.IsEmpty(self);
    public static IReadOnlyDictionary<K, U>     MapValues<K, V, U>(this IReadOnlyDictionary<K, V> self, Func<V, U> f) where K : notnull => Mp.MapValues(self, f);
    public static IReadOnlyDictionary<K, V>     Filter<K, V>      (this IReadOnlyDictionary<K, V> self, Func<K, V, bool> pred) where K : notnull => Mp.Filter(self, pred);
    public static void                          ForEach<K, V>     (this IReadOnlyDictionary<K, V> self, Action<K, V> f) where K : notnull => Mp.ForEach(self, f);
    public static Dictionary<K, V>             ToMutable<K, V>   (this IReadOnlyDictionary<K, V> self) where K : notnull => Mp.ToMutable(self);
}
