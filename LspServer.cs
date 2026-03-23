using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KSR.LSP;

// ─────────────────────────────────────────────────────────────────────────────
//  Minimal LSP (Language Server Protocol) server.
//
//  Transport:  JSON-RPC 2.0 over stdin / stdout  (Content-Length framing)
//  Capabilities provided:
//    • textDocumentSync = Full  — diagnostics on every change
//    • completionProvider       — keyword + symbol completions
//    • hoverProvider            — token info on hover
// ─────────────────────────────────────────────────────────────────────────────

public static class LspServer {
    // ── I/O streams ──────────────────────────────────────────────────────────

    private static readonly Stream _in = Console.OpenStandardInput();
    private static readonly Stream _out = Console.OpenStandardOutput();
    private static readonly object _writeLock = new();

    // ── JSON options ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── document store (uri → full text) ─────────────────────────────────────

    private static readonly Dictionary<string, string> _docs = new();

    // ── keyword list for completion ───────────────────────────────────────────

    private static readonly string[] _keywords =
    [
        "val", "var", "fun", "async", "await", "struct", "sealed", "is", "interface", "implement",
        "use", "new", "if", "else", "while", "for", "in", "return", "when",
        "this", "true", "false", "null",
    ];

    // ── built-in type names for completion ────────────────────────────────────

    private static readonly (string Name, string Detail)[] _builtinTypes =
    [
        ("Int",          "32-bit integer"),
        ("Long",         "64-bit integer"),
        ("Double",       "64-bit float"),
        ("Float",        "32-bit float"),
        ("Bool",         "boolean"),
        ("String",       "string"),
        ("Unit",         "void / no return value"),
        ("List",         "immutable list — List<T>"),
        ("MutableList",  "mutable list — MutableList<T>"),
        ("Map",          "immutable map — Map<K,V>"),
        ("MutableMap",   "mutable map — MutableMap<K,V>"),
    ];

    // ── stdlib symbols for completion ─────────────────────────────────────────

    private static readonly (string Name, string Detail)[] _stdlibSymbols =
    [
        ("IO",   "ksr.io — console I/O: readLine, readInt, readDouble, print"),
        ("File", "ksr.io — file operations: read, write, append, lines, exists, delete, copy"),
        ("Path", "ksr.io — path helpers: join, extension, fileName, fileStem, directory, absolute"),
        ("Text", "ksr.text — string utilities: toInt, toDouble, trim, split, join, replace, …"),
        ("Lst",  "ksr.collections — higher-order list ops: map, filter, fold, sorted, …"),
        ("Mp",   "ksr.collections — higher-order map ops: keys, values, mapValues, filter, …"),
    ];

    // ── stdlib module names for 'use' completion ──────────────────────────────

    private static readonly (string Name, string Detail)[] _stdlibModules =
    [
        ("ksr.io",          "Standard I/O library — IO, File, Path"),
        ("ksr.text",        "Standard text library — Text"),
        ("ksr.collections", "Standard collections library — Lst, Mp"),
    ];

    // ── entry point ───────────────────────────────────────────────────────────

    public static void Run() {
        // Redirect our own Console.Error so it doesn't corrupt the LSP stream.
        // (any internal tracing should go to a log file, not stderr)
        Console.SetError(TextWriter.Null);

        try {
            while (true) {
                var doc = ReadMessage();
                if (doc is null) break;
                HandleMessage(doc);
            }
        } catch (Exception) {
            // Server crashed — VS Code will restart it
        }
    }

    // ── message framing ───────────────────────────────────────────────────────

    private static JsonDocument? ReadMessage() {
        int contentLength = 0;

        // Read headers (terminated by blank line)
        while (true) {
            var line = ReadHeaderLine();
            if (line is null) return null;
            if (line.Length == 0) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line["Content-Length:".Length..].Trim());
        }

        if (contentLength <= 0) return null;

        var buf = new byte[contentLength];
        int offset = 0;
        while (offset < contentLength) {
            int n = _in.Read(buf, offset, contentLength - offset);
            if (n == 0) return null;
            offset += n;
        }

        return JsonDocument.Parse(buf);
    }

    private static string? ReadHeaderLine() {
        var sb = new StringBuilder();
        while (true) {
            int b = _in.ReadByte();
            if (b == -1) return null;
            if (b == '\r') {
                _in.ReadByte(); // consume \n
                return sb.ToString();
            }
            if (b == '\n') return sb.ToString();
            sb.Append((char)b);
        }
    }

    private static void WriteMessage(object msg) {
        var json = JsonSerializer.Serialize(msg, _jsonOpts);
        var body = Encoding.UTF8.GetBytes(json);
        var hdr = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        lock (_writeLock) {
            _out.Write(hdr);
            _out.Write(body);
            _out.Flush();
        }
    }

    // ── dispatch ──────────────────────────────────────────────────────────────

    private static void HandleMessage(JsonDocument doc) {
        var root = doc.RootElement;

        // Extract id (may be string or integer)
        string? id = null;
        if (root.TryGetProperty("id", out var idElem))
            id = idElem.ValueKind == JsonValueKind.Number
                ? idElem.GetInt32().ToString()
                : idElem.GetString();

        if (!root.TryGetProperty("method", out var methodElem)) return;
        var method = methodElem.GetString() ?? "";

        root.TryGetProperty("params", out var p);

        switch (method) {
            // ── lifecycle ────────────────────────────────────────────────────

            case "initialize":
                SendResult(id, new {
                    capabilities = new {
                        textDocumentSync = 1,   // Full
                        completionProvider = new { triggerCharacters = Array.Empty<string>() },
                        hoverProvider = true,
                    },
                    serverInfo = new { name = "KSR Language Server", version = "0.1.0" },
                });
                break;

            case "initialized":
                break; // notification — no response

            case "shutdown":
                SendResult(id, (object?)null);
                break;

            case "exit":
                Environment.Exit(0);
                break;

            // ── text document sync ───────────────────────────────────────────

            case "textDocument/didOpen": {
                    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
                    var text = p.GetProperty("textDocument").GetProperty("text").GetString()!;
                    _docs[uri] = text;
                    PublishDiagnostics(uri, text);
                    break;
                }

            case "textDocument/didChange": {
                    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
                    var changes = p.GetProperty("contentChanges");
                    // Full sync: the last change contains the full document text
                    var text = changes[changes.GetArrayLength() - 1].GetProperty("text").GetString()!;
                    _docs[uri] = text;
                    PublishDiagnostics(uri, text);
                    break;
                }

            case "textDocument/didSave": {
                    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
                    if (_docs.TryGetValue(uri, out var text))
                        PublishDiagnostics(uri, text);
                    break;
                }

            case "textDocument/didClose": {
                    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
                    _docs.Remove(uri);
                    SendNotification("textDocument/publishDiagnostics",
                        new { uri, diagnostics = Array.Empty<object>() });
                    break;
                }

            // ── completion ───────────────────────────────────────────────────

            case "textDocument/completion": {
                    var items = _keywords
                        .Select(kw => new { label = kw, kind = 14 /* Keyword */, detail = "keyword" })
                        .Cast<object>()
                        .ToList();

                    // Built-in types
                    foreach (var (name, detail) in _builtinTypes)
                        items.Add(new { label = name, kind = 25 /* TypeParameter */, detail });

                    // Stdlib class symbols
                    foreach (var (name, detail) in _stdlibSymbols)
                        items.Add(new { label = name, kind = 7 /* Class */, detail });

                    // Stdlib module names (for 'use' completions)
                    foreach (var (name, detail) in _stdlibModules)
                        items.Add(new { label = name, kind = 9 /* Module */, detail });

                    // Data-class, interface, and function names from the current document
                    if (p.TryGetProperty("textDocument", out var td)) {
                        var uri = td.GetProperty("uri").GetString()!;
                        if (_docs.TryGetValue(uri, out var docText)) {
                            foreach (var decl in SafeParse(docText)) {
                                if (decl is KSR.AST.StructDecl dc)
                                    items.Add(new { label = dc.Name, kind = 22 /* Struct */, detail = "struct" });
                                if (decl is KSR.AST.SealedDecl sd)
                                {
                                    items.Add(new { label = sd.Name, kind = 22 /* Struct */, detail = "sealed" });
                                    foreach (var v in sd.Variants)
                                        items.Add(new { label = v.Name, kind = 22, detail = $"variant of {sd.Name}" });
                                }
                                if (decl is KSR.AST.InterfaceDecl ifd)
                                    items.Add(new { label = ifd.Name, kind = 8 /* Interface */, detail = "interface" });
                                if (decl is KSR.AST.FunctionDecl fd)
                                    items.Add(new { label = fd.Name, kind = 3 /* Function */, detail = "fun" });
                            }
                        }
                    }

                    SendResult(id, new { isIncomplete = false, items });
                    break;
                }

            // ── hover ────────────────────────────────────────────────────────

            case "textDocument/hover": {
                    if (p.TryGetProperty("textDocument", out var tdHov) &&
                        p.TryGetProperty("position", out var pos)) {
                        var uri = tdHov.GetProperty("uri").GetString()!;
                        var line = pos.GetProperty("line").GetInt32();
                        var ch = pos.GetProperty("character").GetInt32();
                        SendResult(id, GetHover(uri, line, ch));
                    } else {
                        SendResult(id, (object?)null);
                    }
                    break;
                }

            default:
                // Unknown request — reply null so client doesn't time out
                if (id is not null) SendResult(id, (object?)null);
                break;
        }
    }

    // ── diagnostics ───────────────────────────────────────────────────────────

    private static void PublishDiagnostics(string uri, string text) {
        var diags = new List<object>();
        try {
            var tokens = new KSR.Lexer.Lexer(text).Tokenize();
            new KSR.Parser.Parser(tokens).Parse();
        } catch (KSR.Lexer.KsrLexException ex) {
            diags.Add(MakeDiag(ex.Message, ex.Line - 1, ex.Col - 1));
        } catch (KSR.Parser.KsrParseException ex) {
            diags.Add(MakeDiag(ex.Message, ex.Line - 1, ex.Col - 1));
        } catch { /* ignore other errors */ }

        SendNotification("textDocument/publishDiagnostics", new { uri, diagnostics = diags });
    }

    private static object MakeDiag(string message, int line, int col) => new {
        range = new {
            start = new { line = Math.Max(0, line), character = Math.Max(0, col) },
            end = new { line = Math.Max(0, line), character = 1000 },
        },
        severity = 1, // Error
        message,
        source = "ksr",
    };

    // ── hover ─────────────────────────────────────────────────────────────────

    private static object? GetHover(string uri, int lspLine, int lspChar) {
        if (!_docs.TryGetValue(uri, out var text)) return null;

        try {
            var tokens = new KSR.Lexer.Lexer(text).Tokenize();
            // LSP uses 0-based lines/chars; KSR lexer uses 1-based
            int ksrLine = lspLine + 1;
            int ksrCol = lspChar + 1;

            var tok = tokens.FirstOrDefault(t =>
                t.Line == ksrLine &&
                t.Col <= ksrCol &&
                ksrCol < t.Col + t.Value.Length + 1);

            if (tok is null || tok.Type == KSR.Lexer.TokenType.Eof) return null;

            var info = tok.Type switch {
                KSR.Lexer.TokenType.Val       => "**val** — immutable binding",
                KSR.Lexer.TokenType.Var       => "**var** — mutable binding",
                KSR.Lexer.TokenType.Fun       => "**fun** — function declaration",
                KSR.Lexer.TokenType.Async     => "**async** — declares an async function; return type is the inner (unwrapped) type",
                KSR.Lexer.TokenType.Await     => "**await** — suspends until the async expression completes",
                KSR.Lexer.TokenType.Struct    => "**struct** — value type with named fields",
                KSR.Lexer.TokenType.Sealed    => "**sealed** — sealed type with exhaustive variants; use `when (x) { is Variant -> … }` to match",
                KSR.Lexer.TokenType.Is        => "**is** — type pattern in a `when` arm: `is Circle(c) -> …`",
                KSR.Lexer.TokenType.Interface => "**interface** — interface declaration",
                KSR.Lexer.TokenType.Implement => "**implement** — interface implementation block",
                KSR.Lexer.TokenType.When      => "**when** — pattern-matching expression",
                KSR.Lexer.TokenType.If        => "**if** — conditional",
                KSR.Lexer.TokenType.Else      => "**else** — else branch",
                KSR.Lexer.TokenType.While     => "**while** — loop",
                KSR.Lexer.TokenType.For       => "**for** — range / collection loop",
                KSR.Lexer.TokenType.Return    => "**return** — return statement",
                KSR.Lexer.TokenType.This      => "**this** — receiver reference",
                KSR.Lexer.TokenType.True      => "`true` — boolean literal",
                KSR.Lexer.TokenType.False     => "`false` — boolean literal",
                KSR.Lexer.TokenType.Null      => "`null` — null literal",
                KSR.Lexer.TokenType.IntLiteral    => $"Int literal: `{tok.Value}`",
                KSR.Lexer.TokenType.FloatLiteral  => $"Double literal: `{tok.Value}`",
                KSR.Lexer.TokenType.StringLiteral => "String literal",
                KSR.Lexer.TokenType.Identifier    => GetIdentifierHover(tok.Value),
                _ => null,
            };

            if (info is null) return null;

            return new { contents = new { kind = "markdown", value = info } };
        } catch { return null; }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string GetIdentifierHover(string name) => name switch {
        // Built-in types
        "Int"         => "**Int** — 32-bit integer (`int` in C#)",
        "Long"        => "**Long** — 64-bit integer (`long` in C#)",
        "Double"      => "**Double** — 64-bit float (`double` in C#)",
        "Float"       => "**Float** — 32-bit float (`float` in C#)",
        "Bool"        => "**Bool** — boolean (`bool` in C#)",
        "String"      => "**String** — string (`string` in C#)",
        "Unit"        => "**Unit** — no return value (`void` in C#)",
        "List"        => "**List\\<T\\>** — immutable list (`IReadOnlyList<T>` in C#). Use `MutableList<T>` for a writable list.",
        "MutableList" => "**MutableList\\<T\\>** — mutable list (`List<T>` in C#)",
        "Map"         => "**Map\\<K,V\\>** — immutable map (`IReadOnlyDictionary<K,V>` in C#). Use `MutableMap<K,V>` for a writable map.",
        "MutableMap"  => "**MutableMap\\<K,V\\>** — mutable map (`Dictionary<K,V>` in C#)",
        // Stdlib symbols
        "IO"   => "**IO** (`ksr.io`) — console I/O: `readLine()`, `readInt()`, `readDouble()`, `print(s)`",
        "File" => "**File** (`ksr.io`) — file operations: `read`, `write`, `append`, `lines`, `exists`, `delete`, `copy`",
        "Path" => "**Path** (`ksr.io`) — path helpers: `join`, `extension`, `fileName`, `fileStem`, `directory`, `absolute`",
        "Text" => "**Text** (`ksr.text`) — string utilities: `toInt`, `toDouble`, `trim`, `toUpper`, `toLower`, `split`, `join`, `replace`, …",
        "Lst"  => "**Lst** (`ksr.collections`) — higher-order list operations: `map`, `filter`, `fold`, `any`, `all`, `sorted`, `take`, `drop`, `groupBy`, …",
        "Mp"   => "**Mp** (`ksr.collections`) — higher-order map operations: `keys`, `values`, `mapValues`, `filter`, `get`, `getOrDefault`, `toMutable`, …",
        _      => $"Identifier: `{name}`",
    };

    /// <summary>Parse source text, returning declarations or empty list on error.</summary>
    private static IEnumerable<KSR.AST.AstNode> SafeParse(string text) {
        try {
            var tokens = new KSR.Lexer.Lexer(text).Tokenize();
            return new KSR.Parser.Parser(tokens).Parse().Declarations;
        } catch { return []; }
    }

    private static void SendResult(string? id, object? result) =>
        WriteMessage(new { jsonrpc = "2.0", id, result });

    private static void SendNotification(string method, object @params) =>
        WriteMessage(new { jsonrpc = "2.0", method, @params });
}
