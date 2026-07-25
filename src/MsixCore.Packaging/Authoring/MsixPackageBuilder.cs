using System.IO.Compression;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Authoring;

/// <summary>Builds unsigned MSIX packages from directory layouts or programmatically supplied files.</summary>
/// <remarks>
/// Bundle authoring is tracked by https://github.com/aclinick/msixcore/issues/36; optional integrated
/// signing is tracked by https://github.com/aclinick/msixcore/issues/37.
/// </remarks>
public sealed class MsixPackageBuilder
{
    private static readonly HashSet<string> InputFootprints = new(StringComparer.OrdinalIgnoreCase)
    {
        OpcPartNames.AppxBlockMap,
        OpcPartNames.AppxSignature,
        OpcPartNames.ContentTypes,
    };

    private static readonly HashSet<string> BlockMapFootprints = new(StringComparer.OrdinalIgnoreCase)
    {
        OpcPartNames.AppxBlockMap,
        OpcPartNames.AppxSignature,
        OpcPartNames.ContentTypes,
        OpcPartNames.CodeIntegrityCatalog,
    };

    private static readonly HashSet<string> MakeAppxStoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appx", ".avi", ".cab", ".gif", ".gz", ".jpeg", ".jpg",
        ".m4a", ".mov", ".mp3", ".png", ".rar", ".wmv", ".zip",
    };

    private readonly Dictionary<string, PackageInput> _payload = new(StringComparer.OrdinalIgnoreCase);
    private PackageInput? _manifest;

    /// <summary>
    /// Builds an unsigned MSIX package from a source directory.
    /// </summary>
    /// <param name="sourceDirectory">Directory whose root contains <c>AppxManifest.xml</c> and payload files.</param>
    /// <param name="outputPath">Destination <c>.msix</c> file.</param>
    /// <param name="options">Optional overwrite and compression settings.</param>
    /// <returns>Information read back from the completed package.</returns>
    /// <exception cref="DirectoryNotFoundException">The source directory does not exist.</exception>
    /// <exception cref="InvalidDataException">The source has no root manifest or contains invalid package paths.</exception>
    /// <exception cref="IOException">The output exists without overwrite permission or cannot be written.</exception>
    public static PackResult Build(string sourceDirectory, string outputPath, PackOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        string outputFullPath = Path.GetFullPath(outputPath);
        using DirectoryOpcPackage source = DirectoryOpcPackage.Open(sourceDirectory);
        if (!source.PartNames.Contains(OpcPartNames.AppxManifest, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"The source directory must contain '{OpcPartNames.AppxManifest}' at its root.");
        }

        var inputs = new List<PackageInput>();
        foreach (string partName in source.PartNames)
        {
            if (InputFootprints.Contains(partName))
            {
                continue;
            }

            string sourcePath = Path.GetFullPath(
                Path.Combine(source.RootDirectory, partName.Replace('/', Path.DirectorySeparatorChar)));
            if (string.Equals(sourcePath, outputFullPath, PathComparison))
            {
                continue;
            }

            inputs.Add(new PackageInput(partName, sourcePath, () => source.OpenPart(partName)));
        }

        return BuildCore(inputs, outputFullPath, options ?? new PackOptions());
    }

    /// <summary>
    /// Sets the package manifest from the stream's current position to its end.
    /// </summary>
    /// <remarks>
    /// The bytes are copied immediately. The caller retains ownership of <paramref name="manifestStream"/>
    /// and may dispose or reuse it after this method returns.
    /// </remarks>
    /// <param name="manifestStream">Readable stream containing <c>AppxManifest.xml</c>.</param>
    /// <returns>This builder.</returns>
    public MsixPackageBuilder SetManifest(Stream manifestStream)
    {
        _manifest = PackageInput.FromStream(OpcPartNames.AppxManifest, manifestStream);
        return this;
    }

    /// <summary>Adds a payload file from a filesystem path.</summary>
    /// <param name="packagePath">Package-relative logical path, using slash or backslash separators.</param>
    /// <param name="sourcePath">File whose content is read when <see cref="Build(string, PackOptions?)"/> runs.</param>
    /// <returns>This builder.</returns>
    public MsixPackageBuilder AddFile(string packagePath, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        string normalized = ValidatePayloadPath(packagePath);
        string fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The payload source file was not found.", fullPath);
        }

        AddInput(new PackageInput(normalized, fullPath, () => File.OpenRead(fullPath)));
        return this;
    }

    /// <summary>
    /// Adds a payload file from the stream's current position to its end.
    /// </summary>
    /// <remarks>
    /// The bytes are copied immediately. The caller retains ownership of <paramref name="content"/>
    /// and may dispose or reuse it after this method returns.
    /// </remarks>
    /// <param name="packagePath">Package-relative logical path, using slash or backslash separators.</param>
    /// <param name="content">Readable payload stream.</param>
    /// <returns>This builder.</returns>
    public MsixPackageBuilder AddFile(string packagePath, Stream content)
    {
        string normalized = ValidatePayloadPath(packagePath);
        AddInput(PackageInput.FromStream(normalized, content));
        return this;
    }

    /// <summary>Builds the programmatically configured unsigned MSIX package.</summary>
    /// <param name="outputPath">Destination <c>.msix</c> file.</param>
    /// <param name="options">Optional overwrite and compression settings.</param>
    /// <returns>Information read back from the completed package.</returns>
    public PackResult Build(string outputPath, PackOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        if (_manifest is null)
        {
            throw new InvalidOperationException(
                $"A manifest is required. Call {nameof(SetManifest)} before building the package.");
        }

        var inputs = new List<PackageInput>(_payload.Count + 1) { _manifest };
        inputs.AddRange(_payload.Values);
        return BuildCore(inputs, Path.GetFullPath(outputPath), options ?? new PackOptions());
    }

    private static PackResult BuildCore(
        IReadOnlyCollection<PackageInput> inputs,
        string outputPath,
        PackOptions options)
    {
        if (options.CompressionLevel is not CompressionLevel.NoCompression and not CompressionLevel.Optimal)
        {
            throw new NotSupportedException(
                "MSIX authoring supports CompressionLevel.NoCompression or CompressionLevel.Optimal.");
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new IOException($"The output path '{outputPath}' has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        if (File.Exists(outputPath) && !options.Overwrite)
        {
            throw new IOException($"The output file '{outputPath}' already exists. Use overwrite to replace it.");
        }

        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            List<AuthoredBlockMapFile> files = WritePackage(
                temporaryPath,
                inputs,
                options.CompressionLevel);
            PackageIdentity identity;
            using (MsixPackage package = MsixPackage.Open(temporaryPath))
            {
                identity = package.Identity;
                BlockMapVerificationResult verification = package.VerifyBlockMap();
                if (!verification.IsValid)
                {
                    throw new InvalidDataException("The authored package failed its generated block-map verification.");
                }
            }

            File.Move(temporaryPath, outputPath, options.Overwrite);
            return new PackResult
            {
                OutputPath = outputPath,
                Identity = identity,
                FileCount = files.Count,
                TotalSize = files.Sum(static file => file.File.Size),
                CompressionLevel = options.CompressionLevel,
            };
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static List<AuthoredBlockMapFile> WritePackage(
        string path,
        IReadOnlyCollection<PackageInput> inputs,
        CompressionLevel compressionLevel)
    {
        PackageInput[] orderedInputs = inputs
            .OrderBy(static input => input.SortKey, StringComparer.Ordinal)
            .ToArray();
        var blockMapFiles = new List<AuthoredBlockMapFile>(orderedInputs.Length);

        using (FileStream output = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new StoredZipWriter(output))
        {
            foreach (PackageInput input in orderedInputs)
            {
                using Stream source = input.Open();
                BlockMapFile? file = null;
                StoredZipEntryInfo entry;
                if (ShouldCompress(input.PartName, compressionLevel))
                {
                    entry = archive.AddDeflatedEntry(
                        OpcPartNameEncoder.Encode(input.PartName),
                        destination =>
                        {
                            CompressedBlockMapFile compressed = BlockMapWriter.CompressAndHash(
                                input.PartName,
                                source,
                                destination,
                                compressionLevel);
                            file = compressed.File;
                            return new DeflatedZipEntryContent(
                                compressed.Crc32,
                                compressed.CompressedSize,
                                compressed.UncompressedSize);
                        });
                }
                else
                {
                    entry = archive.AddEntry(
                        OpcPartNameEncoder.Encode(input.PartName),
                        destination => file = BlockMapWriter.CopyAndHash(input.PartName, source, destination));
                }

                if (!BlockMapFootprints.Contains(input.PartName))
                {
                    blockMapFiles.Add(new AuthoredBlockMapFile(file!, entry.LocalHeaderSize));
                }
            }

            WriteGeneratedEntry(
                archive,
                OpcPartNames.ContentTypes,
                ContentTypesWriter.Write(orderedInputs.Select(static input => input.PartName)),
                compressionLevel);
            WriteGeneratedEntry(
                archive,
                OpcPartNames.AppxBlockMap,
                BlockMapWriter.Write(blockMapFiles),
                compressionLevel);
        }

        return blockMapFiles;
    }

    private static void WriteGeneratedEntry(
        StoredZipWriter archive,
        string name,
        byte[] content,
        CompressionLevel compressionLevel)
    {
        if (compressionLevel == CompressionLevel.NoCompression)
        {
            archive.AddEntry(name, destination => destination.Write(content));
            return;
        }

        archive.AddDeflatedEntry(
            name,
            destination =>
            {
                CompressedBlockMapFile compressed = BlockMapWriter.CompressAndHash(
                    name,
                    new MemoryStream(content, writable: false),
                    destination,
                    compressionLevel);
                return new DeflatedZipEntryContent(
                    compressed.Crc32,
                    compressed.CompressedSize,
                    compressed.UncompressedSize);
            });
    }

    private static bool ShouldCompress(string partName, CompressionLevel compressionLevel) =>
        compressionLevel != CompressionLevel.NoCompression
        && !MakeAppxStoredExtensions.Contains(Path.GetExtension(partName));

    private static string ValidatePayloadPath(string packagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        if (packagePath.StartsWith('/') || packagePath.StartsWith('\\') || HasDriveDesignator(packagePath))
        {
            throw new ArgumentException(
                $"'{packagePath}' must be a package-relative path, not a rooted or drive-qualified path.",
                nameof(packagePath));
        }

        string normalized = packagePath.Replace('\\', '/');
        if (!OpcPackage.IsValidPartName(normalized))
        {
            throw new ArgumentException($"'{packagePath}' is not a valid package-relative path.", nameof(packagePath));
        }

        if (string.Equals(normalized, OpcPartNames.AppxManifest, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Use {nameof(SetManifest)} to provide '{OpcPartNames.AppxManifest}'.",
                nameof(packagePath));
        }

        if (InputFootprints.Contains(normalized))
        {
            throw new ArgumentException(
                $"The footprint file '{normalized}' is generated by the package builder.",
                nameof(packagePath));
        }

        return normalized;
    }

    private static bool HasDriveDesignator(string path) =>
        path.Length >= 2
        && ((path[0] is >= 'A' and <= 'Z') || (path[0] is >= 'a' and <= 'z'))
        && path[1] == ':';

    private void AddInput(PackageInput input)
    {
        if (!_payload.TryAdd(input.PartName, input))
        {
            throw new ArgumentException($"A payload file named '{input.PartName}' has already been added.");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record PackageInput(string PartName, string SortKey, Func<Stream> Open)
    {
        public static PackageInput FromStream(string partName, Stream content)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (!content.CanRead)
            {
                throw new ArgumentException("The content stream must be readable.", nameof(content));
            }

            using var copy = new MemoryStream();
            content.CopyTo(copy);
            byte[] bytes = copy.ToArray();
            return new PackageInput(
                partName,
                partName,
                () => new MemoryStream(bytes, writable: false));
        }
    }
}
