using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Xml.Linq;
using MsixCore.Packaging.Authoring;

namespace MsixCore.CorpusRoundtrip;

internal static class Program
{
    private const string MakeAppxPath = @"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1)
            {
                Usage();
                return 2;
            }

            return args[0].ToLowerInvariant() switch
            {
                "read" when args.Length == 2 => Read(args[1]),
                "pack" when args.Length == 3 => Pack(args[1], args[2], rewriteIdentity: false),
                "pack-throwaway" when args.Length == 5 => Pack(args[1], args[2], rewriteIdentity: true, args[3], args[4]),
                "pack-source" when args.Length == 3 => PackSource(args[1], args[2]),
                "variant" when args.Length == 4 => VariantZipRewriter.WriteVariant(args[1], args[2], args[3]),
                _ => Usage(),
            };
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine(FormattableString.Invariant($"COM failure HRESULT=0x{ex.HResult:X8}: {ex.Message}"));
            return 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  read <package.msix>");
        Console.Error.WriteLine("  pack <input.msix> <workdir>");
        Console.Error.WriteLine("  pack-throwaway <input.msix> <workdir> <identity-name> <publisher>");
        Console.Error.WriteLine("  pack-source <source-dir> <workdir>");
        Console.Error.WriteLine("  variant <baseline.msix> <output.msix> <baseline|zip64|descriptor|both|no-utf8>");
        return 2;
    }

    private static int PackSource(string sourceDirectory, string workdir)
    {
        Directory.CreateDirectory(workdir);
        MsixPackageBuilder.Build(
            sourceDirectory,
            Path.Combine(workdir, "ours-stored.msix"),
            new PackOptions { Overwrite = true, CompressionLevel = CompressionLevel.NoCompression });
        MsixPackageBuilder.Build(
            sourceDirectory,
            Path.Combine(workdir, "ours-optimal.msix"),
            new PackOptions { Overwrite = true, CompressionLevel = CompressionLevel.Optimal });
        ToolOutcome makeAppx = new MakeAppxRunner(File.Exists(MakeAppxPath) ? MakeAppxPath : null)
            .Pack(sourceDirectory, Path.Combine(workdir, "makeappx.msix"), RoundtripMode.Optimal);
        if (!makeAppx.Succeeded)
        {
            Console.Error.WriteLine(makeAppx.Message);
            return makeAppx.Skipped ? 0 : 1;
        }

        Console.WriteLine(FormattableString.Invariant($"ours-stored={Path.Combine(workdir, "ours-stored.msix")}"));
        Console.WriteLine(FormattableString.Invariant($"ours-optimal={Path.Combine(workdir, "ours-optimal.msix")}"));
        Console.WriteLine(FormattableString.Invariant($"makeappx={Path.Combine(workdir, "makeappx.msix")}"));
        return 0;
    }

    private static int Read(string packagePath)
    {
        AppxOsReader.Read(packagePath);
        return 0;
    }

    private static int Pack(
        string inputPackage,
        string workdir,
        bool rewriteIdentity,
        string? identityName = null,
        string? publisher = null)
    {
        string normalized = Path.Combine(workdir, "source");
        Directory.CreateDirectory(workdir);
        SourceNormalizer.Normalize(inputPackage, normalized);
        if (rewriteIdentity)
        {
            RewriteIdentity(Path.Combine(normalized, "AppxManifest.xml"), identityName!, publisher!);
        }

        MsixPackageBuilder.Build(
            normalized,
            Path.Combine(workdir, "ours-stored.msix"),
            new PackOptions { Overwrite = true, CompressionLevel = CompressionLevel.NoCompression });
        MsixPackageBuilder.Build(
            normalized,
            Path.Combine(workdir, "ours-optimal.msix"),
            new PackOptions { Overwrite = true, CompressionLevel = CompressionLevel.Optimal });

        ToolOutcome makeAppx = new MakeAppxRunner(File.Exists(MakeAppxPath) ? MakeAppxPath : null)
            .Pack(normalized, Path.Combine(workdir, "makeappx.msix"), RoundtripMode.Optimal);
        if (!makeAppx.Succeeded)
        {
            Console.Error.WriteLine(makeAppx.Message);
            return makeAppx.Skipped ? 0 : 1;
        }

        Console.WriteLine(FormattableString.Invariant($"source={normalized}"));
        Console.WriteLine(FormattableString.Invariant($"ours-stored={Path.Combine(workdir, "ours-stored.msix")}"));
        Console.WriteLine(FormattableString.Invariant($"ours-optimal={Path.Combine(workdir, "ours-optimal.msix")}"));
        Console.WriteLine(FormattableString.Invariant($"makeappx={Path.Combine(workdir, "makeappx.msix")}"));
        return 0;
    }

    private static void RewriteIdentity(string manifestPath, string name, string publisher)
    {
        XDocument document = XDocument.Load(manifestPath, LoadOptions.PreserveWhitespace);
        XElement identity = document.Descendants().First(static element => element.Name.LocalName == "Identity");
        identity.SetAttributeValue("Name", name);
        identity.SetAttributeValue("Publisher", publisher);
        document.Save(manifestPath, SaveOptions.DisableFormatting);
    }
}
