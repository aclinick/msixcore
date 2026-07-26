using System.Text;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Covers the richer <c>uap:VisualElements</c> shape (tiles, splash screen, lock screen, rotation)
/// and the categorization of <c>Capabilities</c> declarations by declaring namespace.
/// </summary>
public class VisualElementsAndCapabilityTests
{
    private const string Publisher =
        "CN=Contoso Corporation, O=Contoso Corporation, L=Redmond, S=Washington, C=US";

    private static AppxManifest ParseManifest(string visualElements, string capabilities)
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package
              xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
              xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
              xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
              xmlns:uap4="http://schemas.microsoft.com/appx/manifest/uap/windows10/4"
              xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
              xmlns:uap7="http://schemas.microsoft.com/appx/manifest/uap/windows10/7"
              xmlns:mobile="http://schemas.microsoft.com/appx/manifest/mobile/windows10"
              xmlns:wincap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/windowscapabilities"
              xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
              </Dependencies>
            {capabilities}
              <Applications>
                <Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">
            {visualElements}
                </Application>
              </Applications>
            </Package>
            """;

        return AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));
    }

    private static VisualElements ParseVisuals(string visualElements) =>
        Assert.Single(ParseManifest(visualElements, capabilities: "").Applications).VisualElements;

    private static IReadOnlyList<ManifestCapability> ParseCapabilities(string body) =>
        ParseManifest(
            visualElements: """<uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />""",
            capabilities: $"<Capabilities>{body}</Capabilities>").DeclaredCapabilities;

    // TC-P1-6a
    [Fact]
    public void Parse_VisualElements_ReadsTheDefaultTileLogos()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="#0078D7"
                                Square150x150Logo="Assets\Square150.png" Square44x44Logo="Assets\Square44.png">
              <uap:DefaultTile Wide310x150Logo="Assets\Wide310.png" Square310x310Logo="Assets\Square310.png"
                               Square71x71Logo="Assets\Square71.png" ShortName="Contoso">
                <uap:ShowNameOnTiles>
                  <uap:ShowOn Tile="square150x150Logo" />
                  <uap:ShowOn Tile="wide310x150Logo" />
                </uap:ShowNameOnTiles>
              </uap:DefaultTile>
            </uap:VisualElements>
            """);

        Assert.Equal("Contoso", visuals.DisplayName);
        Assert.Equal("An app", visuals.Description);
        Assert.Equal("#0078D7", visuals.BackgroundColor);
        Assert.Equal(@"Assets\Square150.png", visuals.Square150x150Logo);
        Assert.Equal(@"Assets\Square44.png", visuals.Square44x44Logo);

        DefaultTile tile = Assert.IsType<DefaultTile>(visuals.DefaultTile);
        Assert.Equal(@"Assets\Wide310.png", tile.Wide310x150Logo);
        Assert.Equal(@"Assets\Square310.png", tile.Square310x310Logo);
        Assert.Equal(@"Assets\Square71.png", tile.Square71x71Logo);
        Assert.Equal("Contoso", tile.ShortName);
        Assert.Equal(["square150x150Logo", "wide310x150Logo"], tile.ShowNameOnTiles);
    }

    // TC-P1-6a
    [Fact]
    public void Parse_VisualElements_ReadsTheSplashScreenAndLockScreen()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:SplashScreen Image="Assets\Splash.png" BackgroundColor="#FFFFFF" uap5:Optional="true" />
              <uap:LockScreen BadgeLogo="Assets\Badge.png" Notification="badgeAndTileText" />
            </uap:VisualElements>
            """);

        SplashScreen splash = Assert.IsType<SplashScreen>(visuals.SplashScreen);
        Assert.Equal(@"Assets\Splash.png", splash.Image);
        Assert.Equal("#FFFFFF", splash.BackgroundColor);
        Assert.True(splash.IsOptional);

        LockScreen lockScreen = Assert.IsType<LockScreen>(visuals.LockScreen);
        Assert.Equal(@"Assets\Badge.png", lockScreen.BadgeLogo);
        Assert.Equal("badgeAndTileText", lockScreen.Notification);
    }

    // TC-P1-6a
    [Fact]
    public void Parse_VisualElements_ReadsTheRotationPreferencesInOrder()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:InitialRotationPreference>
                <uap:Rotation Preference="landscape" />
                <uap:Rotation Preference="landscapeFlipped" />
              </uap:InitialRotationPreference>
            </uap:VisualElements>
            """);

        Assert.Equal(["landscape", "landscapeFlipped"], visuals.InitialRotationPreferences);
    }

    // TC-P1-6a
    [Fact]
    public void Parse_VisualElements_ReadsTheUap3VisualGroup()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap3:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                 Square150x150Logo="a.png" Square44x44Logo="b.png" VisualGroup="Contoso Tools" />
            """);

        Assert.Equal("Contoso Tools", visuals.VisualGroup);
    }

    /// <summary>
    /// The optional children are genuinely optional: a minimal declaration must not fabricate them,
    /// so that "not declared" stays distinguishable from "declared empty".
    /// </summary>
    [Fact]
    public void Parse_VisualElements_WithoutOptionalChildren_LeavesThemUnset()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png" />
            """);

        Assert.Null(visuals.DefaultTile);
        Assert.Null(visuals.SplashScreen);
        Assert.Null(visuals.LockScreen);
        Assert.Null(visuals.VisualGroup);
        Assert.Empty(visuals.InitialRotationPreferences);
        Assert.True(visuals.AppListEntry);
    }

    /// <summary>
    /// <c>uap5:Optional</c> is unstated far more often than it is stated false, so the flag stays
    /// nullable rather than defaulting.
    /// </summary>
    [Fact]
    public void Parse_SplashScreen_WithoutTheOptionalFlag_LeavesItNull()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:SplashScreen Image="Assets\Splash.png" />
            </uap:VisualElements>
            """);

        SplashScreen splash = Assert.IsType<SplashScreen>(visuals.SplashScreen);
        Assert.Null(splash.IsOptional);
        Assert.Null(splash.BackgroundColor);
    }

    [Fact]
    public void Parse_SplashScreen_WithoutAnImage_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:SplashScreen BackgroundColor="#FFFFFF" />
            </uap:VisualElements>
            """));

        Assert.Contains("Image", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_LockScreen_WithoutANotification_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:LockScreen BadgeLogo="Assets\Badge.png" />
            </uap:VisualElements>
            """));

        Assert.Contains("Notification", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The schema requires 1..4 <c>ShowOn</c> children, so an empty container is malformed — and
    /// accepting it would make the empty list mean both "absent" and "declared empty".
    /// </summary>
    [Fact]
    public void Parse_AnEmptyShowNameOnTiles_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:DefaultTile ShortName="Contoso">
                <uap:ShowNameOnTiles />
              </uap:DefaultTile>
            </uap:VisualElements>
            """));

        Assert.Contains("ShowOn", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The schema likewise requires 1..4 <c>Rotation</c> children.</summary>
    [Fact]
    public void Parse_AnEmptyInitialRotationPreference_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:InitialRotationPreference />
            </uap:VisualElements>
            """));

        Assert.Contains("Rotation", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>DefaultTile</c> that declares no <c>ShowNameOnTiles</c> is perfectly valid, so the empty
    /// list must not be conflated with the rejected empty container above.
    /// </summary>
    [Fact]
    public void Parse_ADefaultTileWithoutShowNameOnTiles_YieldsAnEmptyList()
    {
        VisualElements visuals = ParseVisuals(
            """
            <uap:VisualElements DisplayName="Contoso" Description="An app" BackgroundColor="transparent"
                                Square150x150Logo="a.png" Square44x44Logo="b.png">
              <uap:DefaultTile ShortName="Contoso" />
            </uap:VisualElements>
            """);

        Assert.Empty(Assert.IsType<DefaultTile>(visuals.DefaultTile).ShowNameOnTiles);
    }

    // TC-P1-6b
    [Fact]
    public void Parse_Capabilities_CategorizesEachDeclarationByNamespace()
    {
        IReadOnlyList<ManifestCapability> capabilities = ParseCapabilities(
            """
            <Capability Name="internetClient" />
            <uap:Capability Name="picturesLibrary" />
            <mobile:Capability Name="cellularDeviceControl" />
            <wincap:Capability Name="oemDeployment" />
            <rescap:Capability Name="packageQuery" />
            <uap4:CustomCapability Name="Contoso.myCustomCapability_q4tqhpwrkdchy" />
            <DeviceCapability Name="location" />
            """);

        Assert.Equal(
            [
                ("internetClient", CapabilityKind.General),
                ("picturesLibrary", CapabilityKind.General),
                ("cellularDeviceControl", CapabilityKind.General),
                ("oemDeployment", CapabilityKind.Windows),
                ("packageQuery", CapabilityKind.Restricted),
                ("Contoso.myCustomCapability_q4tqhpwrkdchy", CapabilityKind.Custom),
                ("location", CapabilityKind.Device),
            ],
            capabilities.Select(static c => (c.Name, c.Kind)));
    }

    // TC-P1-6c
    [Fact]
    public void Parse_RunFullTrust_IsFlaggedAsRestricted()
    {
        ManifestCapability capability = Assert.Single(
            ParseCapabilities("""<rescap:Capability Name="runFullTrust" />"""));

        Assert.Equal("runFullTrust", capability.Name);
        Assert.Equal(CapabilityKind.Restricted, capability.Kind);
        Assert.Equal(
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities",
            capability.Namespace);
    }

    /// <summary>
    /// The same name declared in the general namespace is <em>not</em> restricted: the namespace,
    /// not the name, carries the distinction.
    /// </summary>
    [Fact]
    public void Parse_ARestrictedNameInTheUapNamespace_IsNotFlaggedAsRestricted()
    {
        ManifestCapability capability = Assert.Single(
            ParseCapabilities("""<uap:Capability Name="runFullTrust" />"""));

        Assert.Equal(CapabilityKind.General, capability.Kind);
    }

    /// <summary>
    /// Capability namespaces gain a new numbered revision most Windows releases; those must classify
    /// as general rather than falling into <see cref="CapabilityKind.Unknown"/>.
    /// </summary>
    [Fact]
    public void Parse_ANumberedUapRevision_IsGeneral()
    {
        ManifestCapability capability = Assert.Single(
            ParseCapabilities("""<uap7:Capability Name="globalMediaControl" />"""));

        Assert.Equal(CapabilityKind.General, capability.Kind);
        Assert.Equal("http://schemas.microsoft.com/appx/manifest/uap/windows10/7", capability.Namespace);
    }

    /// <summary>
    /// A capability from a namespace this library has never seen is still reported — with its
    /// namespace intact — rather than dropped, so a newer package is never silently under-reported.
    /// </summary>
    [Fact]
    public void Parse_ACapabilityFromAnUnknownNamespace_IsReportedAsUnknown()
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:future="http://schemas.contoso.com/appx/manifest/future/windows10">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Capabilities>
                <future:Capability Name="somethingNew" />
              </Capabilities>
            </Package>
            """;

        AppxManifest parsed = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));
        ManifestCapability capability = Assert.Single(parsed.DeclaredCapabilities);

        Assert.Equal("somethingNew", capability.Name);
        Assert.Equal(CapabilityKind.Unknown, capability.Kind);
        Assert.Equal("http://schemas.contoso.com/appx/manifest/future/windows10", capability.Namespace);
    }

    // TC-P1-6b
    [Fact]
    public void Parse_DeviceCapability_ReadsItsDevicesAndFunctions()
    {
        ManifestCapability capability = Assert.Single(ParseCapabilities(
            """
            <DeviceCapability Name="usb">
              <Device Id="vidpid:045E 0611">
                <Function Type="classId:ff * *" />
                <Function Type="name:vendorSpecific" />
              </Device>
              <Device Id="any">
                <Function Type="name:vendorSpecific" />
              </Device>
            </DeviceCapability>
            """));

        Assert.Equal(CapabilityKind.Device, capability.Kind);
        Assert.Collection(
            capability.Devices,
            device =>
            {
                Assert.Equal("vidpid:045E 0611", device.Id);
                Assert.Equal(["classId:ff * *", "name:vendorSpecific"], device.Functions);
            },
            device =>
            {
                Assert.Equal("any", device.Id);
                Assert.Equal(["name:vendorSpecific"], device.Functions);
            });
    }

    /// <summary>
    /// A <c>DeviceCapability</c> may constrain no devices at all; only a declared <c>Device</c> must
    /// name at least one function.
    /// </summary>
    [Fact]
    public void Parse_AnUnconstrainedDeviceCapability_HasNoDevices()
    {
        ManifestCapability capability = Assert.Single(
            ParseCapabilities("""<DeviceCapability Name="location" />"""));

        Assert.Equal(CapabilityKind.Device, capability.Kind);
        Assert.Empty(capability.Devices);
    }

    [Fact]
    public void Parse_ADeviceWithoutAFunction_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseCapabilities(
            """
            <DeviceCapability Name="usb">
              <Device Id="any" />
            </DeviceCapability>
            """));

        Assert.Contains("Function", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Only a device capability carries devices; the others must not fabricate them.</summary>
    [Fact]
    public void Parse_ANonDeviceCapability_HasNoDevices()
    {
        ManifestCapability capability = Assert.Single(
            ParseCapabilities("""<Capability Name="internetClient" />"""));

        Assert.Empty(capability.Devices);
    }

    /// <summary>
    /// Namespace matching must not be a naive prefix test: a namespace that merely starts with the
    /// UAP one, or whose revision segment is not purely numeric, is a different namespace.
    /// </summary>
    [Theory]
    [InlineData("http://schemas.microsoft.com/appx/manifest/uap/windows10x")]
    [InlineData("http://schemas.microsoft.com/appx/manifest/uap/windows10/7extra")]
    [InlineData("http://schemas.microsoft.com/appx/manifest/uap/windows10/")]
    [InlineData("http://schemas.microsoft.com/appx/manifest/uap/windows10/7/8")]
    [InlineData("http://schemas.microsoft.com/appx/manifest/foundation/windows10/somethingelse")]
    public void Parse_ACapabilityFromALookalikeNamespace_IsUnknown(string ns)
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:look="{ns}">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Capabilities>
                <look:Capability Name="somethingNew" />
              </Capabilities>
            </Package>
            """;

        AppxManifest parsed = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));

        Assert.Equal(CapabilityKind.Unknown, Assert.Single(parsed.DeclaredCapabilities).Kind);
    }

    [Fact]
    public void Parse_ACapabilityWithoutAName_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => ParseCapabilities("<Capability />"));

        Assert.Contains("Name", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A borrowed local name from a foreign namespace must not inherit that name's semantics: only
    /// the foundation namespace declares <c>DeviceCapability</c>, and only <c>uap</c> declares
    /// <c>CustomCapability</c>.
    /// </summary>
    [Theory]
    [InlineData("DeviceCapability")]
    [InlineData("CustomCapability")]
    public void Parse_AFamiliarLocalNameInAForeignNamespace_IsUnknown(string localName)
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:future="http://schemas.contoso.com/appx/manifest/future/windows10">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Capabilities>
                <future:{localName} Name="somethingNew" />
              </Capabilities>
            </Package>
            """;

        AppxManifest parsed = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));

        Assert.Equal(CapabilityKind.Unknown, Assert.Single(parsed.DeclaredCapabilities).Kind);
    }

    /// <summary>
    /// A <c>uap4:CustomCapability</c> is the only custom capability the schema declares today, but a
    /// later numbered revision must still classify as custom rather than unknown.
    /// </summary>
    [Fact]
    public void Parse_CustomCapability_IsCustomAcrossUapRevisions()
    {
        Assert.Equal(
            CapabilityKind.Custom,
            Assert.Single(ParseCapabilities(
                """<uap4:CustomCapability Name="Contoso.myCustomCapability_q4tqhpwrkdchy" />""")).Kind);
    }

    /// <summary>
    /// An element name this library does not recognise is still reported when it carries a
    /// <c>Name</c> — the previous flat-list parser collected it, and a newer schema revision is not
    /// a reason to under-report a package.
    /// </summary>
    [Fact]
    public void Parse_AnUnrecognisedCapabilityElement_IsStillReported()
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:future="http://schemas.contoso.com/appx/manifest/future/windows10">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Capabilities>
                <future:QuantumCapability Name="entangle" />
                <future:QuantumCapability />
              </Capabilities>
            </Package>
            """;

        AppxManifest parsed = AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));

        // The nameless one is ignored rather than rejected, matching the pre-existing behaviour for
        // an element the parser cannot identify.
        ManifestCapability capability = Assert.Single(parsed.DeclaredCapabilities);
        Assert.Equal("entangle", capability.Name);
        Assert.Equal(CapabilityKind.Unknown, capability.Kind);
        Assert.Equal(["entangle"], parsed.Capabilities);
    }

    /// <summary>
    /// The flat name list stays de-duplicated and in document order so that the property that
    /// predates categorization keeps behaving exactly as it did.
    /// </summary>
    [Fact]
    public void Parse_Capabilities_ProjectsADeduplicatedNameList()
    {
        AppxManifest manifest = ParseManifest(
            visualElements: """<uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />""",
            capabilities:
            """
            <Capabilities>
              <Capability Name="internetClient" />
              <rescap:Capability Name="runFullTrust" />
              <Capability Name="internetClient" />
            </Capabilities>
            """);

        Assert.Equal(["internetClient", "runFullTrust"], manifest.Capabilities);
        Assert.Equal(3, manifest.DeclaredCapabilities.Count);
    }

    [Fact]
    public void Parse_WithoutACapabilitiesElement_YieldsNoCapabilities()
    {
        AppxManifest manifest = ParseManifest(
            visualElements: """<uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />""",
            capabilities: "");

        Assert.Empty(manifest.Capabilities);
        Assert.Empty(manifest.DeclaredCapabilities);
    }
}
