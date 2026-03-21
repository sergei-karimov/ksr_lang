import * as vscode from 'vscode';
import * as cp     from 'child_process';
import * as path   from 'path';
import * as fs     from 'fs';

// ─────────────────────────────────────────────────────────────────────────────
//  KSR Language Extension
//  Provides live error diagnostics by running `ksr check <file>` on save.
// ─────────────────────────────────────────────────────────────────────────────

const COLLECTION = vscode.languages.createDiagnosticCollection('ksr');

export function activate(context: vscode.ExtensionContext) {
    // Check all already-open KSR documents
    vscode.workspace.textDocuments.forEach(check);

    context.subscriptions.push(
        COLLECTION,
        vscode.workspace.onDidOpenTextDocument(check),
        vscode.workspace.onDidSaveTextDocument(check),
        vscode.workspace.onDidCloseTextDocument(doc => COLLECTION.delete(doc.uri)),
    );
}

export function deactivate() {
    COLLECTION.dispose();
}

// ─────────────────────────────────────────────────────────────────────────────

function check(doc: vscode.TextDocument): void {
    if (doc.languageId !== 'ksr') return;

    const cfg     = vscode.workspace.getConfiguration('ksr');
    if (!cfg.get<boolean>('enableDiagnostics', true)) {
        COLLECTION.delete(doc.uri);
        return;
    }

    const exe = resolveExecutable(cfg.get<string>('executablePath', 'ksr'));
    if (!exe) {
        // ksr not installed — clear silently (don't spam errors)
        COLLECTION.delete(doc.uri);
        return;
    }

    cp.execFile(exe, ['check', doc.fileName], { timeout: 10_000 },
        (_err, stdout, _stderr) => {
            try {
                const items: Array<{ message: string; line: number; col: number }> =
                    JSON.parse(stdout.trim() || '[]');

                const diagnostics = items.map(item => {
                    const line = Math.max(0, (item.line ?? 1) - 1);
                    const col  = Math.max(0, (item.col  ?? 1) - 1);
                    // Highlight to end-of-line so the squiggle is visible
                    const range = new vscode.Range(line, col, line, Number.MAX_SAFE_INTEGER);
                    const diag  = new vscode.Diagnostic(
                        range,
                        item.message,
                        vscode.DiagnosticSeverity.Error,
                    );
                    diag.source = 'ksr';
                    return diag;
                });

                COLLECTION.set(doc.uri, diagnostics);
            } catch {
                // Unparseable output — clear and move on
                COLLECTION.set(doc.uri, []);
            }
        }
    );
}

// ─────────────────────────────────────────────────────────────────────────────

/**
 * Tries to locate the ksr executable.
 * Returns the resolved path, or null if not found.
 */
function resolveExecutable(configured: string): string | null {
    // User configured an absolute or relative path
    if (path.isAbsolute(configured) || configured.includes(path.sep)) {
        return fs.existsSync(configured) ? configured : null;
    }

    // Try well-known locations (for self-contained publish)
    const candidates = [
        'C:\\Program Files\\ksr\\ksr.exe',
        path.join(process.env['USERPROFILE'] ?? '', '.ksr', 'ksr.exe'),
        '/usr/local/bin/ksr',
        '/usr/bin/ksr',
    ];

    for (const c of candidates) {
        if (fs.existsSync(c)) return c;
    }

    // Fall back: rely on PATH (let execFile handle it)
    return configured; // 'ksr' — will fail gracefully if not found
}
