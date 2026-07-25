using System.Security.Cryptography;
using MsixCore.Packaging.Opc;

namespace MsixCore.Packaging.Integrity;

/// <summary>
/// Verifies that the contents of an OPC package match its <c>AppxBlockMap.xml</c>: every block-mapped
/// file's uncompressed content hashes to the declared per-block hashes and total size, and the set of
/// block-mapped files matches the package's payload parts.
/// </summary>
/// <remarks>
/// This is pure managed, cross-platform code (no Windows dependency) so it can gate MSIX packages in
/// Linux CI. It reads uncompressed content through <see cref="IOpcPackage"/> and hashes it with
/// <see cref="IncrementalHash"/>.
/// </remarks>
public static class BlockMapVerifier
{
    /// <summary>OPC parts that are never listed in the block map and are excluded from coverage checks.</summary>
    private static readonly HashSet<string> ExcludedParts = new(StringComparer.OrdinalIgnoreCase)
    {
        OpcPartNames.AppxBlockMap,
        OpcPartNames.AppxSignature,
        OpcPartNames.ContentTypes,
        OpcPartNames.CodeIntegrityCatalog,
    };

    /// <summary>Verifies a package's payload against a parsed block map.</summary>
    /// <param name="package">The OPC package to read content from.</param>
    /// <param name="blockMap">The parsed block map to verify against.</param>
    /// <returns>A result describing per-file and coverage outcomes.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static BlockMapVerificationResult Verify(IOpcPackage package, BlockMap blockMap)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(blockMap);

        var fileResults = new List<BlockMapFileResult>(blockMap.Files.Count);
        var mappedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool allValid = true;

        foreach (BlockMapFile file in blockMap.Files)
        {
            mappedNames.Add(file.Name);
            BlockMapFileResult result = VerifyFile(package, blockMap.HashMethod, file);
            fileResults.Add(result);
            allValid &= result.IsValid;
        }

        List<string> coverageErrors = CheckCoverage(package, mappedNames);
        allValid &= coverageErrors.Count == 0;

        return new BlockMapVerificationResult
        {
            IsValid = allValid,
            Files = fileResults,
            CoverageErrors = coverageErrors,
        };
    }

    private static BlockMapFileResult VerifyFile(IOpcPackage package, BlockMapHashMethod hashMethod, BlockMapFile file)
    {
        if (!package.ContainsPart(file.Name))
        {
            return Invalid(file.Name, "the file is listed in the block map but missing from the package.");
        }

        try
        {
            using Stream content = package.OpenPart(file.Name);
            return VerifyContent(content, hashMethod, file);
        }
        catch (IOException ex)
        {
            return Invalid(file.Name, $"the file could not be read: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return Invalid(file.Name, $"the file could not be decompressed: {ex.Message}");
        }
    }

    private static BlockMapFileResult VerifyContent(Stream content, BlockMapHashMethod hashMethod, BlockMapFile file)
    {
        HashAlgorithmName algorithm = ToAlgorithmName(hashMethod);
        byte[] buffer = new byte[BlockMap.BlockSize];
        long totalRead = 0;
        int blockIndex = 0;

        while (true)
        {
            int filled = ReadBlock(content, buffer);
            if (filled == 0)
            {
                break;
            }

            if (blockIndex >= file.Blocks.Count)
            {
                return Invalid(file.Name, "the file contains more data than the block map declares.");
            }

            byte[] hash = CryptographicOperations.HashData(algorithm, buffer.AsSpan(0, filled));
            string actual = Convert.ToBase64String(hash);
            if (!string.Equals(actual, file.Blocks[blockIndex].Hash, StringComparison.Ordinal))
            {
                return Invalid(file.Name, $"block {blockIndex} hash mismatch.");
            }

            totalRead += filled;
            blockIndex++;
        }

        if (blockIndex != file.Blocks.Count)
        {
            return Invalid(file.Name, $"the block map declares {file.Blocks.Count} block(s) but the file has {blockIndex}.");
        }

        if (totalRead != file.Size)
        {
            return Invalid(file.Name, $"size mismatch: block map declares {file.Size} bytes but the file has {totalRead}.");
        }

        return new BlockMapFileResult { Name = file.Name, IsValid = true };
    }

    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = stream.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static List<string> CheckCoverage(IOpcPackage package, HashSet<string> mappedNames)
    {
        var errors = new List<string>();
        var payloadParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string part in package.PartNames)
        {
            if (ExcludedParts.Contains(part))
            {
                continue;
            }

            payloadParts.Add(part);
            if (!mappedNames.Contains(part))
            {
                errors.Add($"Package part '{part}' is not covered by the block map.");
            }
        }

        foreach (string mapped in mappedNames)
        {
            if (!payloadParts.Contains(mapped))
            {
                errors.Add($"Block map file '{mapped}' is not present in the package.");
            }
        }

        return errors;
    }

    private static HashAlgorithmName ToAlgorithmName(BlockMapHashMethod hashMethod) => hashMethod switch
    {
        BlockMapHashMethod.Sha256 => HashAlgorithmName.SHA256,
        BlockMapHashMethod.Sha384 => HashAlgorithmName.SHA384,
        BlockMapHashMethod.Sha512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentOutOfRangeException(nameof(hashMethod), hashMethod, "Unsupported block map hash method."),
    };

    private static BlockMapFileResult Invalid(string name, string reason) =>
        new() { Name = name, IsValid = false, Error = $"File '{name}': {reason}" };
}
