using System.Text;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Covers the <c>Extensions</c> declarations: the OS integration points a package registers.
/// Parsing only — msixcore surfaces these for tooling and does not yet register them with the OS.
/// </summary>
public class ManifestExtensionTests
{
    private const string Publisher =
        "CN=Contoso Corporation, O=Contoso Corporation, L=Redmond, S=Washington, C=US";

    /// <summary>Parses a manifest whose single application declares <paramref name="body"/>.</summary>
    private static AppxManifest ParseAppExtensions(string body) => ParseManifest(
        applicationExtensions: $"<Extensions>{body}</Extensions>",
        packageExtensions: "");

    private static AppxManifest ParseManifest(string applicationExtensions, string packageExtensions)
    {
        string manifest =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package
              xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
              xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
              xmlns:uap2="http://schemas.microsoft.com/appx/manifest/uap/windows10/2"
              xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
              xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
              xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
              xmlns:desktop7="http://schemas.microsoft.com/appx/manifest/desktop/windows10/7"
              xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10">
              <Identity Name="Contoso.MyApp" Publisher="{Publisher}" Version="1.0.0.0" ProcessorArchitecture="x64" />
              <Properties>
                <DisplayName>App</DisplayName>
                <PublisherDisplayName>Contoso</PublisherDisplayName>
              </Properties>
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
              </Dependencies>
              <Applications>
                <Application Id="App" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="App" Description="App" BackgroundColor="transparent" Square150x150Logo="a.png" Square44x44Logo="b.png" />
            {applicationExtensions}
                </Application>
              </Applications>
            {packageExtensions}
            </Package>
            """;

        return AppxManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(manifest)));
    }

    private static AppExtension SingleExtension(AppxManifest manifest) =>
        Assert.Single(Assert.Single(manifest.Applications).Extensions);

    // TC-P1-4a
    [Fact]
    public void Parse_FileTypeAssociation_ReadsTheNameAndExtensions()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap:Extension Category="windows.fileTypeAssociation">
              <uap:FileTypeAssociation Name="contoso-doc">
                <uap:DisplayName>Contoso Document</uap:DisplayName>
                <uap:Logo>doc.png</uap:Logo>
                <uap:InfoTip>A Contoso document</uap:InfoTip>
                <uap:SupportedFileTypes>
                  <uap:FileType ContentType="application/x-contoso">.cdoc</uap:FileType>
                  <uap:FileType>.cdx</uap:FileType>
                </uap:SupportedFileTypes>
              </uap:FileTypeAssociation>
            </uap:Extension>
            """);

        AppExtension extension = SingleExtension(manifest);
        Assert.Equal("windows.fileTypeAssociation", extension.Category);
        FileTypeAssociationExtension fta = Assert.IsType<FileTypeAssociationExtension>(extension.Payload);
        Assert.Equal("contoso-doc", fta.Name);
        Assert.Equal("Contoso Document", fta.DisplayName);
        Assert.Equal("doc.png", fta.Logo);
        Assert.Equal("A Contoso document", fta.InfoTip);
        Assert.Equal([".cdoc", ".cdx"], fta.FileTypes.Select(t => t.Extension));
        Assert.Equal("application/x-contoso", fta.FileTypes[0].ContentType);
        Assert.Null(fta.FileTypes[1].ContentType);
    }

    [Fact]
    public void Parse_FileTypeAssociation_PreservesTheLeadingDotAndCase()
    {
        // The schema requires the leading dot; normalising or lower-casing here would hide a
        // manifest defect from tooling that reports what the package actually declares.
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap:Extension Category="windows.fileTypeAssociation">
              <uap:FileTypeAssociation Name="contoso-doc">
                <uap:SupportedFileTypes><uap:FileType>.CDoc</uap:FileType></uap:SupportedFileTypes>
              </uap:FileTypeAssociation>
            </uap:Extension>
            """);

        var fta = (FileTypeAssociationExtension)SingleExtension(manifest).Payload!;
        Assert.Equal(".CDoc", Assert.Single(fta.FileTypes).Extension);
    }

    [Fact]
    public void Parse_FileTypeAssociation_WithoutAName_IsRejected()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseAppExtensions(
            """
            <uap:Extension Category="windows.fileTypeAssociation">
              <uap:FileTypeAssociation>
                <uap:SupportedFileTypes><uap:FileType>.cdoc</uap:FileType></uap:SupportedFileTypes>
              </uap:FileTypeAssociation>
            </uap:Extension>
            """));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    // TC-P1-4b
    [Fact]
    public void Parse_Protocol_ReadsTheScheme()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap:Extension Category="windows.protocol">
              <uap:Protocol Name="myscheme" ReturnResults="optional" DesiredView="useHalf">
                <uap:DisplayName>My Scheme</uap:DisplayName>
                <uap:Logo>scheme.png</uap:Logo>
              </uap:Protocol>
            </uap:Extension>
            """);

        ProtocolExtension protocol = Assert.IsType<ProtocolExtension>(SingleExtension(manifest).Payload);
        Assert.Equal("myscheme", protocol.Name);
        Assert.Equal("My Scheme", protocol.DisplayName);
        Assert.Equal("scheme.png", protocol.Logo);
        Assert.Equal("optional", protocol.ReturnResults);
        Assert.Equal("useHalf", protocol.DesiredView);
        Assert.Null(protocol.Parameters);
    }

    [Fact]
    public void Parse_Uap3Protocol_ReadsTheParameters()
    {
        // Parameters exists only on the uap3 form; both forms collapse onto one model.
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap3:Extension Category="windows.protocol">
              <uap3:Protocol Name="myscheme" Parameters="--url %1" />
            </uap3:Extension>
            """);

        ProtocolExtension protocol = Assert.IsType<ProtocolExtension>(SingleExtension(manifest).Payload);
        Assert.Equal("--url %1", protocol.Parameters);
    }

    // TC-P1-4c
    [Fact]
    public void Parse_Uap5AppExecutionAlias_ReadsEveryAlias()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap5:Extension Category="windows.appExecutionAlias">
              <uap5:AppExecutionAlias>
                <uap5:ExecutionAlias Alias="contoso.exe" />
                <uap5:ExecutionAlias Alias="contoso-cli.exe" />
              </uap5:AppExecutionAlias>
            </uap5:Extension>
            """);

        var alias = Assert.IsType<AppExecutionAliasExtension>(SingleExtension(manifest).Payload);
        Assert.Equal(["contoso.exe", "contoso-cli.exe"], alias.Aliases);
    }

    [Fact]
    public void Parse_Uap3AppExecutionAlias_UsesTheDesktopExecutionAliasChild()
    {
        // The uap3 form nests desktop:ExecutionAlias, not uap3:ExecutionAlias. Local-name matching
        // means the same model serves both.
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap3:Extension Category="windows.appExecutionAlias">
              <uap3:AppExecutionAlias>
                <desktop:ExecutionAlias Alias="contoso.exe" />
              </uap3:AppExecutionAlias>
            </uap3:Extension>
            """);

        var alias = Assert.IsType<AppExecutionAliasExtension>(SingleExtension(manifest).Payload);
        Assert.Equal("contoso.exe", Assert.Single(alias.Aliases));
    }

    // TC-P1-4d
    [Fact]
    public void Parse_StartupTask_ReadsTheTaskIdAndEnabledFlag()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <desktop:Extension Category="windows.startupTask" Executable="App.exe" EntryPoint="Windows.FullTrustApplication">
              <desktop:StartupTask TaskId="ContosoStartup" Enabled="true" DisplayName="Contoso" />
            </desktop:Extension>
            """);

        AppExtension extension = SingleExtension(manifest);
        Assert.Equal("App.exe", extension.Executable);
        Assert.Equal("Windows.FullTrustApplication", extension.EntryPoint);
        StartupTaskExtension task = Assert.IsType<StartupTaskExtension>(extension.Payload);
        Assert.Equal("ContosoStartup", task.TaskId);
        Assert.True(task.IsEnabled);
        Assert.Equal("Contoso", task.DisplayName);
    }

    [Fact]
    public void Parse_StartupTask_WithoutEnabled_ReportsItUnstated()
    {
        // The schema declares no default, so "absent" must stay distinct from "false".
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap5:Extension Category="windows.startupTask">
              <uap5:StartupTask TaskId="ContosoStartup" />
            </uap5:Extension>
            """);

        var task = Assert.IsType<StartupTaskExtension>(SingleExtension(manifest).Payload);
        Assert.Null(task.IsEnabled);
    }

    [Fact]
    public void Parse_StartupTask_WithAnInvalidEnabledValue_IsRejected()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseAppExtensions(
            """
            <desktop:Extension Category="windows.startupTask">
              <desktop:StartupTask TaskId="ContosoStartup" Enabled="yes" />
            </desktop:Extension>
            """));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    // TC-P1-4e
    [Fact]
    public void Parse_ComServer_ReadsExeServersAndClasses()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <com:Extension Category="windows.comServer">
              <com:ComServer>
                <com:ExeServer Executable="Server.exe" Arguments="-Embedding" DisplayName="Contoso Server">
                  <com:Class Id="8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21" DisplayName="Doc" ProgId="Contoso.Document.1" />
                </com:ExeServer>
                <com:SurrogateServer AppId="1b7f4d2e-3a5c-4a6b-8f9d-0c1e2a3b4c5d" DisplayName="Surrogate">
                  <com:Class Id="a1b2c3d4-e5f6-4708-9a0b-1c2d3e4f5061" Path="Inproc.dll" ThreadingModel="both" />
                </com:SurrogateServer>
                <com:ProgId Id="Contoso.Document.1" Clsid="8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21" />
              </com:ComServer>
            </com:Extension>
            """);

        ComServerExtension com = Assert.IsType<ComServerExtension>(SingleExtension(manifest).Payload);

        ComExeServer exe = Assert.Single(com.ExeServers);
        Assert.Equal("Server.exe", exe.Executable);
        Assert.Equal("-Embedding", exe.Arguments);
        Assert.Equal("Contoso Server", exe.DisplayName);
        ComClass exeClass = Assert.Single(exe.Classes);
        Assert.Equal("8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21", exeClass.Id);
        Assert.Equal("Contoso.Document.1", exeClass.ProgId);
        // Path/ThreadingModel are declared only on a surrogate class.
        Assert.Null(exeClass.Path);
        Assert.Null(exeClass.ThreadingModel);

        ComSurrogateServer surrogate = Assert.Single(com.SurrogateServers);
        Assert.Equal("1b7f4d2e-3a5c-4a6b-8f9d-0c1e2a3b4c5d", surrogate.AppId);
        ComClass surrogateClass = Assert.Single(surrogate.Classes);
        Assert.Equal("Inproc.dll", surrogateClass.Path);
        Assert.Equal("both", surrogateClass.ThreadingModel);

        ComProgId progId = Assert.Single(com.ProgIds);
        Assert.Equal("Contoso.Document.1", progId.Id);
        Assert.Equal("8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21", progId.Clsid);
    }

    [Fact]
    public void Parse_ComClass_WithoutAnId_IsRejected()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseAppExtensions(
            """
            <com:Extension Category="windows.comServer">
              <com:ComServer>
                <com:ExeServer Executable="Server.exe"><com:Class DisplayName="Doc" /></com:ExeServer>
              </com:ComServer>
            </com:Extension>
            """));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    [Fact]
    public void Parse_SurrogateClass_WithoutAThreadingModel_IsRejected()
    {
        // ThreadingModel is required on a surrogate class in every COM schema revision.
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseAppExtensions(
            """
            <com:Extension Category="windows.comServer">
              <com:ComServer>
                <com:SurrogateServer>
                  <com:Class Id="a1b2c3d4-e5f6-4708-9a0b-1c2d3e4f5061" Path="Inproc.dll" />
                </com:SurrogateServer>
              </com:ComServer>
            </com:Extension>
            """));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    [Fact]
    public void Parse_ExeServerClass_DoesNotRequireAThreadingModel()
    {
        // The attribute is not declared on an ExeServer class at all.
        AppxManifest manifest = ParseAppExtensions(
            """
            <com:Extension Category="windows.comServer">
              <com:ComServer>
                <com:ExeServer Executable="Server.exe">
                  <com:Class Id="8e0d5c1f-2e2b-4f8a-9d3a-6b1c9f4a7e21" />
                </com:ExeServer>
              </com:ComServer>
            </com:Extension>
            """);

        var com = (ComServerExtension)SingleExtension(manifest).Payload!;
        Assert.Null(Assert.Single(Assert.Single(com.ExeServers).Classes).ThreadingModel);
    }

    // TC-P1-4f
    [Fact]
    public void Parse_FullTrustProcess_ReadsTheExecutableAndParameterGroups()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <desktop:Extension Category="windows.fullTrustProcess" Executable="Helper.exe">
              <desktop:FullTrustProcess>
                <desktop:ParameterGroup GroupId="sync" Parameters="/sync" />
                <desktop:ParameterGroup GroupId="settings" Parameters="/settings" />
              </desktop:FullTrustProcess>
            </desktop:Extension>
            """);

        AppExtension extension = SingleExtension(manifest);
        // The executable is an attribute of the Extension, not of FullTrustProcess.
        Assert.Equal("Helper.exe", extension.Executable);
        var process = Assert.IsType<FullTrustProcessExtension>(extension.Payload);
        Assert.Equal(["sync", "settings"], process.ParameterGroups.Select(g => g.GroupId));
        Assert.Equal(["/sync", "/settings"], process.ParameterGroups.Select(g => g.Parameters));
    }

    [Fact]
    public void Parse_FullTrustProcess_WithoutAChildElement_IsAccepted()
    {
        // The child choice is minOccurs="0"; a bare declaration is both valid and common.
        AppxManifest manifest = ParseAppExtensions(
            """<desktop:Extension Category="windows.fullTrustProcess" Executable="Helper.exe" />""");

        AppExtension extension = SingleExtension(manifest);
        Assert.Equal("windows.fullTrustProcess", extension.Category);
        Assert.Equal("Helper.exe", extension.Executable);
        Assert.Null(extension.Payload);
    }

    [Fact]
    public void Parse_Shortcut_ReadsTheFileAndIcon()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <desktop7:Extension Category="windows.shortcut">
              <desktop7:Shortcut File="Contoso.lnk" Icon="icon.ico" Arguments="/new" Description="New doc" PinToStartMenu="true" />
            </desktop7:Extension>
            """);

        ShortcutExtension shortcut = Assert.IsType<ShortcutExtension>(SingleExtension(manifest).Payload);
        Assert.Equal("Contoso.lnk", shortcut.File);
        Assert.Equal("icon.ico", shortcut.Icon);
        Assert.Equal("/new", shortcut.Arguments);
        Assert.Equal("New doc", shortcut.Description);
        Assert.True(shortcut.PinToStartMenu);
    }

    [Fact]
    public void Parse_AnUnrecognisedCategory_IsReportedWithoutAPayload()
    {
        // The category enumeration grows with every schema revision; failing on an unfamiliar one
        // would reject packages that are valid against a newer schema than this library knows.
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap:Extension Category="windows.somethingFromTheFuture">
              <uap:SomethingNew Whatever="1" />
            </uap:Extension>
            """);

        AppExtension extension = SingleExtension(manifest);
        Assert.Equal("windows.somethingFromTheFuture", extension.Category);
        Assert.Null(extension.Payload);
    }

    [Fact]
    public void Parse_AnExtensionWithoutACategory_IsRejected()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ParseAppExtensions(
            """<uap:Extension Executable="App.exe" />"""));

        Assert.Equal(MsixErrorCode.ManifestSemantics, MsixError.GetCode(error));
    }

    [Fact]
    public void Parse_PackageLevelExtensions_AreSeparateFromApplicationExtensions()
    {
        AppxManifest manifest = ParseManifest(
            applicationExtensions:
                """
                <Extensions>
                  <uap:Extension Category="windows.protocol">
                    <uap:Protocol Name="myscheme" />
                  </uap:Extension>
                </Extensions>
                """,
            packageExtensions:
                """
                <Extensions>
                  <desktop7:Extension Category="windows.shortcut">
                    <desktop7:Shortcut File="Contoso.lnk" Icon="icon.ico" />
                  </desktop7:Extension>
                </Extensions>
                """);

        Assert.Equal("windows.shortcut", Assert.Single(manifest.Extensions).Category);
        Assert.Equal("windows.protocol", SingleExtension(manifest).Category);
    }

    [Fact]
    public void Parse_TwoPackageLevelExtensionContainers_AreBothRead()
    {
        // The foundation <Extensions> and <com:Extensions> are distinct elements that may both
        // appear at package level, in either order. Reading only the first would drop one.
        AppxManifest manifest = ParseManifest(
            applicationExtensions: "",
            packageExtensions:
                """
                <com:Extensions>
                  <com:Extension Category="windows.comInterface" />
                </com:Extensions>
                <Extensions>
                  <desktop7:Extension Category="windows.shortcut">
                    <desktop7:Shortcut File="Contoso.lnk" Icon="icon.ico" />
                  </desktop7:Extension>
                </Extensions>
                """);

        Assert.Equal(
            ["windows.comInterface", "windows.shortcut"],
            manifest.Extensions.Select(e => e.Category));
    }

    [Fact]
    public void Parse_WithoutAnExtensionsElement_YieldsEmptyLists()
    {
        AppxManifest manifest = ParseManifest(applicationExtensions: "", packageExtensions: "");

        Assert.Empty(manifest.Extensions);
        Assert.Empty(Assert.Single(manifest.Applications).Extensions);
    }

    [Fact]
    public void Parse_SeveralExtensions_KeepsManifestOrder()
    {
        AppxManifest manifest = ParseAppExtensions(
            """
            <uap:Extension Category="windows.protocol"><uap:Protocol Name="one" /></uap:Extension>
            <uap:Extension Category="windows.fileTypeAssociation">
              <uap:FileTypeAssociation Name="two">
                <uap:SupportedFileTypes><uap:FileType>.two</uap:FileType></uap:SupportedFileTypes>
              </uap:FileTypeAssociation>
            </uap:Extension>
            <desktop:Extension Category="windows.fullTrustProcess" Executable="Helper.exe" />
            """);

        Assert.Equal(
            ["windows.protocol", "windows.fileTypeAssociation", "windows.fullTrustProcess"],
            Assert.Single(manifest.Applications).Extensions.Select(e => e.Category));
    }
}
