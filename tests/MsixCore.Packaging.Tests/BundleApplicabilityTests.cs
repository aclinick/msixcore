using System.Text;
using MsixCore.Packaging.Bundles;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Tests;

public class BundleApplicabilityTests
{
    private const string MultiArchBundle =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
          <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.2.3.4" />
          <Packages>
            <Package Type="application" Version="1.2.3.4" Architecture="x86" FileName="MyApp_x86.msix" />
            <Package Type="application" Version="1.2.3.4" Architecture="x64" FileName="MyApp_x64.msix" />
            <Package Type="application" Version="1.2.3.4" Architecture="arm64" FileName="MyApp_arm64.msix" />
          </Packages>
        </Bundle>
        """;

    private const string ResourceBundle =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
          <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.2.3.4" />
          <Packages>
            <Package Type="application" Version="1.2.3.4" Architecture="x64" FileName="MyApp_x64.msix" />
            <Package Type="resource" Version="1.2.3.4" ResourceId="en-US" FileName="lang-en-US.msix">
              <Resources><Resource Language="en-US" /></Resources>
            </Package>
            <Package Type="resource" Version="1.2.3.4" ResourceId="fr-FR" FileName="lang-fr-FR.msix">
              <Resources><Resource Language="fr-FR" /></Resources>
            </Package>
            <Package Type="resource" Version="1.2.3.4" ResourceId="scale-200" FileName="scale-200.msix">
              <Resources><Resource Scale="200" /></Resources>
            </Package>
            <Package Type="resource" Version="1.2.3.4" ResourceId="scale-400" FileName="scale-400.msix">
              <Resources><Resource Scale="400" /></Resources>
            </Package>
          </Packages>
        </Bundle>
        """;

    private static BundleManifest Parse(string xml) =>
        BundleManifestParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string[] FileNames(IEnumerable<BundlePackageEntry> packages) =>
        packages.Select(p => p.FileName).ToArray();

    // TC-P1-2a: an x64 target selects only the x64 application package.
    [Fact]
    public void Select_X64Target_ChoosesX64ApplicationOnly()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(MultiArchBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.X64 });

        Assert.Equal("MyApp_x64.msix", result.ApplicationPackage.FileName);
        Assert.Equal(["MyApp_x64.msix"], FileNames(result.ApplicablePackages));
    }

    [Fact]
    public void Select_Arm64Target_PrefersNativeArm64OverEmulatedX64()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(MultiArchBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.Arm64 });

        Assert.Equal("MyApp_arm64.msix", result.ApplicationPackage.FileName);

        // x64 and x86 remain runnable under emulation, but must rank below native.
        Assert.Equal(
            ["MyApp_arm64.msix", "MyApp_x64.msix", "MyApp_x86.msix"],
            FileNames(result.CandidateApplicationPackages));
    }

    [Fact]
    public void Select_X64Target_FallsBackToX86UnderWow64()
    {
        const string x86Only =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x86" FileName="a_x86.msix" />
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(x86Only),
            new BundleTarget { Architecture = ProcessorArchitecture.X64 });

        Assert.Equal("a_x86.msix", result.ApplicationPackage.FileName);
    }

    [Fact]
    public void Select_NeutralApplication_AppliesToEveryArchitecture()
    {
        const string neutral =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="neutral" FileName="a_neutral.msix" />
              </Packages>
            </Bundle>
            """;

        BundleManifest manifest = Parse(neutral);

        foreach (ProcessorArchitecture architecture in new[]
        {
            ProcessorArchitecture.X86,
            ProcessorArchitecture.X64,
            ProcessorArchitecture.Arm,
            ProcessorArchitecture.Arm64,
        })
        {
            BundleApplicabilityResult result = BundleApplicability.Select(
                manifest,
                new BundleTarget { Architecture = architecture });

            Assert.Equal("a_neutral.msix", result.ApplicationPackage.FileName);
        }
    }

    // TC-P1-2c: no applicable architecture produces a clear, categorized error.
    [Fact]
    public void Select_NoApplicableArchitecture_ThrowsWithAvailableArchitectures()
    {
        const string arm64Only =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="arm64" FileName="a_arm64.msix" />
              </Packages>
            </Bundle>
            """;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            BundleApplicability.Select(
                Parse(arm64Only),
                new BundleTarget { Architecture = ProcessorArchitecture.X86 }));

        Assert.Contains("X86", error.Message, StringComparison.Ordinal);
        Assert.Contains("Arm64", error.Message, StringComparison.Ordinal);
        Assert.True(MsixError.TryGetCode(error, out MsixErrorCode code));
        Assert.Equal(MsixErrorCode.NoApplicablePackage, code);
    }

    [Fact]
    public void Select_BundleWithNoApplicationPackage_Throws()
    {
        const string resourceOnly =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="resource" Version="1.0.0.0" ResourceId="en" FileName="lang-en.msix">
                  <Resources><Resource Language="en" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            BundleApplicability.Select(
                Parse(resourceOnly),
                new BundleTarget { Architecture = ProcessorArchitecture.X64 }));

        Assert.True(MsixError.TryGetCode(error, out MsixErrorCode code));
        Assert.Equal(MsixErrorCode.NoApplicablePackage, code);
    }

    // TC-P1-2b: fr-FR + scale-200 selects the matching resources and excludes the rest.
    [Fact]
    public void Select_FrenchScale200_ChoosesMatchingResourcesOnly()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["fr-FR"],
                Scale = 200,
            });

        Assert.Equal(
            ["MyApp_x64.msix", "lang-fr-FR.msix", "scale-200.msix"],
            FileNames(result.ApplicablePackages));
    }

    [Fact]
    public void Select_UnavailableScale_RoundsUpToNextLargest()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["en-US"],
                Scale = 250,
            });

        Assert.Contains("scale-400.msix", FileNames(result.ResourcePackages));
        Assert.DoesNotContain("scale-200.msix", FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_ScaleAboveEverythingAvailable_UsesLargest()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["en-US"],
                Scale = 800,
            });

        Assert.Contains("scale-400.msix", FileNames(result.ResourcePackages));
        Assert.DoesNotContain("scale-200.msix", FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_UnspecifiedScale_DoesNotFilterScaleResources()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["en-US"] });

        Assert.Contains("scale-200.msix", FileNames(result.ResourcePackages));
        Assert.Contains("scale-400.msix", FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_UnspecifiedLanguages_DoesNotFilterLanguageResources()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.X64 });

        Assert.Contains("lang-en-US.msix", FileNames(result.ResourcePackages));
        Assert.Contains("lang-fr-FR.msix", FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_RegionNeutralRequest_PrefersExactOverSiblingRegion()
    {
        const string frenchRegions =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr-FR" FileName="fr-FR.msix">
                  <Resources><Resource Language="fr-FR" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr-CA" FileName="fr-CA.msix">
                  <Resources><Resource Language="fr-CA" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(frenchRegions),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["fr-FR"] });

        Assert.Equal(["fr-FR.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_NoExactLanguageMatch_FallsBackToSiblingRegions()
    {
        const string frenchRegions =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr-CA" FileName="fr-CA.msix">
                  <Resources><Resource Language="fr-CA" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(frenchRegions),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["fr-FR"] });

        Assert.Equal(["fr-CA.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_UnqualifiedResourcePackage_IsAlwaysIncluded()
    {
        const string unqualified =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="common" FileName="common.msix" />
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(unqualified),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["ja-JP"],
                Scale = 100,
            });

        Assert.Equal(["common.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_DXFeatureLevel_FiltersWhenTargetSpecifiesOne()
    {
        const string dx =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="dx10" FileName="dx10.msix">
                  <Resources><Resource DXFeatureLevel="DX10" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="dx11" FileName="dx11.msix">
                  <Resources><Resource DXFeatureLevel="DX11" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(dx),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, DXFeatureLevel = "dx11" });

        Assert.Equal(["dx11.msix"], FileNames(result.ResourcePackages));
    }

    [Theory]
    [InlineData("x-private")]
    [InlineData("")]
    public void Select_UnsupportedTargetLanguage_Throws(string language)
    {
        // Silently dropping it would eventually leave no requested languages at all, which disables
        // language filtering and quietly selects every language in the bundle.
        Assert.Throws<ArgumentException>(() => BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["en-US", language],
            }));
    }

    [Fact]
    public void Select_UnsupportedDeclaredLanguage_IsNotTreatedAsUnqualified()
    {
        // A package declaring a language we cannot parse is still language-qualified. Treating it as
        // unqualified would make it applicable to every device instead of none.
        const string bundle =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
              <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.2.3.4" />
              <Packages>
                <Package Type="application" Version="1.2.3.4" Architecture="x64" FileName="MyApp_x64.msix" />
                <Package Type="resource" Version="1.2.3.4" ResourceId="odd" FileName="odd.msix">
                  <Resources><Resource Language="x-private" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(bundle),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["en-US"] });

        Assert.Empty(result.ResourcePackages);
    }

    [Fact]
    public void Select_ScaleOfNonSelectedLanguage_DoesNotEliminateEverything()
    {
        // Scale must be resolved among the packages that survive language filtering. Resolving it
        // over the whole bundle first would choose 200 (round up from 150) because of the French
        // package, then drop the English package on scale and the French one on language.
        const string bundle =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle" SchemaVersion="4.0">
              <Identity Name="Contoso.MyApp" Publisher="CN=Contoso" Version="1.2.3.4" />
              <Packages>
                <Package Type="application" Version="1.2.3.4" Architecture="x64" FileName="MyApp_x64.msix" />
                <Package Type="resource" Version="1.2.3.4" ResourceId="en" FileName="en-100.msix">
                  <Resources><Resource Language="en" Scale="100" /></Resources>
                </Package>
                <Package Type="resource" Version="1.2.3.4" ResourceId="fr" FileName="fr-200.msix">
                  <Resources><Resource Language="fr" Scale="200" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(bundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["en"],
                Scale = 150,
            });

        Assert.Equal(["en-100.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_SkipAll_ReturnsEveryResourcePackage()
    {
        BundleManifest manifest = Parse(ResourceBundle);

        BundleApplicabilityResult result = BundleApplicability.Select(
            manifest,
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X86,
                Languages = ["ja-JP"],
                Scale = 100,
            },
            BundleApplicabilityOptions.All);

        Assert.Equal(manifest.Packages.Count, result.ApplicablePackages.Count);
    }

    [Fact]
    public void Select_SkipAll_StillReturnsOneApplicationPackage()
    {
        // 'All' means "apply no qualifier", not "install everything": a bundle's application
        // packages are alternatives to one another, so only one can ever be installed.
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(MultiArchBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.Arm64 },
            BundleApplicabilityOptions.All);

        Assert.Equal("MyApp_arm64.msix", result.ApplicationPackage!.FileName);
        Assert.Equal(3, result.CandidateApplicationPackages.Count);
        Assert.Single(result.ApplicablePackages);
    }

    [Fact]
    public void Select_SkipArchitecture_KeepsUnrunnableApplication()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(MultiArchBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.X86 },
            BundleApplicabilityOptions.SkipArchitecture);

        Assert.Equal(3, result.CandidateApplicationPackages.Count);
    }

    [Fact]
    public void Select_UnknownTargetArchitecture_Throws()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            BundleApplicability.Select(
                Parse(MultiArchBundle),
                new BundleTarget { Architecture = ProcessorArchitecture.Unknown }));

        Assert.True(MsixError.TryGetCode(error, out MsixErrorCode code));
        Assert.Equal(MsixErrorCode.NoApplicablePackage, code);
    }

    [Fact]
    public void Select_PreservesBundleManifestOrder()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget { Architecture = ProcessorArchitecture.X64 },
            BundleApplicabilityOptions.All);

        Assert.Equal(
            ["MyApp_x64.msix", "lang-en-US.msix", "lang-fr-FR.msix", "scale-200.msix", "scale-400.msix"],
            FileNames(result.ApplicablePackages));
    }

    [Fact]
    public void Select_UndeterminedPackage_DoesNotSuppressLanguageFallback()
    {
        // 'und' carries language-neutral payload, so it must not count as "French was found" and
        // leave a fr-FR user with no French at all.
        const string undAndSibling =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="und" FileName="und.msix">
                  <Resources><Resource Language="und" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr-CA" FileName="fr-CA.msix">
                  <Resources><Resource Language="fr-CA" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(undAndSibling),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["fr-FR"] });

        Assert.Equal(["und.msix", "fr-CA.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_ExactMatchPresent_StillSuppressesSiblingRegion()
    {
        // The mirror of the 'und' case: a real French match must suppress fr-CA.
        const string undAndExact =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="und" FileName="und.msix">
                  <Resources><Resource Language="und" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr-FR" FileName="fr-FR.msix">
                  <Resources><Resource Language="fr-FR" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr-CA" FileName="fr-CA.msix">
                  <Resources><Resource Language="fr-CA" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(undAndExact),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["fr-FR"] });

        Assert.Equal(["und.msix", "fr-FR.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_DuplicateManifestEntries_AreNotConflated()
    {
        // BundlePackageEntry is a record; selection must be tracked by manifest position so that a
        // value-based lookup cannot conflate two structurally identical entries.
        const string duplicates =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr" FileName="fr.msix">
                  <Resources><Resource Language="fr-FR" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="fr" FileName="fr.msix">
                  <Resources><Resource Language="fr-FR" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="de" FileName="de.msix">
                  <Resources><Resource Language="de-DE" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(duplicates),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, Languages = ["fr-FR"] });

        // Both declared entries are reported, and the non-matching German package is still excluded.
        Assert.Equal(["fr.msix", "fr.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_SkipLanguageOnly_KeepsAllLanguagesButStillFiltersScale()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["ja-JP"],
                Scale = 200,
            },
            BundleApplicabilityOptions.SkipLanguage);

        Assert.Equal(
            ["lang-en-US.msix", "lang-fr-FR.msix", "scale-200.msix"],
            FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_SkipScaleOnly_KeepsAllScalesButStillFiltersLanguage()
    {
        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(ResourceBundle),
            new BundleTarget
            {
                Architecture = ProcessorArchitecture.X64,
                Languages = ["fr-FR"],
                Scale = 200,
            },
            BundleApplicabilityOptions.SkipScale);

        Assert.Equal(
            ["lang-fr-FR.msix", "scale-200.msix", "scale-400.msix"],
            FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_SkipDXFeatureLevelOnly_KeepsAllDXResources()
    {
        const string dx =
            """
            <Bundle xmlns="http://schemas.microsoft.com/appx/2013/bundle">
              <Identity Name="A" Publisher="CN=C" Version="1.0.0.0" />
              <Packages>
                <Package Type="application" Version="1.0.0.0" Architecture="x64" FileName="a.msix" />
                <Package Type="resource" Version="1.0.0.0" ResourceId="dx10" FileName="dx10.msix">
                  <Resources><Resource DXFeatureLevel="DX10" /></Resources>
                </Package>
                <Package Type="resource" Version="1.0.0.0" ResourceId="dx11" FileName="dx11.msix">
                  <Resources><Resource DXFeatureLevel="DX11" /></Resources>
                </Package>
              </Packages>
            </Bundle>
            """;

        BundleApplicabilityResult result = BundleApplicability.Select(
            Parse(dx),
            new BundleTarget { Architecture = ProcessorArchitecture.X64, DXFeatureLevel = "DX11" },
            BundleApplicabilityOptions.SkipDXFeatureLevel);

        Assert.Equal(["dx10.msix", "dx11.msix"], FileNames(result.ResourcePackages));
    }

    [Fact]
    public void Select_NullArguments_Throw()
    {
        BundleManifest manifest = Parse(MultiArchBundle);

        Assert.Throws<ArgumentNullException>(() =>
            BundleApplicability.Select(null!, new BundleTarget { Architecture = ProcessorArchitecture.X64 }));
        Assert.Throws<ArgumentNullException>(() => BundleApplicability.Select(manifest, null!));
    }

    [Fact]
    public void BundleTargetCurrent_ReportsRunnableArchitectureAndLanguages()
    {
        BundleTarget target = BundleTarget.Current();

        Assert.NotEqual(ProcessorArchitecture.Unknown, target.Architecture);
        Assert.NotEmpty(target.Languages);

        // Scale and DXFL are deliberately unset so they cannot silently drop resource packages.
        Assert.Null(target.Scale);
        Assert.Null(target.DXFeatureLevel);
    }
}
