using System.Text;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Validation;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Covers <see cref="ManifestValidator"/>: identifier form, publisher shape, package-type
/// consistency, version ranges, and namespace recognition.
/// </summary>
public class ManifestValidatorTests
{
    private const string Publisher =
        "CN=Contoso Corporation, O=Contoso Corporation, L=Redmond, S=Washington, C=US";

    private const string MinimalVisuals =
        """<uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />""";

    private static string BuildManifest(
        string name = "Contoso.MyApp",
        string publisher = Publisher,
        string architecture = "x64",
        string? resourceId = null,
        string properties = "",
        string dependencies = "",
        string capabilities = "",
        string applications = $"""<Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">{MinimalVisuals}</Application>""",
        string extraNamespaces = "") =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package
          xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
          xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
          xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
          xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"{extraNamespaces}>
          <Identity Name="{name}" Publisher="{publisher}" Version="1.0.0.0" ProcessorArchitecture="{architecture}"{(resourceId is null ? "" : $" ResourceId=\"{resourceId}\"")} />
          <Properties>
            <DisplayName>App</DisplayName>
            <PublisherDisplayName>Contoso</PublisherDisplayName>
        {properties}
          </Properties>
          <Dependencies>
            <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
        {dependencies}
          </Dependencies>
        {capabilities}
          <Applications>
        {applications}
          </Applications>
        </Package>
        """;

    private static ManifestValidationResult Validate(string manifestXml) =>
        ManifestValidator.Validate(new MemoryStream(Encoding.UTF8.GetBytes(manifestXml)));

    private static ManifestValidationIssue SingleIssue(ManifestValidationResult result, ManifestValidationRule rule) =>
        Assert.Single(result.Issues, i => i.Rule == rule);

    // TC-VAL-01
    [Fact]
    public void Validate_WellFormedManifest_ReportsNoIssues()
    {
        ManifestValidationResult result = Validate(BuildManifest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-02
    [Theory]
    [InlineData("Contoso MyApp")]
    [InlineData("Contoso/MyApp")]
    [InlineData("Contoso_MyApp")]
    [InlineData("Contoso\u00e9App")]
    public void Validate_IdentityNameWithIllegalCharacters_IsMalformed(string name)
    {
        ManifestValidationResult result = Validate(BuildManifest(name: name));

        Assert.False(result.IsValid);
        ManifestValidationIssue issue = SingleIssue(result, ManifestValidationRule.IdentifierMalformed);
        Assert.Equal("Identity/@Name", issue.Target);
        Assert.Equal(ManifestValidationSeverity.Error, issue.Severity);
    }

    // TC-VAL-03
    [Theory]
    [InlineData("ab")]
    [InlineData("a")]
    public void Validate_IdentityNameShorterThanThreeCharacters_IsTooShort(string name)
    {
        ManifestValidationResult result = Validate(BuildManifest(name: name));

        Assert.False(result.IsValid);
        Assert.Equal("Identity/@Name", SingleIssue(result, ManifestValidationRule.IdentifierLength).Target);
    }

    // TC-VAL-04
    [Fact]
    public void Validate_IdentityNameLongerThanFiftyCharacters_IsTooLong()
    {
        ManifestValidationResult result = Validate(BuildManifest(name: new string('a', 51)));

        Assert.False(result.IsValid);
        Assert.Equal("Identity/@Name", SingleIssue(result, ManifestValidationRule.IdentifierLength).Target);
    }

    // TC-VAL-05
    [Theory]
    [InlineData("con")]
    [InlineData("CON")]
    [InlineData("lpt9")]
    [InlineData("com1")]
    [InlineData("nul")]
    public void Validate_IdentityNameThatIsAReservedDeviceName_IsReserved(string name)
    {
        ManifestValidationResult result = Validate(BuildManifest(name: name));

        Assert.False(result.IsValid);
        Assert.Equal("Identity/@Name", SingleIssue(result, ManifestValidationRule.IdentifierReserved).Target);
    }

    // TC-VAL-06
    [Theory]
    [InlineData("con.app")]
    [InlineData("PRN.Contoso")]
    [InlineData("xn--contoso")]
    public void Validate_IdentityNameWithAReservedPrefix_IsReserved(string name)
    {
        ManifestValidationResult result = Validate(BuildManifest(name: name));

        Assert.False(result.IsValid);
        Assert.Equal(ManifestValidationRule.IdentifierReserved, Assert.Single(result.Errors).Rule);
    }

    // TC-VAL-07
    [Fact]
    public void Validate_IdentityNameEndingWithAPeriod_IsReserved()
    {
        ManifestValidationResult result = Validate(BuildManifest(name: "Contoso.MyApp."));

        Assert.False(result.IsValid);
        Assert.Contains("period", SingleIssue(result, ManifestValidationRule.IdentifierReserved).Message);
    }

    // TC-VAL-08
    [Fact]
    public void Validate_IdentityNameContainingButNotStartingWithAReservedWord_IsAccepted()
    {
        // 'con' is only reserved as a whole segment or a prefix; 'Contoso' must not be caught.
        ManifestValidationResult result = Validate(BuildManifest(name: "Contoso.Console"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-09
    [Fact]
    public void Validate_ResourceIdWithIllegalCharacters_IsMalformed()
    {
        ManifestValidationResult result = Validate(BuildManifest(resourceId: "en US"));

        Assert.False(result.IsValid);
        Assert.Equal("Identity/@ResourceId", SingleIssue(result, ManifestValidationRule.IdentifierMalformed).Target);
    }

    // TC-VAL-10
    [Fact]
    public void Validate_ResourceIdLongerThanThirtyCharacters_IsTooLong()
    {
        ManifestValidationResult result = Validate(BuildManifest(resourceId: new string('a', 31)));

        Assert.False(result.IsValid);
        Assert.Equal("Identity/@ResourceId", SingleIssue(result, ManifestValidationRule.IdentifierLength).Target);
    }

    // TC-VAL-11
    [Fact]
    public void Validate_ShortResourceId_IsAccepted()
    {
        // A ResourceId may be a single character, unlike a package name.
        ManifestValidationResult result = Validate(BuildManifest(resourceId: "a"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-12
    [Theory]
    [InlineData("Contoso")]
    [InlineData("CN=")]
    [InlineData("=Contoso")]
    public void Validate_PublisherThatIsNotADistinguishedName_IsMalformed(string publisher)
    {
        ManifestValidationResult result = Validate(BuildManifest(publisher: publisher));

        Assert.False(result.IsValid);
        Assert.Equal("Identity/@Publisher", SingleIssue(result, ManifestValidationRule.PublisherMalformed).Target);
    }

    // TC-VAL-13
    [Fact]
    public void Validate_PublisherLongerThanTheSchemaMaximum_IsMalformed()
    {
        string publisher = "CN=" + new string('a', 9000);

        ManifestValidationResult result = Validate(BuildManifest(publisher: publisher));

        Assert.False(result.IsValid);
        ManifestValidationIssue issue = SingleIssue(result, ManifestValidationRule.PublisherMalformed);
        Assert.Contains("8192", issue.Message, StringComparison.Ordinal);
    }

    // TC-VAL-14
    [Fact]
    public void Validate_PackageDependencyWithMinVersionAboveMaxMajorVersionTested_IsInverted()
    {
        string dependency =
            """<PackageDependency Name="Microsoft.VCLibs.140.00" Publisher="CN=Microsoft" MinVersion="14.0.0.0" MaxMajorVersionTested="13" />""";

        ManifestValidationResult result = Validate(BuildManifest(dependencies: dependency));

        Assert.False(result.IsValid);
        ManifestValidationIssue issue = SingleIssue(result, ManifestValidationRule.VersionRangeInverted);
        Assert.Contains("Microsoft.VCLibs.140.00", issue.Target, StringComparison.Ordinal);
    }

    // TC-VAL-15
    [Fact]
    public void Validate_PackageDependencyWithMinVersionEqualToMaxMajorVersionTested_IsAccepted()
    {
        // MaxMajorVersionTested is a major-version bound, so equality is the common, valid case.
        string dependency =
            """<PackageDependency Name="Microsoft.VCLibs.140.00" Publisher="CN=Microsoft" MinVersion="14.0.30704.0" MaxMajorVersionTested="14" />""";

        ManifestValidationResult result = Validate(BuildManifest(dependencies: dependency));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-16
    [Fact]
    public void Validate_PackageDependencyWithAnIllegalName_IsMalformed()
    {
        string dependency =
            """<PackageDependency Name="Bad Name" Publisher="CN=Microsoft" MinVersion="1.0.0.0" />""";

        ManifestValidationResult result = Validate(BuildManifest(dependencies: dependency));

        Assert.False(result.IsValid);
        ManifestValidationIssue issue = SingleIssue(result, ManifestValidationRule.IdentifierMalformed);
        Assert.Equal("Dependencies/PackageDependency[Bad Name]/@Name", issue.Target);
    }

    // TC-VAL-17
    [Fact]
    public void Validate_TargetDeviceFamilyWithMinVersionAboveMaxVersionTested_IsInverted()
    {
        string manifest = BuildManifest().Replace(
            @"MinVersion=""10.0.17763.0"" MaxVersionTested=""10.0.22621.0""",
            @"MinVersion=""10.0.22621.0"" MaxVersionTested=""10.0.17763.0""",
            StringComparison.Ordinal);

        ManifestValidationResult result = Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Equal(
            "Dependencies/TargetDeviceFamily[Windows.Desktop]",
            SingleIssue(result, ManifestValidationRule.VersionRangeInverted).Target);
    }

    // TC-VAL-18
    [Fact]
    public void Validate_PackageThatIsBothFrameworkAndResource_ConflictsOnType()
    {
        string properties = "    <Framework>true</Framework>\n    <ResourcePackage>true</ResourcePackage>";

        ManifestValidationResult result = Validate(BuildManifest(properties: properties, applications: ""));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            i => i.Rule == ManifestValidationRule.ConflictingPackageType && i.Target == "Properties");
    }

    // TC-VAL-19
    [Fact]
    public void Validate_FrameworkPackageWithApplications_IsRejected()
    {
        ManifestValidationResult result = Validate(BuildManifest(properties: "    <Framework>true</Framework>"));

        Assert.False(result.IsValid);
        Assert.Equal("Applications", SingleIssue(result, ManifestValidationRule.FrameworkContent).Target);
    }

    // TC-VAL-20
    [Fact]
    public void Validate_FrameworkPackageWithCapabilities_IsRejected()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            properties: "    <Framework>true</Framework>",
            capabilities: """  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>""",
            applications: ""));

        Assert.False(result.IsValid);
        Assert.Equal("Capabilities", SingleIssue(result, ManifestValidationRule.FrameworkContent).Target);
    }

    // TC-VAL-21
    [Fact]
    public void Validate_FrameworkPackageWithoutApplicationsOrCapabilities_IsAccepted()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            properties: "    <Framework>true</Framework>",
            applications: ""));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-22
    [Fact]
    public void Validate_ResourcePackageWithAnArchitecture_IsRejected()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            properties: "    <ResourcePackage>true</ResourcePackage>",
            resourceId: "en-US",
            applications: ""));

        Assert.False(result.IsValid);
        Assert.Equal(
            "Identity/@ProcessorArchitecture",
            Assert.Single(result.Errors, i => i.Target == "Identity/@ProcessorArchitecture").Target);
    }

    // TC-VAL-23
    [Fact]
    public void Validate_NeutralResourcePackage_IsAccepted()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            properties: "    <ResourcePackage>true</ResourcePackage>",
            architecture: "neutral",
            resourceId: "en-US",
            applications: ""));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-24
    [Fact]
    public void Validate_ResourcePackageWithADependency_IsRejected()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            properties: "    <ResourcePackage>true</ResourcePackage>",
            architecture: "neutral",
            dependencies: """<PackageDependency Name="Microsoft.VCLibs.140.00" Publisher="CN=Microsoft" MinVersion="14.0.0.0" />""",
            applications: ""));

        Assert.False(result.IsValid);
        Assert.Equal(
            "Dependencies/PackageDependency",
            SingleIssue(result, ManifestValidationRule.ResourcePackageContent).Target);
    }

    // TC-VAL-25
    [Fact]
    public void Validate_OptionalPackageWithCapabilities_IsRejected()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            extraNamespaces: "\n  xmlns:uap4=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/4\"",
            dependencies: """<uap4:MainPackageDependency Name="Contoso.MainApp" />""",
            capabilities: """  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>"""));

        Assert.False(result.IsValid);
        Assert.Equal("Capabilities", SingleIssue(result, ManifestValidationRule.OptionalPackageContent).Target);
    }

    // TC-VAL-26
    [Fact]
    public void Validate_OptionalPackageThatIsAlsoAFramework_ConflictsOnType()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            extraNamespaces: "\n  xmlns:uap4=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/4\"",
            properties: "    <Framework>true</Framework>",
            dependencies: """<uap4:MainPackageDependency Name="Contoso.MainApp" />""",
            applications: ""));

        Assert.False(result.IsValid);
        Assert.Equal(
            "Dependencies/MainPackageDependency",
            SingleIssue(result, ManifestValidationRule.ConflictingPackageType).Target);
    }

    // TC-VAL-27
    [Fact]
    public void Validate_OptionalPackageWithoutCapabilities_IsAccepted()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            extraNamespaces: "\n  xmlns:uap4=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/4\"",
            dependencies: """<uap4:MainPackageDependency Name="Contoso.MainApp" />"""));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-28
    [Theory]
    [InlineData("1App")]
    [InlineData("App-One")]
    [InlineData("App..Two")]
    [InlineData(".App")]
    public void Validate_ApplicationIdWithAnIllegalForm_IsMalformed(string id)
    {
        ManifestValidationResult result = Validate(BuildManifest(
            applications: $"""<Application Id="{id}" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">{MinimalVisuals}</Application>"""));

        Assert.False(result.IsValid);
        Assert.Equal(ManifestValidationRule.ApplicationIdMalformed, Assert.Single(result.Errors).Rule);
    }

    // TC-VAL-29
    [Fact]
    public void Validate_ApplicationIdLongerThanSixtyFourCharacters_IsMalformed()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            applications: $"""<Application Id="{new string('A', 65)}" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">{MinimalVisuals}</Application>"""));

        Assert.False(result.IsValid);
        Assert.Contains("65 characters", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    // TC-VAL-30
    [Fact]
    public void Validate_TwoApplicationsSharingAnId_IsADuplicate()
    {
        string application =
            $"""<Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">{MinimalVisuals}</Application>""";

        ManifestValidationResult result = Validate(BuildManifest(applications: application + application));

        Assert.False(result.IsValid);
        Assert.Equal(ManifestValidationRule.DuplicateApplicationId, Assert.Single(result.Errors).Rule);
    }

    // TC-VAL-31
    [Fact]
    public void Validate_DottedApplicationId_IsAccepted()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            applications: $"""<Application Id="Contoso.App2" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">{MinimalVisuals}</Application>"""));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-32
    [Fact]
    public void Validate_SameCapabilityDeclaredTwice_IsADuplicate()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            capabilities: """  <Capabilities><Capability Name="internetClient" /><Capability Name="internetClient" /></Capabilities>"""));

        Assert.False(result.IsValid);
        Assert.Equal(ManifestValidationRule.DuplicateCapability, Assert.Single(result.Errors).Rule);
    }

    // TC-VAL-33
    [Fact]
    public void Validate_SameCapabilityNameInTwoNamespaces_IsNotADuplicate()
    {
        // The schema's uniqueness constraints are per element type, so a foundation Capability and a
        // restricted one that happen to share a name are two different declarations.
        ManifestValidationResult result = Validate(BuildManifest(
            capabilities: """  <Capabilities><Capability Name="documentsLibrary" /><rescap:Capability Name="documentsLibrary" /></Capabilities>"""));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    // TC-VAL-34
    [Fact]
    public void Validate_UnknownNamespace_IsAWarningNotAnError()
    {
        // A package built against a newer SDK than this library knows about is still valid.
        ManifestValidationResult result = Validate(BuildManifest(
            extraNamespaces: "\n  xmlns:future=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/99\"",
            capabilities: """  <Capabilities><future:Capability Name="somethingNew" /></Capabilities>"""));

        Assert.True(result.IsValid);
        ManifestValidationIssue issue = Assert.Single(result.Warnings);
        Assert.Equal(ManifestValidationRule.UnknownNamespace, issue.Rule);
        Assert.Equal("http://schemas.microsoft.com/appx/manifest/uap/windows10/99", issue.Target);
    }

    // TC-VAL-35
    [Fact]
    public void Validate_UnusedNamespaceDeclaration_IsNotReported()
    {
        // Manifests routinely declare a bank of prefixes and use only some; declaring is not using.
        ManifestValidationResult result = Validate(BuildManifest(
            extraNamespaces: "\n  xmlns:future=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/99\""));

        Assert.Empty(result.Issues);
    }

    // TC-VAL-36
    [Fact]
    public void Validate_SameUnknownNamespaceUsedTwice_IsReportedOnce()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            extraNamespaces: "\n  xmlns:future=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/99\"",
            capabilities: """  <Capabilities><future:Capability Name="one" /><future:Capability Name="two" /></Capabilities>"""));

        Assert.Single(result.Warnings);
    }

    // TC-VAL-37
    [Fact]
    public void Validate_WithoutTheDocument_SkipsTheNamespaceCheck()
    {
        // The AppxManifest-only overload cannot see namespaces, so it must not invent findings.
        AppxManifest manifest = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(BuildManifest(
            extraNamespaces: "\n  xmlns:future=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/99\"",
            capabilities: """  <Capabilities><future:Capability Name="somethingNew" /></Capabilities>"""))));

        ManifestValidationResult result = ManifestValidator.Validate(manifest);

        Assert.Empty(result.Issues);
    }

    // TC-VAL-38
    [Fact]
    public void Validate_ManifestWithSeveralProblems_ReportsThemAll()
    {
        ManifestValidationResult result = Validate(BuildManifest(
            name: "con",
            publisher: "Contoso",
            applications: $"""<Application Id="1App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">{MinimalVisuals}</Application>"""));

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                ManifestValidationRule.IdentifierReserved,
                ManifestValidationRule.PublisherMalformed,
                ManifestValidationRule.ApplicationIdMalformed,
            ],
            result.Errors.Select(i => i.Rule));
    }

    // TC-VAL-39
    [Fact]
    public void Validate_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ManifestValidator.Validate((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => ManifestValidator.Validate((AppxManifest)null!));
    }

    // TC-VAL-40
    [Fact]
    public void ManifestNamespaces_KnowsEveryNamespaceTheCorpusManifestsUse()
    {
        Assert.True(ManifestNamespaces.IsKnownPackageNamespace(
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10"));
        Assert.True(ManifestNamespaces.IsKnownPackageNamespace(
            "http://schemas.microsoft.com/appx/manifest/uap/windows10/13"));
        Assert.False(ManifestNamespaces.IsKnownPackageNamespace(
            "http://schemas.microsoft.com/appx/manifest/uap/windows10/9"));
        Assert.True(ManifestNamespaces.IsKnownBundleNamespace(
            "http://schemas.microsoft.com/appx/2013/bundle"));
        Assert.False(ManifestNamespaces.IsKnownBundleNamespace(
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10"));
    }
}
