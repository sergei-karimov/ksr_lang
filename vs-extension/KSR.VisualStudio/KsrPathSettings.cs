namespace KSR.VisualStudio;

/// <summary>Pure path normalization helpers; no VS SDK dependencies.</summary>
internal static class KsrPathSettings
{
    internal const string DefaultExecutableName = "ksr";

    /// <summary>
    /// Returns <paramref name="value"/> unchanged unless it is null or whitespace-only,
    /// in which case the default <c>"ksr"</c> name is returned.
    /// </summary>
    internal static string NormalizePath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DefaultExecutableName : value;
}
