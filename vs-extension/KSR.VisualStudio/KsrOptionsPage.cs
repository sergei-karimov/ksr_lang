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
    private string _executablePath = string.Empty;

    [Category("Language Server")]
    [DisplayName("KSR Executable Path")]
    [Description(
        "Full path to the ksr executable. " +
        "Leave blank to use the default: %USERPROFILE%\\.ksr\\ksr.exe, " +
        "falling back to PATH lookup.")]
    public string ExecutablePath
    {
        get => _executablePath;
        set => _executablePath = value ?? string.Empty;
    }
}
