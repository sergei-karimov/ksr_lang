import * as vscode from 'vscode';
import * as path    from 'path';
import * as fs      from 'fs';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind,
} from 'vscode-languageclient/node';

// ─────────────────────────────────────────────────────────────────────────────
//  Kestrel Language Extension
//
//  Starts `kestrel lsp` as a child process and connects via the Language Server
//  Protocol (JSON-RPC over stdio). This gives us:
//    • Real-time diagnostics (error squiggles as you type)
//    • Keyword + symbol completions
//    • Hover documentation
// ─────────────────────────────────────────────────────────────────────────────

let client: LanguageClient | undefined;

export function activate(context: vscode.ExtensionContext): void {
    // ── Language Server ────────────────────────────────────────────────────────

    const cfg = vscode.workspace.getConfiguration('ksr');
    const exe = resolveExecutable(cfg.get<string>('executablePath', 'kestrel'));

    if (exe) {
        const serverOptions: ServerOptions = {
            command:   exe,
            args:      ['lsp'],
            transport: TransportKind.stdio,
        };

        const clientOptions: LanguageClientOptions = {
            documentSelector: [{ scheme: 'file', language: 'ksr' }],
            synchronize: {
                // Re-validate when any .ksr file changes on disk
                fileEvents: vscode.workspace.createFileSystemWatcher('**/*.ksr'),
            },
        };

        client = new LanguageClient(
            'ksr',
            'Kestrel Language Server',
            serverOptions,
            clientOptions,
        );

        client.start();
        context.subscriptions.push(client);
    }

    // ── Debug support ──────────────────────────────────────────────────────────

    context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider('ksr', {

            // Called when no launch.json exists — provides the initial config list.
            provideDebugConfigurations(): vscode.DebugConfiguration[] {
                return [defaultLaunchConfig()];
            },

            // Called before every debug session.  Transforms type "ksr" → "coreclr"
            // so the C# extension's debug adapter handles the actual execution.
            resolveDebugConfiguration(
                _folder: vscode.WorkspaceFolder | undefined,
                config: vscode.DebugConfiguration,
            ): vscode.DebugConfiguration | null {
                // Fill in defaults when launched without a launch.json (F5)
                if (!config.type && !config.request && !config.name) {
                    return defaultLaunchConfig();
                }

                // Warn if neither C# extension variant is installed
                const hasCsharp =
                    vscode.extensions.getExtension('ms-dotnettools.csharp') ||
                    vscode.extensions.getExtension('ms-dotnettools.csdevkit');
                if (!hasCsharp) {
                    vscode.window.showWarningMessage(
                        'Kestrel debugging requires the C# extension. Please install "ms-dotnettools.csharp" from the Marketplace.',
                        'Install',
                    ).then(choice => {
                        if (choice === 'Install') {
                            vscode.commands.executeCommand(
                                'workbench.extensions.search',
                                'ms-dotnettools.csharp',
                            );
                        }
                    });
                    return null;
                }

                // Remap type "ksr" → "coreclr" (delegate to C# debug adapter)
                config.type = 'coreclr';

                // Ensure requireExactSource is off so the debugger accepts .ksr files
                if (config.requireExactSource === undefined) {
                    config.requireExactSource = false;
                }

                return config;
            },
        }),
    );
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}

// ─────────────────────────────────────────────────────────────────────────────

/** Returns a default coreclr launch configuration for a Kestrel console project. */
function defaultLaunchConfig(): vscode.DebugConfiguration {
    return {
        name: 'Debug Kestrel',
        type: 'coreclr',
        request: 'launch',
        preLaunchTask: 'build',
        program: '${workspaceFolder}/bin/Debug/net8.0/${workspaceFolderBasename}.dll',
        args: [],
        cwd: '${workspaceFolder}',
        console: 'internalConsole',
        stopAtEntry: false,
        requireExactSource: false,
    };
}

// ─────────────────────────────────────────────────────────────────────────────

/**
 * Resolves the Kestrel executable path, with the legacy ksr name as a fallback.
 * Returns the path string to pass to child_process, or null if not found.
 */
function resolveExecutable(configured: string): string | null {
    // Absolute or relative path configured by user
    if (isExplicitPath(configured) && fs.existsSync(configured)) return configured;

    // Resolve through PATH in canonical-first order. The explicit configured
    // path above remains untouched, including legacy workspace settings.
    const canonical = findOnPath('kestrel');
    if (canonical) return canonical;

    const legacy = findOnPath('ksr');
    if (legacy) return legacy;

    // Let child_process perform the final PATH lookup so a shell-managed
    // environment still works even when PATH is not visible to this process.
    return isExplicitPath(configured) ? null : configured;
}

function isExplicitPath(value: string): boolean {
    return path.isAbsolute(value) || value.includes(path.sep) || value.includes('/');
}

function findOnPath(command: string): string | null {
    const pathValue = process.env['PATH'] ?? '';
    const candidates = pathValue.split(path.delimiter).filter(Boolean);
    const names = process.platform === 'win32' ? [command, `${command}.exe`] : [command];

    for (const directory of candidates) {
        for (const name of names) {
            const candidate = path.join(directory, name);
            if (fs.existsSync(candidate)) return candidate;
        }
    }

    return null;
}
