using System.Text;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Covers the package-to-package dependencies declared under <c>Dependencies</c>: framework
/// dependencies, the modification-package relationship, and host runtimes.
/// </summary>
public class ManifestDependencyTests
{
    private const string Publisher =
        "CN=Contoso Corporation, O=Contoso Corporation, L=Redmond, S=Washington, C=US";

    private static AppxManifest Parse(string dependenciesBody)
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package
              xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
              xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
              xmlns:uap4="http://schemas.microsoft.com/appx/manifest/uap/windows10/4"
              xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
              xmlns:uap6="http://schemas.microsoft.com/appx/manifest/uap/windows10/6"
              xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
            {dependenciesBody}
              </Dependencies>
            </Package>
            """;

        return AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));
    }

    // TC-P1-3a
    [Fact]
    public void Parse_PackageDependency_ReadsNameMinVersionAndPublisher()
    {
        AppxManifest manifest = Parse(
            """
                <PackageDependency Name="Microsoft.VCLibs.140.00"
                                   MinVersion="14.0.30704.0"
                                   Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />
            """);

        PackageDependency dependency = Assert.Single(manifest.PackageDependencies);
        Assert.Equal(PackageDependencyKind.Framework, dependency.Kind);
        Assert.Equal("Microsoft.VCLibs.140.00", dependency.Name);
        Assert.Equal(new Version(14, 0, 30704, 0), dependency.MinVersion);
        Assert.Equal(
            "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            dependency.Publisher);
        Assert.Null(dependency.MaxMajorVersionTested);
    }

    [Fact]
    public void Parse_PackageDependency_ReadsMaxMajorVersionTestedAsASingleNumber()
    {
        // MaxMajorVersionTested is an xs:unsignedShort, not a version quad; parsing it as a quad
        // would reject every real manifest that declares it.
        AppxManifest manifest = Parse(
            $"""
                <PackageDependency Name="Microsoft.VCLibs.140.00" MinVersion="14.0.0.0" Publisher="{Publisher}"
                                   MaxMajorVersionTested="15" />
            """);

        PackageDependency dependency = Assert.Single(manifest.PackageDependencies);
        Assert.Equal((ushort)15, dependency.MaxMajorVersionTested);
    }

    [Theory]
    [InlineData("14.0.0.0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Parse_PackageDependency_RejectsAnInvalidMaxMajorVersionTested(string value)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Parse(
            $"""
                <PackageDependency Name="Framework" MinVersion="1.0.0.0" Publisher="{Publisher}"
                                   MaxMajorVersionTested="{value}" />
            """));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    [Theory]
    [InlineData("""<PackageDependency MinVersion="1.0.0.0" Publisher="CN=X" />""")]
    [InlineData("""<PackageDependency Name="Framework" Publisher="CN=X" />""")]
    [InlineData("""<PackageDependency Name="Framework" MinVersion="1.0.0.0" />""")]
    public void Parse_PackageDependency_RequiresNamePublisherAndMinVersion(string element)
    {
        // All three are use="required" in the foundation schema.
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Parse($"    {element}"));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void Parse_PackageDependency_ReadsTheOptionalFlag(string value, bool expected)
    {
        AppxManifest manifest = Parse(
            $"""
                <PackageDependency Name="Framework" MinVersion="1.0.0.0" Publisher="{Publisher}"
                                   uap6:Optional="{value}" />
            """);

        Assert.Equal(expected, Assert.Single(manifest.PackageDependencies).IsOptional);
    }

    [Fact]
    public void Parse_PackageDependency_DefaultsToRequired()
    {
        AppxManifest manifest = Parse(
            $"""<PackageDependency Name="Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" />""");

        Assert.False(Assert.Single(manifest.PackageDependencies).IsOptional);
    }

    [Fact]
    public void Parse_PackageDependency_RejectsAnInvalidOptionalFlag()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Parse(
            $"""<PackageDependency Name="Framework" MinVersion="1.0.0.0" Publisher="{Publisher}" uap6:Optional="yes" />"""));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    // TC-P1-3b
    [Fact]
    public void Parse_MainPackageDependency_CapturesTheModificationRelationship()
    {
        AppxManifest manifest = Parse(
            $"""
                <uap4:MainPackageDependency Name="Contoso.MainApp" Publisher="{Publisher}" />
            """);

        PackageDependency dependency = Assert.Single(manifest.PackageDependencies);
        Assert.Equal(PackageDependencyKind.MainPackage, dependency.Kind);
        Assert.Equal("Contoso.MainApp", dependency.Name);
        Assert.Equal(Publisher, dependency.Publisher);

        // uap4:MainPackageDependency has no version attribute at all: a modification package binds
        // to its parent by name and publisher only.
        Assert.Null(dependency.MinVersion);
        Assert.Null(dependency.MaxMajorVersionTested);
    }

    [Fact]
    public void Parse_MainPackageDependency_AllowsAnOmittedPublisher()
    {
        // Publisher is optional on uap4 and absent entirely from the uap3 form, because a
        // modification package always shares its parent's publisher.
        AppxManifest manifest = Parse("""    <uap3:MainPackageDependency Name="Contoso.MainApp" />""");

        PackageDependency dependency = Assert.Single(manifest.PackageDependencies);
        Assert.Equal(PackageDependencyKind.MainPackage, dependency.Kind);
        Assert.Null(dependency.Publisher);
    }

    // TC-P1-3c
    [Fact]
    public void Parse_HostRuntimeDependency_IsRead()
    {
        AppxManifest manifest = Parse(
            $"""
                <uap10:HostRuntimeDependency Name="Contoso.PythonHost" Publisher="{Publisher}" MinVersion="3.11.0.0" />
            """);

        PackageDependency dependency = Assert.Single(manifest.PackageDependencies);
        Assert.Equal(PackageDependencyKind.HostRuntime, dependency.Kind);
        Assert.Equal("Contoso.PythonHost", dependency.Name);
        Assert.Equal(Publisher, dependency.Publisher);
        Assert.Equal(new Version(3, 11, 0, 0), dependency.MinVersion);
    }

    [Fact]
    public void Parse_HostRuntimeDependency_RequiresAllThreeAttributes()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Parse(
            """    <uap10:HostRuntimeDependency Name="Contoso.PythonHost" />"""));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    [Fact]
    public void Parse_KeepsEveryDependencyInManifestOrder()
    {
        AppxManifest manifest = Parse(
            $"""
                <PackageDependency Name="Framework.One" MinVersion="1.0.0.0" Publisher="{Publisher}" />
                <PackageDependency Name="Framework.Two" MinVersion="2.0.0.0" Publisher="{Publisher}" />
                <uap4:MainPackageDependency Name="Contoso.MainApp" Publisher="{Publisher}" />
            """);

        Assert.Equal(
            ["Framework.One", "Framework.Two", "Contoso.MainApp"],
            manifest.PackageDependencies.Select(static dependency => dependency.Name));
    }

    [Fact]
    public void Parse_IgnoresDependencyChildrenThatAreNotPackageRelationships()
    {
        // TargetDeviceFamily has its own model, and DriverDependency/OSPackageDependency are not
        // package-to-package relationships. Unknown children must not break forward compatibility.
        AppxManifest manifest = Parse(
            """
                <uap5:DriverDependency>
                  <uap5:DriverConstraint Name="Contoso.Driver" MinDate="2018-08-01" />
                </uap5:DriverDependency>
                <SomeFutureDependency Name="Whatever" />
            """);

        Assert.Empty(manifest.PackageDependencies);
        Assert.Single(manifest.TargetDeviceFamilies);
    }

    [Fact]
    public void Parse_WithoutADependenciesElement_YieldsNoDependencies()
    {
        const string manifestXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.0.0.0" />
            </Package>
            """;

        AppxManifest manifest = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifestXml)));

        Assert.Empty(manifest.PackageDependencies);
    }
}
