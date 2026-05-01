using Xunit;

namespace KSR.VsExtension.Tests;

public sealed class KsrPathSettingsTests
{
    [Fact]
    public void NormalizePath_Null_ReturnsDefault()
    {
        Assert.Equal("ksr", KSR.VisualStudio.KsrPathSettings.NormalizePath(null));
    }

    [Fact]
    public void NormalizePath_Empty_ReturnsDefault()
    {
        Assert.Equal("ksr", KSR.VisualStudio.KsrPathSettings.NormalizePath(""));
    }

    [Fact]
    public void NormalizePath_Whitespace_ReturnsDefault()
    {
        Assert.Equal("ksr", KSR.VisualStudio.KsrPathSettings.NormalizePath("   "));
    }

    [Fact]
    public void NormalizePath_ValidAbsolutePath_ReturnsValueUnchanged()
    {
        const string path = @"C:\tools\ksr.exe";
        Assert.Equal(path, KSR.VisualStudio.KsrPathSettings.NormalizePath(path));
    }

    [Fact]
    public void NormalizePath_ValidRelativeName_ReturnsValueUnchanged()
    {
        Assert.Equal("ksr", KSR.VisualStudio.KsrPathSettings.NormalizePath("ksr"));
    }
}
