using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace KSR.VisualStudio;

/// <summary>
/// Tools → Options → Kestrel → General
/// Stores user-configurable settings for the Kestrel language extension.
/// </summary>
[System.Runtime.InteropServices.ComVisible(true)]
public sealed class KsrOptionsPage : DialogPage
{
    private string _executablePath = KsrPathSettings.DefaultExecutableName;

    [Category("Language Server")]
    [DisplayName("Kestrel Executable Path")]
    [Description(
        "Path to the Kestrel executable used to start the Language Server. " +
        "Defaults to 'kestrel' (resolved via PATH), with 'ksr' as a legacy fallback. " +
        "Example: C:\\Users\\you\\.kestrel\\kestrel.exe")]
    public string ExecutablePath
    {
        get => _executablePath;
        set => _executablePath = KsrPathSettings.NormalizePath(value);
    }
}
