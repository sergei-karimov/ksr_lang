using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace KSR.VisualStudio;

/// <summary>
/// VS Package entry point.
/// Responsibilities: options-page registration only.
/// Language features (LSP) are handled by KsrLanguageClient via MEF.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideOptionPage(
    typeof(KsrOptionsPage),
    categoryName:    "KSR",
    pageName:        "General",
    categoryResourceID: 0,
    pageNameResourceID: 0,
    supportsAutomation: true)]
public sealed class KsrPackage : AsyncPackage
{
    public const string PackageGuidString = "4A1F9B3C-E7D2-4C8A-B5F0-9E3D6A2C1B7F";

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
    }
}
