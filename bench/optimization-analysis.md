# MSIX Core (.NET) performance analysis

Analysis-only snapshot measured July 25, 2026 on Windows 11, Snapdragon X X1P64100 Arm64,
.NET SDK 10.0.300 / .NET 10.0.8, using BenchmarkDotNet 0.15.4 with `MemoryDiagnoser`.
No product code or package output was changed.

## Baseline

Command:

```powershell
dotnet run -c Release --project bench\MsixCore.Benchmarks -- --filter *
```

The suite completed without broken or `NA` benchmarks. It used three warmups and five measured
iterations. The runtime reported Arm SHA-256 hardware intrinsics enabled.

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Extract package payload (small) | 4,728.62 us | 200.66 KB |
| Extract package payload (large) | 61,387.32 us | 514.53 KB |
| Open + parse manifest/identity (small, stream) | 25.05 us | 41.57 KB |
| Open + parse manifest/identity (large, file) | 376.66 us | 120.30 KB |
| Verify block map (small) | 167.34 us | 427.09 KB |
| Verify block map (large, multi-file/multi-block) | 13,440.06 us | 4,537.42 KB |
| Read signature (CMS envelope) | 167.04 us | 27.78 KB |
| Open loose directory + verify block map (large) | 17,490.35 us | 4,484.53 KB |

The large package is approximately 10 MB across 64 synthetic payload files. Large extraction is
disk-bound and had higher variance (61.39 ms mean, 2.08 ms standard deviation after one outlier was
removed). Verification was much more stable (13.44 ms mean, 91.96 us standard deviation), making it
the strongest optimization signal.

## Hot-path inspection

### Block-map verification

`src/MsixCore.Packaging/Integrity/BlockMapVerifier.cs:75` allocates a new 64 KiB array for every
verified file. The benchmark verifies 66 block-mapped files (64 generated files plus
`AppxManifest.xml` and `Assets/StoreLogo.png`) and allocates 4,537.42 KB; 4,224 KiB of that is
explained by the 66 payload buffers alone, approximately 93%. The loop correctly fills a complete
block across short reads at lines 155-170 and hashes only the populated span at line 93.

Each block also allocates a digest array and a Base64 string at lines 93-95 solely to compare with
the block-map string. The existing one-shot `CryptographicOperations.HashData` path is preferable to
`IncrementalHash` for independent 64 KiB blocks and is already hardware accelerated on this host.

### Extraction

`src/MsixCore.PackageStore/PackageExtractor.cs:164-178` already uses an 80 KiB `ArrayPool` buffer.
Managed allocation therefore stays nearly flat from the small to large package (200.66 KB to
514.53 KB). `ExtractAndVerify` also performs hashing while copying the same read at lines 120-147,
so there is no redundant payload read in that path.

The remaining per-file work includes path canonicalization, walking every destination segment for
reparse points, repeated `Directory.CreateDirectory`, and `File.Create` at lines 53-71 and
194-228. These checks are security-sensitive. The current OPC abstraction does not expose a
reliable uncompressed length, so destination preallocation cannot be added cleanly.

### Pack / authoring

`src/MsixCore.Packaging/Authoring/BlockMapWriter.cs:20-35` allocates one 64 KiB buffer per source
file and a 32-byte digest array per block while already combining copy, SHA-256, and block-map
generation in one source pass. There is no separate hash pass before ZIP writing.

`src/MsixCore.Packaging/Authoring/StoredZipWriter.cs:220-225` computes CRC-32 with a scalar
byte-at-a-time managed loop. Every authored payload byte passes through both this loop and SHA-256,
making CRC a credible CPU opportunity that requires a dedicated authoring/CRC benchmark before a
change.

`src/MsixCore.Packaging/Authoring/MsixPackageBuilder.cs:175-184` reopens the completed package and
performs a full block-map verification before publishing it. This deliberately reads and hashes all
payload bytes a second time as a correctness check.

The existing BenchmarkDotNet suite does not contain a pack benchmark. A later implementation pass
should add one before changing authoring so CRC, hashing, output I/O, and post-write verification can
be separated and measured.

### Manifest / OPC open

Open/parse is already cheap: 25.05 us for the small stream and 376.66 us for the large file.
`AppxManifestParser` and `BlockMapParser` materialize `XDocument`
(`src/MsixCore.Packaging/Manifest/AppxManifestParser.cs:35-36` and
`src/MsixCore.Packaging/Integrity/BlockMapParser.cs:37-38`). ZIP entry canonicalization also splits,
URI-decodes, and rejoins every path (`src/MsixCore.Packaging/Opc/OpcPackage.cs:148-174`).
Those allocations are measurable but not important enough to justify parser rewrites now.

## Ranked opportunities

| Rank | Hypothesis and evidence | Estimated impact | Risk | Effort | Disposition |
|---:|---|---|---|---|---|
| 1 | Reuse a single pooled 64 KiB block buffer across complete verification. The 66 per-file arrays account for 4,224 KiB, approximately 93% of the 4,537.42 KB large-verification allocation. | About 93% lower verification allocation; CPU neutral to modestly better through fewer GCs. | Low | Small | Ready to implement |
| 2 | Reuse one pooled block buffer across the complete authoring pass and hash into a stack destination span. `BlockMapWriter` currently allocates per file and per block. As a correctness precondition, `ReadBlock` and the hashed/written span must be capped at exactly `BlockMap.BlockSize`, regardless of a rented array's potentially larger length. | Save roughly 64 KiB per authored file plus transient digest arrays; likely fewer pack GCs. | Low | Small | Ready after adding a pack benchmark and enforcing the 64 KiB cap |
| 3 | Compare decoded expected hashes with stack-span digest bytes rather than allocating an actual Base64 string per block. Verification still needs result/model allocations after buffer pooling. | Tens of KB per 10 MB package; negligible-to-small CPU benefit. | Low-medium because malformed Base64 and SHA-384/512 behavior must remain identical | Small | Ready only with focused tests/benchmark |
| 4 | Accelerate the scalar CRC-32 loop using a proven runtime implementation or a wider managed algorithm. Every authored byte uses the byte-at-a-time table loop. | Potentially material pack CPU reduction; magnitude unknown until isolated. | Medium | Medium | Needs issue/benchmark |
| 5 | Replace the builder's post-write full reread with equivalent in-memory validation or make deep validation configurable. It currently adds another complete hash/read pass. | Approximately one verification pass plus package reopen; around 13 ms per 10 MB on this host as an upper-order estimate. | Medium-high: removes a strong correctness invariant | Medium | Needs issue/design |
| 6 | Cache validated extraction parent directories and evaluate destination `FileStreamOptions`/buffering. Many files repeat directory and reparse-point work. | Moderate for many-small-file packages; uncertain for large files. | High because link-defense caching changes the race/security model | Medium | Needs issue/security design |
| 7 | Replace `XDocument` parsing and per-entry path splitting with streaming/span-oriented parsing. Open allocates 42-120 KB but takes only 25-377 us. | Low end-to-end value. | Medium | Large | Defer |
| 8 | Parallel hashing, memory-mapped I/O, or custom SIMD/SHA. The runtime already exposes Arm SHA-256 acceleration and package streams are not thread-safe. | Workload-dependent; could regress memory and determinism. | High | Large | Defer |

## Ready to implement (low-risk wins)

1. **Verification buffer pooling:** rent once in `BlockMapVerifier.Verify`, pass the buffer through
   the private file/content helpers, cap reads at exactly `BlockMap.BlockSize` even if the pool
   returns a larger array, and return it in `finally`. Preserve the standalone `VerifyAndCopy`
   contract with its own pooled rental.
2. **Authoring buffer pooling and span hashing:** first add a large stored-package authoring
   benchmark. Then rent once in `MsixPackageBuilder.WritePackage`, pass the buffer to
   `BlockMapWriter`, and use the span overload of `SHA256.HashData`. As a required correctness
   precondition, change `ReadBlock` to read, write, and hash at most `BlockMap.BlockSize` bytes rather
   than `buffer.Length`; an oversized pooled array must never create an oversized MSIX block.
   Require byte-identical package, block-hash differential, makeappx unpack, and full round-trip tests.
3. **Binary digest comparison:** add malformed-hash and SHA-256/384/512 tests before replacing
   transient digest/Base64 allocations. Keep error text and failure ordering unchanged.

Each change should be benchmarked independently. Any mean-time regression should be reverted even
if allocation falls.

## Deferred / needs issue (architectural)

1. **CRC-32 authoring optimization:** add isolated 64 KiB and 10 MB CRC benchmarks; compare the
   existing loop with `System.IO.Hashing.Crc32` and a dependency-free wider implementation. Require
   identical ZIP CRC fields and byte-identical output.
2. **Avoid authoring's post-write full verification pass:** design an equivalent consistency check,
   retain a deep-validation option, and add injected short-write/corruption tests before removing
   the reread.
3. **Many-file extraction optimization:** benchmark directory caching and file-stream options
   separately, but first document and preserve the reparse-point race/security guarantees.
4. **Parallelism, memory mapping, custom hashing intrinsics, or XML-stack replacement:** treat these
   as separate architectural investigations, not opportunistic hot-path edits.
