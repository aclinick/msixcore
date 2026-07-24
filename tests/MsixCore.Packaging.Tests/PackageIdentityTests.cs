using MsixCore.Packaging;

namespace MsixCore.Packaging.Tests;

public class PackageIdentityTests
{
    private static PackageIdentity MicrosoftLikeIdentity(ProcessorArchitecture arch, string resourceId = "") => new()
    {
        Name = "Microsoft.MyApp",
        Publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
        Version = new Version(1, 0, 0, 0),
        Architecture = arch,
        ResourceId = resourceId,
    };

    [Fact]
    public void PackageFamilyName_CombinesNameAndPublisherHash()
    {
        var identity = MicrosoftLikeIdentity(ProcessorArchitecture.X64);
        Assert.Equal("Microsoft.MyApp_8wekyb3d8bbwe", identity.PackageFamilyName);
    }

    [Fact]
    public void PackageFullName_MainPackage_HasEmptyResourceIdSegment()
    {
        var identity = MicrosoftLikeIdentity(ProcessorArchitecture.X64);
        Assert.Equal("Microsoft.MyApp_1.0.0.0_x64__8wekyb3d8bbwe", identity.PackageFullName);
    }

    [Fact]
    public void PackageFullName_ResourcePackage_IncludesResourceId()
    {
        var identity = MicrosoftLikeIdentity(ProcessorArchitecture.Neutral, resourceId: "en-us");
        Assert.Equal("Microsoft.MyApp_1.0.0.0_neutral_en-us_8wekyb3d8bbwe", identity.PackageFullName);
    }

    [Theory]
    [InlineData(ProcessorArchitecture.X86, "x86")]
    [InlineData(ProcessorArchitecture.X64, "x64")]
    [InlineData(ProcessorArchitecture.Arm, "arm")]
    [InlineData(ProcessorArchitecture.Arm64, "arm64")]
    [InlineData(ProcessorArchitecture.Neutral, "neutral")]
    [InlineData(ProcessorArchitecture.X86OnArm64, "x86a64")]
    public void ArchitectureMoniker_MapsAllArchitectures(ProcessorArchitecture arch, string expected)
    {
        Assert.Equal(expected, PackageIdentity.ArchitectureMoniker(arch));
    }
}
