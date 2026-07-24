using MsixCore.Packaging;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Tests;

public class ScaffoldTests
{
    [Fact]
    public void ProcessorArchitecture_HasExpectedMembers()
    {
        Assert.Equal(0, (int)ProcessorArchitecture.Neutral);
        Assert.Equal(1, (int)ProcessorArchitecture.X86);
        Assert.Equal(2, (int)ProcessorArchitecture.X64);
        Assert.Equal(3, (int)ProcessorArchitecture.Arm);
        Assert.Equal(4, (int)ProcessorArchitecture.Arm64);
    }

    [Fact]
    public void OpcPartNames_AreStable()
    {
        Assert.Equal("AppxManifest.xml", OpcPartNames.AppxManifest);
        Assert.Equal("AppxBlockMap.xml", OpcPartNames.AppxBlockMap);
        Assert.Equal("AppxSignature.p7x", OpcPartNames.AppxSignature);
        Assert.Equal("[Content_Types].xml", OpcPartNames.ContentTypes);
        Assert.Equal("AppxMetadata/AppxBundleManifest.xml", OpcPartNames.AppxBundleManifest);
    }

    [Fact]
    public void PackageIdentity_CanBeConstructed_WithRequiredMembers()
    {
        var identity = new PackageIdentity
        {
            Name = "Contoso.MyApp",
            Publisher = "CN=Contoso",
            Version = new Version(1, 2, 3, 4),
        };

        Assert.Equal("Contoso.MyApp", identity.Name);
        Assert.Equal(ProcessorArchitecture.Neutral, identity.Architecture);
        Assert.Equal(string.Empty, identity.ResourceId);
    }

    [Fact]
    public void MsixPackage_Open_NotYetImplemented_InPhase0()
    {
        // Contract guard: flips to real behavior in Phase 1.
        Assert.Throws<NotImplementedException>(() => MsixPackage.Open("nonexistent.msix"));
    }
}
