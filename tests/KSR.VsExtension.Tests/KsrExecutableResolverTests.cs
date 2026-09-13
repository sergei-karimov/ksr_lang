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
        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve("kestrel", _ => false);
        Assert.Equal("kestrel", result);
    }

    [Fact]
    public void Resolve_CanonicalUserProfileCandidateAvailable_ReturnsThatCandidate()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kestrel", "kestrel.exe");

        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve("kestrel", path => path == expected);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_LegacyCandidateIsFallbackAfterCanonicalCandidates()
    {
        var canonicalCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kestrel", "kestrel.exe");
        var legacyCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ksr", "ksr.exe");

        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(
            "kestrel",
            path => path == canonicalCandidate || path == legacyCandidate);

        Assert.Equal(canonicalCandidate, result);
    }

    [Fact]
    public void Resolve_LegacyCandidateIsUsedWhenCanonicalCandidateIsAbsent()
    {
        var legacyCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ksr", "ksr.exe");

        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(
            "kestrel",
            path => path == legacyCandidate);

        Assert.Equal(legacyCandidate, result);
    }

    [Fact]
    public void Resolve_CanonicalProgramFilesCandidatePrecedesLegacyProgramFiles()
    {
        var canonicalCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Kestrel", "kestrel.exe");
        var legacyCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ksr", "ksr.exe");

        var result = KSR.VisualStudio.KsrExecutableResolver.Resolve(
            "kestrel",
            path => path == canonicalCandidate || path == legacyCandidate);

        Assert.Equal(canonicalCandidate, result);
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
