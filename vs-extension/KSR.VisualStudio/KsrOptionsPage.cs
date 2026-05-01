using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace KSR.VisualStudio;

/// <summary>
/// Tools → Options → KSR → General
/// Stores user-configurable settings for the KSR language extension.
/// </summary>
[System.Runtime.InteropServices.ComVisible(true)]
public sealed class KsrOptionsPage : DialogPage
{
    private string _executablePath = "ksr";

    [Category("Language Server")]
    [DisplayName("KSR Executable Path")]
    [Description(
        "Path to the ksr executable used to start the Language Server. " +
        "Defaults to 'ksr' (resolved via PATH). " +
        "Example: C:\\Users\\you\\.ksr\\ksr.exe")]
    public string ExecutablePath
    {
        get => _executablePath;
        set => _executablePath = string.IsNullOrWhiteSpace(value) ? "ksr" : value;
    }
}
