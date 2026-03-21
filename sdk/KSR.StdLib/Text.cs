namespace KSR.Text;

// ─────────────────────────────────────────────────────────────────────────────
//  ksr.text  —  string utilities
//
//  KSR usage:
//    use ksr.text
//    val n: Int?     = Text.toInt("42")
//    val s: String   = Text.trim("  hi  ")
//    val parts       = Text.split("a,b", ",")
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>String utility functions.</summary>
public static class Text
{
    // ── parsing ───────────────────────────────────────────────────────────────

    /// <summary>Parses an integer. Returns null on failure.</summary>
    public static int? ToInt(string s) =>
        int.TryParse(s.Trim(), out var n) ? n : null;

    /// <summary>Parses a double. Returns null on failure.</summary>
    public static double? ToDouble(string s) =>
        double.TryParse(
            s.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) ? d : null;

    // ── whitespace ────────────────────────────────────────────────────────────

    /// <summary>Removes leading and trailing whitespace.</summary>
    public static string Trim(string s) => s.Trim();

    /// <summary>Removes leading whitespace.</summary>
    public static string TrimStart(string s) => s.TrimStart();

    /// <summary>Removes trailing whitespace.</summary>
    public static string TrimEnd(string s) => s.TrimEnd();

    // ── case ──────────────────────────────────────────────────────────────────

    /// <summary>Converts to upper-case.</summary>
    public static string ToUpper(string s) => s.ToUpperInvariant();

    /// <summary>Converts to lower-case.</summary>
    public static string ToLower(string s) => s.ToLowerInvariant();

    // ── search / test ─────────────────────────────────────────────────────────

    /// <summary>Returns true if s contains sub.</summary>
    public static bool Contains(string s, string sub) => s.Contains(sub);

    /// <summary>Returns true if s starts with prefix.</summary>
    public static bool StartsWith(string s, string prefix) => s.StartsWith(prefix);

    /// <summary>Returns true if s ends with suffix.</summary>
    public static bool EndsWith(string s, string suffix) => s.EndsWith(suffix);

    /// <summary>Returns true if s has length 0.</summary>
    public static bool IsEmpty(string s) => s.Length == 0;

    /// <summary>Returns true if s is null, empty, or all whitespace.</summary>
    public static bool IsBlank(string s) => string.IsNullOrWhiteSpace(s);

    // ── manipulation ──────────────────────────────────────────────────────────

    /// <summary>Returns the number of characters in s.</summary>
    public static int Length(string s) => s.Length;

    /// <summary>Replaces all occurrences of oldValue with newValue.</summary>
    public static string Replace(string s, string oldValue, string newValue) =>
        s.Replace(oldValue, newValue);

    /// <summary>Repeats s n times.</summary>
    public static string Repeat(string s, int n) =>
        string.Concat(Enumerable.Repeat(s, n));

    /// <summary>Splits s on sep and returns all parts as a List.</summary>
    public static List<string> Split(string s, string sep) =>
        [.. s.Split(sep)];

    /// <summary>Splits s on sep and returns at most limit parts.</summary>
    public static List<string> Split(string s, string sep, int limit) =>
        [.. s.Split(sep, limit)];

    /// <summary>Joins a list of strings with sep between each element.</summary>
    public static string Join(string sep, List<string> parts) =>
        string.Join(sep, parts);

    /// <summary>Returns the character at index i as a one-character string.</summary>
    public static string CharAt(string s, int i) =>
        s[i].ToString();

    /// <summary>Returns a substring from start (inclusive) with the given length.</summary>
    public static string Substring(string s, int start, int length) =>
        s.Substring(start, length);

    /// <summary>Returns the part of s from start to the end.</summary>
    public static string Drop(string s, int start) =>
        s[start..];

    /// <summary>Returns the first n characters of s.</summary>
    public static string Take(string s, int n) =>
        s[..n];

    /// <summary>Pads s on the left with spaces to totalWidth.</summary>
    public static string PadLeft(string s, int totalWidth) =>
        s.PadLeft(totalWidth);

    /// <summary>Pads s on the left with padChar to totalWidth.</summary>
    public static string PadLeft(string s, int totalWidth, char padChar) =>
        s.PadLeft(totalWidth, padChar);

    /// <summary>Pads s on the right with spaces to totalWidth.</summary>
    public static string PadRight(string s, int totalWidth) =>
        s.PadRight(totalWidth);

    /// <summary>Returns the index of the first occurrence of sub in s, or -1.</summary>
    public static int IndexOf(string s, string sub) =>
        s.IndexOf(sub, StringComparison.Ordinal);

    /// <summary>Returns the index of the last occurrence of sub in s, or -1.</summary>
    public static int LastIndexOf(string s, string sub) =>
        s.LastIndexOf(sub, StringComparison.Ordinal);
}
