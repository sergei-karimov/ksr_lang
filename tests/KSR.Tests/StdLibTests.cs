using Xunit;

namespace KSR.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  StdLibTests
//
//  Two sections:
//    1. CodeGen — verifies that `use ksr.io` / `use ksr.text` generate the
//       correct C# `using` directives (namespace mapping).
//    2. Runtime  — unit tests for KSR.Text and KSR.Io directly.
// ─────────────────────────────────────────────────────────────────────────────

public class StdLibCodeGenTests
{
    // ── use ksr.io ────────────────────────────────────────────────────────────

    [Fact]
    public void UseKsrIo_EmitsUsingKsrIo()
    {
        var cs = KsrHelper.Generate("use ksr.io\nfun main() {}");
        Assert.Contains("using KSR.Io;", cs);
    }

    [Fact]
    public void UseKsrIo_DoesNotEmitLiteralKsrIo()
    {
        var cs = KsrHelper.Generate("use ksr.io\nfun main() {}");
        Assert.DoesNotContain("using ksr.io;", cs);
    }

    // ── use ksr.text ──────────────────────────────────────────────────────────

    [Fact]
    public void UseKsrText_EmitsUsingKsrText()
    {
        var cs = KsrHelper.Generate("use ksr.text\nfun main() {}");
        Assert.Contains("using KSR.Text;", cs);
    }

    [Fact]
    public void UseKsrText_DoesNotEmitLiteralKsrText()
    {
        var cs = KsrHelper.Generate("use ksr.text\nfun main() {}");
        Assert.DoesNotContain("using ksr.text;", cs);
    }

    // ── regular namespace passes through unchanged ────────────────────────────

    [Fact]
    public void UseRegularNamespace_PassesThrough()
    {
        var cs = KsrHelper.Generate("use System.Collections.Generic\nfun main() {}");
        Assert.Contains("using System.Collections.Generic;", cs);
    }

    // ── both modules together ────────────────────────────────────────────────

    [Fact]
    public void UseBothModules_EmitsBothUsings()
    {
        var cs = KsrHelper.Generate("use ksr.io\nuse ksr.text\nfun main() {}");
        Assert.Contains("using KSR.Io;", cs);
        Assert.Contains("using KSR.Text;", cs);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  KSR.Text unit tests
// ─────────────────────────────────────────────────────────────────────────────

public class TextTests
{
    // ── parsing ───────────────────────────────────────────────────────────────

    [Fact] public void ToInt_ValidString_ReturnsValue()
        => Assert.Equal(42, KSR.Text.Text.ToInt("42"));

    [Fact] public void ToInt_WithWhitespace_ReturnsValue()
        => Assert.Equal(7, KSR.Text.Text.ToInt("  7  "));

    [Fact] public void ToInt_InvalidString_ReturnsNull()
        => Assert.Null(KSR.Text.Text.ToInt("abc"));

    [Fact] public void ToDouble_ValidString_ReturnsValue()
        => Assert.Equal(3.14, KSR.Text.Text.ToDouble("3.14")!.Value, precision: 10);

    [Fact] public void ToDouble_InvalidString_ReturnsNull()
        => Assert.Null(KSR.Text.Text.ToDouble("xyz"));

    // ── whitespace ────────────────────────────────────────────────────────────

    [Fact] public void Trim_RemovesWhitespace()
        => Assert.Equal("hello", KSR.Text.Text.Trim("  hello  "));

    [Fact] public void TrimStart_RemovesLeading()
        => Assert.Equal("hello  ", KSR.Text.Text.TrimStart("  hello  "));

    [Fact] public void TrimEnd_RemovesTrailing()
        => Assert.Equal("  hello", KSR.Text.Text.TrimEnd("  hello  "));

    // ── case ──────────────────────────────────────────────────────────────────

    [Fact] public void ToUpper_ReturnsUpperCase()
        => Assert.Equal("HELLO", KSR.Text.Text.ToUpper("hello"));

    [Fact] public void ToLower_ReturnsLowerCase()
        => Assert.Equal("hello", KSR.Text.Text.ToLower("HELLO"));

    // ── search ────────────────────────────────────────────────────────────────

    [Fact] public void Contains_Match_ReturnsTrue()
        => Assert.True(KSR.Text.Text.Contains("hello world", "world"));

    [Fact] public void Contains_NoMatch_ReturnsFalse()
        => Assert.False(KSR.Text.Text.Contains("hello", "xyz"));

    [Fact] public void StartsWith_Match_ReturnsTrue()
        => Assert.True(KSR.Text.Text.StartsWith("hello", "hel"));

    [Fact] public void EndsWith_Match_ReturnsTrue()
        => Assert.True(KSR.Text.Text.EndsWith("hello", "llo"));

    [Fact] public void IsEmpty_EmptyString_ReturnsTrue()
        => Assert.True(KSR.Text.Text.IsEmpty(""));

    [Fact] public void IsBlank_Whitespace_ReturnsTrue()
        => Assert.True(KSR.Text.Text.IsBlank("   "));

    // ── manipulation ──────────────────────────────────────────────────────────

    [Fact] public void Length_ReturnsCount()
        => Assert.Equal(5, KSR.Text.Text.Length("hello"));

    [Fact] public void Replace_ReplacesAll()
        => Assert.Equal("herro", KSR.Text.Text.Replace("hello", "l", "r"));

    [Fact] public void Repeat_RepeatsString()
        => Assert.Equal("ababab", KSR.Text.Text.Repeat("ab", 3));

    [Fact] public void Split_ReturnsParts()
    {
        var parts = KSR.Text.Text.Split("a,b,c", ",");
        Assert.Equal(["a", "b", "c"], parts);
    }

    [Fact] public void Split_WithLimit_ReturnsLimitedParts()
    {
        var parts = KSR.Text.Text.Split("a,b,c", ",", 2);
        Assert.Equal(["a", "b,c"], parts);
    }

    [Fact] public void Join_CombinesParts()
        => Assert.Equal("a-b-c", KSR.Text.Text.Join("-", ["a", "b", "c"]));

    [Fact] public void Substring_ReturnsSlice()
        => Assert.Equal("ell", KSR.Text.Text.Substring("hello", 1, 3));

    [Fact] public void Drop_ReturnsFromIndex()
        => Assert.Equal("llo", KSR.Text.Text.Drop("hello", 2));

    [Fact] public void Take_ReturnsFirstN()
        => Assert.Equal("he", KSR.Text.Text.Take("hello", 2));

    [Fact] public void IndexOf_Found_ReturnsIndex()
        => Assert.Equal(2, KSR.Text.Text.IndexOf("hello", "ll"));

    [Fact] public void IndexOf_NotFound_ReturnsMinusOne()
        => Assert.Equal(-1, KSR.Text.Text.IndexOf("hello", "xyz"));

    [Fact] public void PadLeft_PadsToWidth()
        => Assert.Equal("  hi", KSR.Text.Text.PadLeft("hi", 4));
}

// ─────────────────────────────────────────────────────────────────────────────
//  KSR.Io — Path unit tests  (file-system independent)
// ─────────────────────────────────────────────────────────────────────────────

public class IoPathTests
{
    [Fact] public void Extension_ReturnsDotExt()
        => Assert.Equal(".txt", KSR.Io.Path.Extension("file.txt"));

    [Fact] public void Extension_NoExtension_ReturnsEmpty()
        => Assert.Equal("", KSR.Io.Path.Extension("file"));

    [Fact] public void FileName_ReturnsName()
        => Assert.Equal("file.txt", KSR.Io.Path.FileName("/some/dir/file.txt"));

    [Fact] public void FileStem_ReturnsNameWithoutExt()
        => Assert.Equal("file", KSR.Io.Path.FileStem("/some/dir/file.txt"));

    [Fact] public void IsAbsolute_AbsolutePath_ReturnsTrue()
        => Assert.True(KSR.Io.Path.IsAbsolute("/absolute/path"));
}
