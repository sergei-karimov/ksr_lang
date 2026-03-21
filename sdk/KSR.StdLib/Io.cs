namespace KSR.Io;

// ─────────────────────────────────────────────────────────────────────────────
//  ksr.io  —  file system and console I/O
//
//  KSR usage:
//    use ksr.io
//    val line: String? = IO.readLine()
//    val n: Int?       = IO.readInt()
//    IO.print("hello ")
//    val txt = File.read("data.txt")
//    val p   = Path.join("dir", "file.txt")
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Console I/O helpers.</summary>
public static class IO
{
    /// <summary>Reads a line from stdin. Returns null on EOF.</summary>
    public static string? ReadLine() => Console.ReadLine();

    /// <summary>Reads a line and parses it as Int. Returns null on failure.</summary>
    public static int? ReadInt()
    {
        var line = Console.ReadLine();
        return int.TryParse(line?.Trim(), out var n) ? n : null;
    }

    /// <summary>Reads a line and parses it as Double. Returns null on failure.</summary>
    public static double? ReadDouble()
    {
        var line = Console.ReadLine();
        return double.TryParse(
            line?.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) ? d : null;
    }

    /// <summary>Writes text without a newline.</summary>
    public static void Print(string s) => Console.Write(s);
}

/// <summary>File system helpers.</summary>
public static class File
{
    /// <summary>Reads the entire file as a string.</summary>
    public static string Read(string path) =>
        System.IO.File.ReadAllText(path);

    /// <summary>Writes (or overwrites) a file with the given content.</summary>
    public static void Write(string path, string content) =>
        System.IO.File.WriteAllText(path, content);

    /// <summary>Appends text to a file (creates it if it does not exist).</summary>
    public static void Append(string path, string content) =>
        System.IO.File.AppendAllText(path, content);

    /// <summary>Returns all lines of a file as a List.</summary>
    public static List<string> Lines(string path) =>
        [.. System.IO.File.ReadAllLines(path)];

    /// <summary>Returns true if the path points to an existing file.</summary>
    public static bool Exists(string path) =>
        System.IO.File.Exists(path);

    /// <summary>Deletes the file if it exists.</summary>
    public static void Delete(string path)
    {
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    /// <summary>Copies src to dst, overwriting dst if it exists.</summary>
    public static void Copy(string src, string dst) =>
        System.IO.File.Copy(src, dst, overwrite: true);
}

/// <summary>File-path helpers.</summary>
public static class Path
{
    /// <summary>Joins two path segments.</summary>
    public static string Join(string a, string b) =>
        System.IO.Path.Combine(a, b);

    /// <summary>Joins three path segments.</summary>
    public static string Join(string a, string b, string c) =>
        System.IO.Path.Combine(a, b, c);

    /// <summary>Returns the file extension including the dot, e.g. ".txt". Returns empty string if none.</summary>
    public static string Extension(string path) =>
        System.IO.Path.GetExtension(path) ?? "";

    /// <summary>Returns the file name with extension, e.g. "file.txt".</summary>
    public static string FileName(string path) =>
        System.IO.Path.GetFileName(path) ?? "";

    /// <summary>Returns the file name without extension, e.g. "file".</summary>
    public static string FileStem(string path) =>
        System.IO.Path.GetFileNameWithoutExtension(path) ?? "";

    /// <summary>Returns the directory part of the path. Returns empty string for a root path.</summary>
    public static string Directory(string path) =>
        System.IO.Path.GetDirectoryName(path) ?? "";

    /// <summary>Returns the absolute (full) path.</summary>
    public static string Absolute(string path) =>
        System.IO.Path.GetFullPath(path);

    /// <summary>Returns true if the path is rooted (absolute).</summary>
    public static bool IsAbsolute(string path) =>
        System.IO.Path.IsPathRooted(path);
}
