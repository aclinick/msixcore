namespace MsixCore.Packaging.Opc;

/// <summary>Well-known OPC part names defined by the MSIX/APPX package format.</summary>
public static class OpcPartNames
{
    /// <summary>The application package manifest.</summary>
    public const string AppxManifest = "AppxManifest.xml";

    /// <summary>The bundle manifest (present in <c>.msixbundle</c>/<c>.appxbundle</c> packages).</summary>
    public const string AppxBundleManifest = "AppxMetadata/AppxBundleManifest.xml";

    /// <summary>The block map describing per-file block hashes.</summary>
    public const string AppxBlockMap = "AppxBlockMap.xml";

    /// <summary>The AppxMetadata code-integrity catalog (a footprint part, never in the block map).</summary>
    public const string CodeIntegrityCatalog = "AppxMetadata/CodeIntegrity.cat";

    /// <summary>The digital signature part.</summary>
    public const string AppxSignature = "AppxSignature.p7x";

    /// <summary>The OPC content-types part.</summary>
    public const string ContentTypes = "[Content_Types].xml";
}
