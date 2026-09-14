using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace KSR.Cli.Tests;

public sealed class LspTransportTests
{
    [Fact]
    public async Task DidOpenAndDidChangePublishDiagnosticsThroughJsonRpcWithoutRollForwardOverride()
    {
        using var process = StartServer();
        var input = process.StandardInput.BaseStream;
        var output = process.StandardOutput.BaseStream;

        await SendAsync(input, new
        {
            jsonrpc = "2.0", id = 1, method = "initialize", @params = new { }
        });
        using var initialized = await ReadAsync(output);
        Assert.Equal("Kestrel Language Server", initialized.RootElement
            .GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        await SendAsync(input, new { jsonrpc = "2.0", method = "initialized", @params = new { } });
        await SendAsync(input, new
        {
            jsonrpc = "2.0", method = "textDocument/didOpen",
            @params = new
            {
                textDocument = new { uri = "file:///transport.ksr", languageId = "ksr", version = 1,
                    text = "fun main() { unknownName() }" }
            }
        });

        using var opened = await ReadAsync(output);
        var openedDiagnostics = opened.RootElement.GetProperty("params").GetProperty("diagnostics");
        Assert.NotEmpty(openedDiagnostics.EnumerateArray());
        Assert.Equal("kestrel", openedDiagnostics[0].GetProperty("source").GetString());
        Assert.Contains("unknown", openedDiagnostics[0].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        await SendAsync(input, new
        {
            jsonrpc = "2.0", method = "textDocument/didChange",
            @params = new
            {
                textDocument = new { uri = "file:///transport.ksr", version = 2 },
                contentChanges = new[] { new { text = "fun main() { }" } }
            }
        });

        using var changed = await ReadAsync(output);
        Assert.Empty(changed.RootElement.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        await SendAsync(input, new { jsonrpc = "2.0", id = 2, method = "shutdown", @params = new { } });
        using var shutdown = await ReadAsync(output);
        Assert.Equal("2", shutdown.RootElement.GetProperty("id").GetString());
        await SendAsync(input, new { jsonrpc = "2.0", method = "exit", @params = new { } });
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Process StartServer()
    {
        var root = RepositoryRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment.Remove("DOTNET_ROLL_FORWARD");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(root, "KSR.csproj"));
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("lsp");
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Kestrel LSP.");
    }

    private static async Task SendAsync(Stream input, object message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await input.WriteAsync(header);
        await input.WriteAsync(body);
        await input.FlushAsync();
    }

    private static async Task<JsonDocument> ReadAsync(Stream output)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var contentLength = 0;
        while (true)
        {
            var line = await ReadLineAsync(output, timeout.Token);
            if (line.Length == 0) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line[15..].Trim());
        }

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < body.Length)
            offset += await output.ReadAsync(body.AsMemory(offset, body.Length - offset), timeout.Token);
        return JsonDocument.Parse(body);
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var count = await stream.ReadAsync(one, token);
            if (count == 0) throw new EndOfStreamException();
            if (one[0] == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(one[0]);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KSR.csproj"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate KSR.csproj.");
    }
}
