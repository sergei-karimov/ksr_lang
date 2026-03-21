using System.Collections.Generic;
using Xunit;

namespace KSR.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  CollectionsTests
//
//  Two sections:
//    1. CodeGen — verifies namespace mapping and generated C# for
//       ksr.collections, MutableList<T>, and MutableMap<K,V>.
//    2. Runtime — unit tests for KSR.Collections.Lst and Mp directly.
// ─────────────────────────────────────────────────────────────────────────────

public class CollectionsCodeGenTests
{
    private static string Gen(string src) => KsrHelper.Generate(src);
    private static string Flat(string src) => KsrHelper.Flatten(Gen(src));

    // ── ksr.collections namespace mapping ─────────────────────────────────────

    [Fact]
    public void UseKsrCollections_EmitsUsingKsrCollections()
    {
        var cs = Gen("use ksr.collections\nfun main() {}");
        Assert.Contains("using KSR.Collections;", cs);
    }

    [Fact]
    public void UseKsrCollections_DoesNotEmitLiteralName()
    {
        var cs = Gen("use ksr.collections\nfun main() {}");
        Assert.DoesNotContain("using ksr.collections;", cs);
    }

    // ── MutableList type mapping ───────────────────────────────────────────────

    [Fact]
    public void MutableList_MapsToListCSharp() =>
        Assert.Contains("List<int>", Gen("fun f(xs: MutableList<Int>) { }"));

    [Fact]
    public void MutableMap_MapsToDictionaryCSharp() =>
        Assert.Contains("Dictionary<string, int>", Gen("fun f(m: MutableMap<String, Int>) { }"));

    // ── MutableList literals ──────────────────────────────────────────────────

    [Fact]
    public void MutableList_EmptyLiteralWithHint() =>
        Assert.Contains("new List<int>()",
            Flat("fun f() { val xs: MutableList<Int> = [] }"));

    [Fact]
    public void MutableList_NonEmptyLiteralWithHint() =>
        Assert.Contains("new List<int> { 1, 2, 3 }",
            Flat("fun f() { val xs: MutableList<Int> = [1, 2, 3] }"));

    // ── Lst / Mp member access is Pascal-cased ────────────────────────────────

    [Fact]
    public void Lst_MapCall_PascalCased() =>
        Assert.Contains("Lst.Map(",
            Flat("use ksr.collections\nfun f(xs: List<Int>) { val r = Lst.map(xs, { x -> x }) }"));

    [Fact]
    public void Lst_FilterCall_PascalCased() =>
        Assert.Contains("Lst.Filter(",
            Flat("use ksr.collections\nfun f(xs: List<Int>) { val r = Lst.filter(xs, { x -> true }) }"));

    [Fact]
    public void Mp_KeysCall_PascalCased() =>
        Assert.Contains("Mp.Keys(",
            Flat("use ksr.collections\nfun f(m: Map<String, Int>) { val r = Mp.keys(m) }"));

    // ── System.Collections.Generic is in preamble ─────────────────────────────

    [Fact]
    public void Preamble_ContainsSystemCollectionsGeneric() =>
        Assert.Contains("using System.Collections.Generic;", Gen("fun main() { }"));
}

public class LstRuntimeTests
{
    // ── Map / Filter / FlatMap ─────────────────────────────────────────────────

    [Fact]
    public void Map_TransformsEachElement()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        var result = KSR.Collections.Lst.Map(input, x => x * 2);
        Assert.Equal(new[] { 2, 4, 6 }, result);
    }

    [Fact]
    public void Filter_KeepsMatchingElements()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3, 4, 5 };
        var result = KSR.Collections.Lst.Filter(input, x => x % 2 == 0);
        Assert.Equal(new[] { 2, 4 }, result);
    }

    [Fact]
    public void FlatMap_FlattensResults()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        var result = KSR.Collections.Lst.FlatMap(input, x => (IReadOnlyList<int>)new[] { x, x * 10 });
        Assert.Equal(new[] { 1, 10, 2, 20, 3, 30 }, result);
    }

    // ── Aggregation ───────────────────────────────────────────────────────────

    [Fact]
    public void Fold_AccumulatesCorrectly()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3, 4 };
        var sum = KSR.Collections.Lst.Fold(input, 0, (acc, x) => acc + x);
        Assert.Equal(10, sum);
    }

    [Fact]
    public void Any_ReturnsTrueWhenMatch()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.True(KSR.Collections.Lst.Any(input, x => x > 2));
    }

    [Fact]
    public void All_ReturnsFalseWhenAnyFails()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.False(KSR.Collections.Lst.All(input, x => x > 1));
    }

    [Fact]
    public void None_ReturnsTrueWhenNoMatch()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.True(KSR.Collections.Lst.None(input, x => x > 10));
    }

    [Fact]
    public void Count_CountsMatchingElements()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3, 4, 5 };
        Assert.Equal(2, KSR.Collections.Lst.Count(input, x => x % 2 == 0));
    }

    [Fact]
    public void Sum_SumsAllElements()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3, 4 };
        Assert.Equal(10, KSR.Collections.Lst.Sum(input));
    }

    [Fact]
    public void Min_ReturnsMinimum()
    {
        IReadOnlyList<int> input = new[] { 3, 1, 4, 1, 5 };
        Assert.Equal(1, KSR.Collections.Lst.Min(input));
    }

    [Fact]
    public void Max_ReturnsMaximum()
    {
        IReadOnlyList<int> input = new[] { 3, 1, 4, 1, 5 };
        Assert.Equal(5, KSR.Collections.Lst.Max(input));
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sorted_SortsAscending()
    {
        IReadOnlyList<int> input = new[] { 3, 1, 4, 1, 5, 9 };
        var result = KSR.Collections.Lst.Sorted(input);
        Assert.Equal(new[] { 1, 1, 3, 4, 5, 9 }, result);
    }

    [Fact]
    public void Reversed_ReversesOrder()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.Equal(new[] { 3, 2, 1 }, KSR.Collections.Lst.Reversed(input));
    }

    [Fact]
    public void SortedBy_SortsByKey()
    {
        IReadOnlyList<string> input = new[] { "banana", "apple", "cherry" };
        var result = KSR.Collections.Lst.SortedBy(input, s => s.Length);
        Assert.Equal(new[] { "apple", "banana", "cherry" }, result);
    }

    // ── Slicing ───────────────────────────────────────────────────────────────

    [Fact]
    public void Take_TakesFirstN()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3, 4, 5 };
        Assert.Equal(new[] { 1, 2, 3 }, KSR.Collections.Lst.Take(input, 3));
    }

    [Fact]
    public void Drop_SkipsFirstN()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3, 4, 5 };
        Assert.Equal(new[] { 4, 5 }, KSR.Collections.Lst.Drop(input, 3));
    }

    // ── Access helpers ────────────────────────────────────────────────────────

    [Fact]
    public void First_ReturnsFirstElement()
    {
        IReadOnlyList<int> input = new[] { 7, 8, 9 };
        Assert.Equal(7, KSR.Collections.Lst.First(input));
    }

    [Fact]
    public void Last_ReturnsLastElement()
    {
        IReadOnlyList<int> input = new[] { 7, 8, 9 };
        Assert.Equal(9, KSR.Collections.Lst.Last(input));
    }

    [Fact]
    public void Find_ReturnsFirstMatch()
    {
        IReadOnlyList<string> input = new[] { "a", "bb", "ccc" };
        Assert.Equal("bb", KSR.Collections.Lst.Find(input, s => s.Length == 2));
    }

    [Fact]
    public void Find_ReturnsNullWhenNoMatch()
    {
        IReadOnlyList<string> input = new[] { "a", "bb" };
        Assert.Null(KSR.Collections.Lst.Find(input, s => s.Length == 5));
    }

    // ── Combining ─────────────────────────────────────────────────────────────

    [Fact]
    public void Concat_CombinesTwoLists()
    {
        IReadOnlyList<int> a = new[] { 1, 2 };
        IReadOnlyList<int> b = new[] { 3, 4 };
        Assert.Equal(new[] { 1, 2, 3, 4 }, KSR.Collections.Lst.Concat(a, b));
    }

    [Fact]
    public void Plus_AppendsElement()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.Equal(new[] { 1, 2, 3, 4 }, KSR.Collections.Lst.Plus(input, 4));
    }

    [Fact]
    public void Zip_PairsTwoLists()
    {
        IReadOnlyList<int> a = new[] { 1, 2, 3 };
        IReadOnlyList<string> b = new[] { "a", "b", "c" };
        var result = KSR.Collections.Lst.Zip(a, b);
        Assert.Equal((1, "a"), result[0]);
        Assert.Equal((3, "c"), result[2]);
    }

    // ── Other ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Distinct_RemovesDuplicates()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 2, 3, 3, 3 };
        Assert.Equal(new[] { 1, 2, 3 }, KSR.Collections.Lst.Distinct(input));
    }

    [Fact]
    public void JoinToString_JoinsWithSeparator()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.Equal("1, 2, 3", KSR.Collections.Lst.JoinToString(input, ", "));
    }

    [Fact]
    public void ToMutable_ReturnsListT()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        var mutable = KSR.Collections.Lst.ToMutable(input);
        Assert.IsType<List<int>>(mutable);
        mutable.Add(4);
        Assert.Equal(4, mutable.Count);
    }

    [Fact]
    public void Size_ReturnsCount()
    {
        IReadOnlyList<int> input = new[] { 1, 2, 3 };
        Assert.Equal(3, KSR.Collections.Lst.Size(input));
    }

    [Fact]
    public void IsEmpty_ReturnsTrueForEmptyList()
    {
        Assert.True(KSR.Collections.Lst.IsEmpty(Array.Empty<int>()));
        Assert.False(KSR.Collections.Lst.IsEmpty(new[] { 1 }));
    }
}

public class MpRuntimeTests
{
    private static IReadOnlyDictionary<string, int> MakeMap()
        => new Dictionary<string, int> { ["alice"] = 10, ["bob"] = 7, ["carol"] = 13 };

    [Fact]
    public void Keys_ReturnsAllKeys()
    {
        var keys = KSR.Collections.Mp.Keys(MakeMap());
        Assert.Contains("alice", keys);
        Assert.Contains("bob", keys);
        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public void Values_ReturnsAllValues()
    {
        var vals = KSR.Collections.Mp.Values(MakeMap());
        Assert.Contains(10, vals);
        Assert.Contains(7, vals);
    }

    [Fact]
    public void ContainsKey_TrueForExistingKey()
    {
        Assert.True(KSR.Collections.Mp.ContainsKey(MakeMap(), "alice"));
        Assert.False(KSR.Collections.Mp.ContainsKey(MakeMap(), "dave"));
    }

    [Fact]
    public void Get_ReturnsNullableValue()
    {
        // Note: int is a value type; Get<K,V> requires V : class
        IReadOnlyDictionary<string, string> m = new Dictionary<string, string>
            { ["a"] = "hello", ["b"] = "world" };
        Assert.Equal("hello", KSR.Collections.Mp.Get(m, "a"));
        Assert.Null(KSR.Collections.Mp.Get(m, "z"));
    }

    [Fact]
    public void GetOrDefault_FallsBackToDefault()
    {
        Assert.Equal(0, KSR.Collections.Mp.GetOrDefault(MakeMap(), "nobody", 0));
        Assert.Equal(10, KSR.Collections.Mp.GetOrDefault(MakeMap(), "alice", 0));
    }

    [Fact]
    public void MapValues_TransformsValues()
    {
        var result = KSR.Collections.Mp.MapValues(MakeMap(), v => v * 2);
        Assert.Equal(20, result["alice"]);
        Assert.Equal(14, result["bob"]);
    }

    [Fact]
    public void Filter_KeepsMatchingEntries()
    {
        var result = KSR.Collections.Mp.Filter(MakeMap(), (k, v) => v >= 10);
        Assert.True(KSR.Collections.Mp.ContainsKey(result, "alice"));
        Assert.False(KSR.Collections.Mp.ContainsKey(result, "bob"));
    }

    [Fact]
    public void Size_ReturnsCount()
    {
        Assert.Equal(3, KSR.Collections.Mp.Size(MakeMap()));
    }

    [Fact]
    public void IsEmpty_ReturnsTrueForEmptyMap()
    {
        Assert.True(KSR.Collections.Mp.IsEmpty(new Dictionary<string, int>()));
        Assert.False(KSR.Collections.Mp.IsEmpty(MakeMap()));
    }

    [Fact]
    public void ToMutable_ReturnsDictionaryThatCanBeModified()
    {
        var mutable = KSR.Collections.Mp.ToMutable(MakeMap());
        Assert.IsType<Dictionary<string, int>>(mutable);
        mutable["dave"] = 99;
        Assert.Equal(4, mutable.Count);
    }
}
