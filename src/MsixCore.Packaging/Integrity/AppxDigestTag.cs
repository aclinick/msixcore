namespace MsixCore.Packaging.Integrity;

/// <summary>
/// The 4-byte tag values that identify entries in an APPX/MSIX digest table.
/// Each tag is a <see langword="uint"/> read little-endian from the digest table; the literal
/// bytes in the file spell ASCII mnemonics (e.g. <c>AXBM</c> = <c>0x4D425841</c>).
/// </summary>
public enum AppxDigestTag : uint
{
    /// <summary><c>AXPC</c> — ZIP file-record (local file header + data) digest.</summary>
    Axpc = 0x43505841, // bytes: 41 58 50 43

    /// <summary><c>AXCD</c> — ZIP central-directory digest.</summary>
    Axcd = 0x44435841, // bytes: 41 58 43 44

    /// <summary><c>AXCT</c> — <c>[Content_Types].xml</c> (uncompressed) digest.</summary>
    Axct = 0x54435841, // bytes: 41 58 43 54

    /// <summary><c>AXBM</c> — <c>AppxBlockMap.xml</c> (uncompressed) digest.</summary>
    Axbm = 0x4D425841, // bytes: 41 58 42 4D

    /// <summary><c>AXCI</c> — <c>AppxMetadata/CodeIntegrity.cat</c> (uncompressed, optional) digest.</summary>
    Axci = 0x49435841, // bytes: 41 58 43 49
}
