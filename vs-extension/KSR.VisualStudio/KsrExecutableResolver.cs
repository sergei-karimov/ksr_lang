using System;
using System.Collections.Generic;
using System.IO;

namespace KSR.VisualStudio;

/// <summary>Resolves the path to the Kestrel executable.</summary>
internal static class KsrExecutableResolver
{
    /// <summary>
    /// Returns the best absolute path to the Kestrel executable, or <paramref name="configured"/>
    /// if no absolute path can be verified (caller relies on PATH resolution).
    /// Returns <see langword="null"/> only when <paramref name="configured"/> is an absolute
    /// path that does not exist on disk.
    /// </summary>
    /// <param name="configured">The user-configured value (may be a name, relative path, or absolute path).</param>
    /// <param name="fileExists">
    /// Optional override for <see cref="File.Exists"/> — injected in unit tests to avoid disk I/O.
    /// </param>
    /// <param name="pathValue">Optional PATH override for tests; defaults to the process PATH.</param>
    internal static string? Resolve(
        string configured,
        Func<string, bool>? fileExists = null,
        string? pathValue = null)
    {
        fileExists ??= File.Exists;

        if (Path.IsPathRooted(configured))
            return fileExists(configured) ? configured : null;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] installDirectories =
        [
            Path.Combine(userProfile, ".dotnet", "tools"),
            Path.Combine(userProfile, ".kestrel"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Kestrel"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Kestrel"),
            Path.Combine(userProfile, ".ksr"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ksr"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ksr"),
        ];

        string[] canonicalNames = ["kestrel.exe", "kestrel.cmd", "kestrel.ps1", "kestrel"];
        string[] legacyNames = ["ksr.cmd", "ksr.ps1", "ksr.exe", "ksr"];

        var candidates = new List<string>();
        AddCandidates(candidates, installDirectories, canonicalNames);
        AddCandidates(candidates, installDirectories, legacyNames);

        var pathDirectories = (pathValue ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
        AddCandidates(candidates, pathDirectories, canonicalNames);
        AddCandidates(candidates, pathDirectories, legacyNames);

        foreach (var candidate in candidates)
            if (fileExists(candidate)) return candidate;

        return configured;
    }

    private static void AddCandidates(
        List<string> candidates,
        IEnumerable<string> directories,
        IEnumerable<string> names)
    {
        foreach (var directory in directories)
        foreach (var name in names)
            candidates.Add(Path.Combine(directory, name));
    }
}
