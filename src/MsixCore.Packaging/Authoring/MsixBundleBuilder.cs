using System.IO.Compression;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Manifest;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Authoring;

/// <summary>Builds unsigned MSIX/APPX bundles from already-built package files.</summary>
public sealed class MsixBundleBuilder
{
    private readonly List<string> _packagePaths = [];

    /// <summary>Adds an MSIX/APPX package to the bundle.</summary>
    public MsixBundleBuilder AddPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        string fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The child package was not found.", fullPath);
        }

        ValidatePackageExtension(fullPath);
        _packagePaths.Add(fullPath);
        return this;
    }

    /// <summary>Builds a bundle from the supplied MSIX/APPX package paths.</summary>
    public static BundleResult Build(
        IEnumerable<string> packagePaths,
        string outputPath,
        BundleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packagePaths);
        var builder = new MsixBundleBuilder();
        foreach (string packagePath in packagePaths)
        {
            builder.AddPackage(packagePath);
        }

        return builder.Build(outputPath, options);
    }

    /// <summary>Builds the configured unsigned bundle.</summary>
    public BundleResult Build(string outputPath, BundleOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        if (_packagePaths.Count == 0)
        {
            throw new InvalidOperationException("At least one child package is required to build a bundle.");
        }

        string outputFullPath = Path.GetFullPath(outputPath);
        ValidateBundleExtension(outputFullPath);
        BundleOptions effectiveOptions = options ?? new BundleOptions();
        ValidateVersion(effectiveOptions.Version);

        List<BundleInput> inputs = ReadInputs(_packagePaths);
        ValidateInputs(inputs);
        Version bundleVersion = effectiveOptions.Version
            ?? inputs.Max(static input => input.Identity.Version)!;
        var bundleIdentity = new PackageIdentity
        {
            Name = inputs[0].Identity.Name,
            Publisher = inputs[0].Identity.Publisher,
            Version = bundleVersion,
            Architecture = ProcessorArchitecture.Neutral,
        };

        string? outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new IOException($"The output path '{outputFullPath}' has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        if (File.Exists(outputFullPath) && !effectiveOptions.Overwrite)
        {
            throw new IOException(
                $"The output file '{outputFullPath}' already exists. Use overwrite to replace it.");
        }

        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputFullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteBundle(temporaryPath, inputs, bundleIdentity);
            BundleManifest manifest;
            using (MsixBundle bundle = MsixBundle.Open(temporaryPath))
            {
                manifest = bundle.Manifest;
            }

            File.Move(temporaryPath, outputFullPath, effectiveOptions.Overwrite);
            return new BundleResult
            {
                OutputPath = outputFullPath,
                Identity = manifest.Identity,
                Packages = manifest.Packages,
            };
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static List<BundleInput> ReadInputs(IEnumerable<string> paths)
    {
        var result = new List<BundleInput>();
        foreach (string path in paths)
        {
            using MsixPackage package = MsixPackage.Open(path);
            AppxManifest manifest = package.Manifest;
            result.Add(new BundleInput(
                path,
                Path.GetFileName(path),
                manifest.Identity,
                manifest.IsResourcePackage,
                manifest.Resources,
                manifest.TargetDeviceFamilies));
        }

        return result;
    }

    private static void ValidateInputs(IReadOnlyList<BundleInput> inputs)
    {
        BundleInput first = inputs[0];
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applicationArchitectures = new HashSet<ProcessorArchitecture>();

        foreach (BundleInput input in inputs)
        {
            if (!string.Equals(input.Identity.Name, first.Identity.Name, StringComparison.Ordinal)
                || !string.Equals(input.Identity.Publisher, first.Identity.Publisher, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "All child packages in a bundle must have the same Name and Publisher.");
            }

            if (!input.Identity.Version.Equals(first.Identity.Version))
            {
                throw new InvalidDataException(
                    "All child packages in a bundle must have the same Version. "
                    + $"'{input.FileName}' declares version '{input.Identity.Version}' but "
                    + $"'{first.FileName}' declares '{first.Identity.Version}'.");
            }

            if (!identities.Add(input.Identity.PackageFullName))
            {
                throw new InvalidDataException(
                    $"The child package identity '{input.Identity.PackageFullName}' is duplicated.");
            }

            if (!fileNames.Add(input.FileName))
            {
                throw new InvalidDataException(
                    $"The child package file name '{input.FileName}' is duplicated.");
            }

            if (input.IsResourcePackage)
            {
                if (string.IsNullOrEmpty(input.Identity.ResourceId))
                {
                    throw new InvalidDataException(
                        $"Resource package '{input.FileName}' must declare a ResourceId.");
                }
            }
            else if ((input.Identity.Architecture == ProcessorArchitecture.Neutral
                    && applicationArchitectures.Count > 0)
                || (input.Identity.Architecture != ProcessorArchitecture.Neutral
                    && applicationArchitectures.Contains(ProcessorArchitecture.Neutral)))
            {
                throw new InvalidDataException(
                    "An architecture-neutral application package cannot be combined with "
                    + "architecture-specific application packages.");
            }
            else if (!applicationArchitectures.Add(input.Identity.Architecture))
            {
                throw new InvalidDataException(
                    $"The bundle contains more than one application package for architecture "
                    + $"'{PackageIdentity.ArchitectureMoniker(input.Identity.Architecture)}'.");
            }
        }
    }

    private static void WriteBundle(
        string path,
        IReadOnlyList<BundleInput> inputs,
        PackageIdentity bundleIdentity)
    {
        using FileStream output = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new StoredZipWriter(output);
        var packages = new List<BundlePackageEntry>(inputs.Count);

        foreach (BundleInput input in inputs)
        {
            using Stream source = File.OpenRead(input.Path);
            StoredZipEntryInfo entry = archive.AddEntry(
                OpcPartNameEncoder.Encode(input.FileName),
                source.CopyTo);
            packages.Add(new BundlePackageEntry
            {
                FileName = input.FileName,
                Type = input.IsResourcePackage
                    ? BundlePackageType.Resource
                    : BundlePackageType.Application,
                Version = input.Identity.Version,
                Architecture = input.Identity.Architecture,
                ResourceId = input.Identity.ResourceId,
                Resources = input.Resources,
                Offset = entry.ContentOffset,
                Size = entry.UncompressedSize,
                TargetDeviceFamilies = input.TargetDeviceFamilies,
            });
        }

        byte[] manifest = BundleManifestWriter.Write(bundleIdentity, packages);
        BlockMapFile? manifestBlockMap = null;
        StoredZipEntryInfo manifestEntry = archive.AddDeflatedEntry(
            OpcPartNames.AppxBundleManifest,
            destination =>
            {
                CompressedBlockMapFile compressed = BlockMapWriter.CompressAndHash(
                    OpcPartNames.AppxBundleManifest,
                    new MemoryStream(manifest, writable: false),
                    destination,
                    CompressionLevel.Optimal);
                manifestBlockMap = compressed.File;
                return new DeflatedZipEntryContent(
                    compressed.Crc32,
                    compressed.CompressedSize,
                    compressed.UncompressedSize);
            });

        WriteGeneratedDeflated(
            archive,
            OpcPartNames.AppxBlockMap,
            BundleBlockMapWriter.Write(
                new AuthoredBlockMapFile(manifestBlockMap!, manifestEntry.LocalHeaderSize)));
        WriteGeneratedDeflated(
            archive,
            OpcPartNames.ContentTypes,
            ContentTypesWriter.WriteBundle(inputs.Select(static input => input.FileName)));
    }

    private static void WriteGeneratedDeflated(StoredZipWriter archive, string name, byte[] content)
    {
        archive.AddDeflatedEntry(
            name,
            destination =>
            {
                CompressedBlockMapFile compressed = BlockMapWriter.CompressAndHash(
                    name,
                    new MemoryStream(content, writable: false),
                    destination,
                    CompressionLevel.Optimal);
                return new DeflatedZipEntryContent(
                    compressed.Crc32,
                    compressed.CompressedSize,
                    compressed.UncompressedSize);
            });
    }

    private static void ValidatePackageExtension(string path)
    {
        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".msix", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".appx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Child package '{path}' must have a .msix or .appx extension.",
                nameof(path));
        }
    }

    private static void ValidateBundleExtension(string path)
    {
        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".msixbundle", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".appxbundle", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Bundle output '{path}' must have a .msixbundle or .appxbundle extension.",
                nameof(path));
        }
    }

    private static void ValidateVersion(Version? version)
    {
        if (version is null)
        {
            return;
        }

        if (version.Major is < 0 or > ushort.MaxValue
            || version.Minor is < 0 or > ushort.MaxValue
            || version.Build is < 0 or > ushort.MaxValue
            || version.Revision is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "The bundle version must have four components, each from 0 through 65535.");
        }
    }

    private sealed record BundleInput(
        string Path,
        string FileName,
        PackageIdentity Identity,
        bool IsResourcePackage,
        IReadOnlyList<BundleResource> Resources,
        IReadOnlyList<TargetDeviceFamily> TargetDeviceFamilies);
}
