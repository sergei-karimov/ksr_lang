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
//  KSR Language Extension
//
//  Starts `ksr lsp` as a child process and connects via the Language Server
//  Protocol (JSON-RPC over stdio). This gives us:
//    • Real-time diagnostics (error squiggles as you type)
//    • Keyword + symbol completions
//    • Hover documentation
// ─────────────────────────────────────────────────────────────────────────────

let client: LanguageClient | undefined;

export function activate(context: vscode.ExtensionContext): void {
    const cfg = vscode.workspace.getConfiguration('ksr');
    const exe = resolveExecutable(cfg.get<string>('executablePath', 'ksr'));

    if (!exe) {
        // ksr not installed — skip silently (no spam)
        return;
    }

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
        'KSR Language Server',
        serverOptions,
        clientOptions,
    );

    client.start();
    context.subscriptions.push(client);
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}

// ─────────────────────────────────────────────────────────────────────────────

/**
 * Resolves the ksr executable path.
 * Returns the path string to pass to child_process, or null if not found.
 */
function resolveExecutable(configured: string): string | null {
    // Absolute or relative path configured by user
    if (path.isAbsolute(configured) || configured.includes(path.sep)) {
        return fs.existsSync(configured) ? configured : null;
    }

    // Well-known install locations
    const candidates = [
        'C:\\Program Files\\ksr\\ksr.exe',
        path.join(process.env['USERPROFILE'] ?? '', '.ksr', 'ksr.exe'),
        '/usr/local/bin/ksr',
        '/usr/bin/ksr',
    ];

    for (const c of candidates) {
        if (fs.existsSync(c)) return c;
    }

    // Fall back to PATH resolution ('ksr') — fails gracefully if absent
    return configured;
}
