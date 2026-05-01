using System;
using System.IO;

namespace KSR.VisualStudio;

/// <summary>Resolves the path to the ksr executable.</summary>
internal static class KsrExecutableResolver
{
    /// <summary>
    /// Returns the best absolute path to the ksr executable, or <paramref name="configured"/>
    /// if no absolute path can be verified (caller relies on PATH resolution).
    /// Returns <see langword="null"/> only when <paramref name="configured"/> is an absolute
    /// path that does not exist on disk.
    /// </summary>
    /// <param name="configured">The user-configured value (may be a name, relative path, or absolute path).</param>
    /// <param name="fileExists">
    /// Optional override for <see cref="File.Exists"/> — injected in unit tests to avoid disk I/O.
    /// </param>
    internal static string? Resolve(string configured, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        if (Path.IsPathRooted(configured))
            return fileExists(configured) ? configured : null;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Path.Combine(userProfile, ".ksr", "ksr.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ksr", "ksr.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ksr", "ksr.exe"),
        ];

        foreach (var c in candidates)
            if (fileExists(c)) return c;

        return configured;
    }
}
