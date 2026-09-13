using System;
using Xunit;

namespace KSR.VsExtension.Tests;

public sealed class KsrLanguageClientTests
{
    [Fact]
    public void CreateStartInfo_DirectExecutableUsesExecutableAndArguments()
    {
        var startInfo = KSR.VisualStudio.KsrLanguageClient.CreateStartInfo(
            @"C:\tools\kestrel.exe", isWindows: true,
            commandProcessor: @"C:\Windows\System32\cmd.exe",
            powerShell: "powershell.exe");

        Assert.Equal(@"C:\tools\kestrel.exe", startInfo.FileName);
        Assert.Equal("lsp", startInfo.Arguments);
    }

    [Fact]
    public void CreateStartInfo_CmdAliasUsesCommandProcessor()
    {
        var startInfo = KSR.VisualStudio.KsrLanguageClient.CreateStartInfo(
            @"C:\Users\me\.dotnet\tools\ksr.cmd", isWindows: true,
            commandProcessor: @"C:\Windows\System32\cmd.exe",
            powerShell: "powershell.exe");

        Assert.Equal(@"C:\Windows\System32\cmd.exe", startInfo.FileName);
        Assert.Contains("ksr.cmd", startInfo.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lsp", startInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStartInfo_PowerShellAliasUsesPowerShell()
    {
        var startInfo = KSR.VisualStudio.KsrLanguageClient.CreateStartInfo(
            @"C:\Users\me\.dotnet\tools\ksr.ps1", isWindows: true,
            commandProcessor: @"C:\Windows\System32\cmd.exe",
            powerShell: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", startInfo.FileName);
        Assert.Contains("-File", startInfo.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ksr.ps1", startInfo.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lsp", startInfo.Arguments, StringComparison.Ordinal);
    }
}
