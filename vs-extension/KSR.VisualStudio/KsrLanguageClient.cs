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
/// Starts `ksr lsp` as a child process and exposes it to Visual Studio
/// as an LSP language client.  VS routes all .ksr documents through this
/// client, which provides diagnostics, completions, and hover.
/// </summary>
[ContentType("ksr")]
[Export(typeof(ILanguageClient))]
public sealed class KsrLanguageClient : ILanguageClient
{
    private Process? _serverProcess;

    // ── ILanguageClient ───────────────────────────────────────────────────────

    public string Name => "KSR Language Server";

    /// <summary>
    /// VS uses these section names to forward workspace configuration to the
    /// LSP server via workspace/configuration requests.
    /// </summary>
    public IEnumerable<string>? ConfigurationSections => new[] { "ksr" };

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
    /// Resolves the ksr executable, starts `ksr lsp`, and returns stdio streams.
    /// </summary>
    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        var exe = await ResolveExecutableAsync(token);
        if (exe is null)
        {
            ShowExecutableNotFoundMessage();
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = "lsp",
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = false,
            CreateNoWindow         = true,
        };

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };
        process.Exited += OnServerExited;

        try
        {
            if (!process.Start())
            {
                process.Exited -= OnServerExited;
                process.Dispose();
                ShowExecutableNotFoundMessage();
                return null;
            }
        }
        catch (Win32Exception)
        {
            process.Exited -= OnServerExited;
            process.Dispose();
            ShowExecutableNotFoundMessage();
            return null;
        }

        _serverProcess = process;

        return new Connection(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
    }

    public Task OnServerInitializedAsync() => Task.CompletedTask;

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(
        ILanguageClientInitializationInfo initializationFailureContext)
        => Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = initializationFailureContext.StatusMessage
                ?? initializationFailureContext.InitializationException?.Message
                ?? "KSR language server failed to initialize."
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the configured executable path from the options page, then searches:
    ///   1. The path as-is (if absolute)
    ///   2. %USERPROFILE%\.ksr\ksr.exe
    ///   3. Falls back to just "ksr" (relies on PATH — returns it even if not confirmed)
    /// </summary>
    private static async Task<string?> ResolveExecutableAsync(CancellationToken token)
    {
        var configured = "ksr";
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
        catch
        {
            // Options page is unavailable; use the default executable resolution.
        }

        return await Task.Run(() => ResolveExecutablePath(configured), token);
    }

    private static string? ResolveExecutablePath(string configured)
    {
        if (Path.IsPathRooted(configured))
            return File.Exists(configured) ? configured : null;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, ".ksr", "ksr.exe"),
            @"C:\Program Files\ksr\ksr.exe",
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return configured;
    }

    private static void ShowExecutableNotFoundMessage()
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                "KSR executable not found. Install KSR or set the path under " +
                "Tools → Options → KSR → General → KSR Executable Path.",
                "KSR Language Server",
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
