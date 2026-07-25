using System.Buffers;
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

        // Rent a single block buffer and reuse it across every verified file. The pool may return an
        // array larger than BlockMap.BlockSize, so all reads are capped at exactly BlockMap.BlockSize.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockMap.BlockSize);
        try
        {
            foreach (BlockMapFile file in blockMap.Files)
            {
                mappedNames.Add(file.Name);
                BlockMapFileResult result = VerifyFile(package, blockMap.HashMethod, file, buffer);
                fileResults.Add(result);
                allValid &= result.IsValid;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

    /// <summary>
    /// Verifies one block-mapped file while copying its uncompressed bytes to another stream.
    /// </summary>
    public static BlockMapFileResult VerifyAndCopy(
        Stream content,
        Stream destination,
        BlockMapHashMethod hashMethod,
        BlockMapFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(file);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BlockMap.BlockSize);
        try
        {
            return VerifyAndCopyCore(content, destination, hashMethod, file, buffer, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static BlockMapFileResult VerifyAndCopyCore(
        Stream content,
        Stream destination,
        BlockMapHashMethod hashMethod,
        BlockMapFile file,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        HashAlgorithmName algorithm = ToAlgorithmName(hashMethod);
        long totalRead = 0;
        int blockIndex = 0;
        Span<byte> actual = stackalloc byte[64];
        Span<byte> expected = stackalloc byte[64];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int filled = ReadBlock(content, buffer, cancellationToken);
            if (filled == 0)
            {
                break;
            }

            if (blockIndex >= file.Blocks.Count)
            {
                return Invalid(file.Name, "the file contains more data than the block map declares.");
            }

            // Hash into a stack buffer (max digest is SHA-512 = 64 bytes) and compare the raw digest
            // bytes against the Base64-decoded expected hash. This avoids allocating a digest array and
            // a Base64 string per block. Malformed expected Base64 is treated as a mismatch, never an
            // exception, preserving the previous ordinal-string-compare semantics.
            int actualLength = CryptographicOperations.HashData(algorithm, buffer.AsSpan(0, filled), actual);

            if (!Convert.TryFromBase64String(file.Blocks[blockIndex].Hash, expected, out int expectedLength)
                || !actual[..actualLength].SequenceEqual(expected[..expectedLength]))
            {
                return Invalid(file.Name, $"block {blockIndex} hash mismatch.");
            }

            destination.Write(buffer, 0, filled);
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

    /// <summary>Checks that the block map and package contain the same payload files.</summary>
    public static IReadOnlyList<string> VerifyCoverage(IOpcPackage package, BlockMap blockMap)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(blockMap);
        return CheckCoverage(
            package,
            new HashSet<string>(blockMap.Files.Select(static file => file.Name), StringComparer.OrdinalIgnoreCase));
    }

    private static BlockMapFileResult VerifyFile(IOpcPackage package, BlockMapHashMethod hashMethod, BlockMapFile file, byte[] buffer)
    {
        if (!package.ContainsPart(file.Name))
        {
            return Invalid(file.Name, "the file is listed in the block map but missing from the package.");
        }

        try
        {
            using Stream content = package.OpenPart(file.Name);
            return VerifyContent(content, hashMethod, file, buffer);
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

    private static BlockMapFileResult VerifyContent(Stream content, BlockMapHashMethod hashMethod, BlockMapFile file, byte[] buffer)
    {
        return VerifyAndCopyCore(content, Stream.Null, hashMethod, file, buffer, CancellationToken.None);
    }

    private static int ReadBlock(Stream stream, byte[] buffer, CancellationToken cancellationToken = default)
    {
        // Cap at exactly BlockMap.BlockSize even when the pool returns a larger array; a block must
        // never exceed 64 KiB or its hash would not match the block map.
        int limit = Math.Min(buffer.Length, BlockMap.BlockSize);
        int filled = 0;
        while (filled < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, filled, limit - filled);
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
