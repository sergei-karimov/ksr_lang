using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace KSR.VisualStudio;

/// <summary>
/// Starts `kestrel lsp` as a child process and exposes it to Visual Studio
/// as an LSP language client.  VS routes all .ksr documents through this
/// client, which provides diagnostics, completions, and hover.
/// </summary>
[ContentType("ksr")]
[Export(typeof(ILanguageClient))]
public sealed class KsrLanguageClient : ILanguageClient
{
    private volatile Process? _serverProcess;

    // ── ILanguageClient ───────────────────────────────────────────────────────

    public string Name => "Kestrel Language Server";

    /// <summary>
    /// VS uses these section names to forward workspace configuration to the
    /// LSP server via workspace/configuration requests.
    /// </summary>
    private static readonly IEnumerable<string> s_configSections = new[] { "ksr" };
    public IEnumerable<string>? ConfigurationSections => s_configSections;

    public object? InitializationOptions => null;

    public IEnumerable<string>? FilesToWatch => null;

    public bool ShowNotificationOnInitializeFailed => true;

    public event AsyncEventHandler<EventArgs>? StartAsync;
    public event AsyncEventHandler<EventArgs>? StopAsync;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by VS when the extension is loaded.
    /// We fire StartAsync immediately so VS knows it can call ActivateAsync.
    /// </summary>
    public async Task OnLoadedAsync()
    {
        await (StartAsync?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Called by VS to launch the LSP server.
    /// Resolves the Kestrel executable, starts `kestrel lsp`, and returns stdio streams.
    /// </summary>
    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        var exe = await ResolveExecutableAsync(token);
        if (exe is null)
        {
            ShowExecutableNotFoundMessage();
            return null;
        }

        var psi = CreateStartInfo(exe, Path.DirectorySeparatorChar == '\\');
        psi.UseShellExecute        = false;
        psi.RedirectStandardInput  = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError  = false;
        psi.CreateNoWindow         = true;

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };

        // Assign before subscribing so OnServerExited can always null the field
        _serverProcess = process;
        process.Exited += OnServerExited;

        try
        {
            if (!process.Start())
            {
                _serverProcess = null;
                process.Exited -= OnServerExited;
                process.Dispose();
                ShowExecutableNotFoundMessage();
                return null;
            }
        }
        catch (Win32Exception)
        {
            _serverProcess = null;
            process.Exited -= OnServerExited;
            process.Dispose();
            ShowExecutableNotFoundMessage();
            return null;
        }

        return new Connection(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
    }

    public Task OnServerInitializedAsync() => Task.CompletedTask;

    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        bool isWindows,
        string? commandProcessor = null,
        string? powerShell = null)
    {
        if (!isWindows || !executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) &&
            !executable.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo(executable, "lsp");
        }

        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var shell = commandProcessor
                ?? Environment.GetEnvironmentVariable("ComSpec")
                ?? "cmd.exe";
            return new ProcessStartInfo(shell, $"/d /s /c \"\"{executable}\" lsp\"");
        }

        var powershellPath = powerShell ?? "powershell.exe";
        return new ProcessStartInfo(
            powershellPath,
            $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{executable}\" lsp");
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(
        ILanguageClientInitializationInfo initializationFailureContext)
        => Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = initializationFailureContext.StatusMessage
                ?? initializationFailureContext.InitializationException?.Message
                ?? "Kestrel language server failed to initialize."
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the configured executable path from the options page, then searches:
    ///   1. The path as-is (if absolute)
    ///   2. Kestrel install locations
    ///   3. Legacy ksr install locations
    ///   4. Falls back to the configured name for PATH resolution
    /// </summary>
    private static async Task<string?> ResolveExecutableAsync(CancellationToken token)
    {
        var configured = KsrPathSettings.DefaultExecutableName;
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            var shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
            if (shell is not null)
            {
                var packageGuid = new Guid(KsrPackage.PackageGuidString);
                shell.LoadPackage(ref packageGuid, out var package);
                if (package is KsrPackage pkg)
                {
                    var page = (KsrOptionsPage)pkg.GetDialogPage(typeof(KsrOptionsPage));
                    configured = page.ExecutablePath;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidCastException
                                 || ex is System.Runtime.InteropServices.COMException
                                 || ex is InvalidOperationException)
        {
            // Options page unavailable; use default resolution.
        }

        return await Task.Run(() => KsrExecutableResolver.Resolve(configured), token);
    }

    private static void ShowExecutableNotFoundMessage()
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                "Kestrel executable not found. Install Kestrel or set the path under " +
                "Tools → Options → Kestrel → General → Kestrel Executable Path.",
                "Kestrel Language Server",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        });
    }

    private void OnServerExited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            process.Exited -= OnServerExited;
            process.Dispose();
            if (ReferenceEquals(_serverProcess, process))
                _serverProcess = null;
        }

        ThreadHelper.JoinableTaskFactory.Run(
            () => StopAsync?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask);
    }
}
