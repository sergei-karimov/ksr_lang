using System;
using System.IO;
using Xunit;

namespace KSR.VsExtension.Tests;

public sealed class KsrExecutableResolverTests
{
    [Fact]
    public void Resolve_AbsolutePathThatExists_ReturnsThatPath()
    {
        var path = @"C:\tools\ksr.exe";
        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(path, _ => true);
        Assert.Equal(path, result);
    }

    [Fact]
    public void Resolve_AbsolutePathThatDoesNotExist_ReturnsNull()
    {
        var path = @"C:\does\not\exist\ksr.exe";
        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(path, _ => false);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_RelativeNameNoCandidates_FallsBackToConfigured()
    {
        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve("ksr", _ => false);
        Assert.Equal("ksr", result);
    }

    [Fact]
    public void Resolve_RelativeNameWithUserProfileCandidateAvailable_ReturnsThatCandidate()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ksr", "ksr.exe");

        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve("ksr", path => path == expected);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_UserProfileCandidateTakesPrecedenceOverProgramFiles()
    {
        var userProfileCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ksr", "ksr.exe");
        var programFilesCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ksr", "ksr.exe");

        // Both candidates "exist" — resolver must return the first (user profile) one.
        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(
            "ksr",
            path => path == userProfileCandidate || path == programFilesCandidate);

        Assert.Equal(userProfileCandidate, result);
    }

    [Fact]
    public void Resolve_RelativePathWithSeparator_FallsBackToConfigured()
    {
        // A relative path like "tools\ksr.exe" is not rooted — resolver falls back to configured.
        var configured = @"tools\ksr.exe";
        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(configured, _ => false);
        Assert.Equal(configured, result);
    }
}
