using MsixCore.Packaging.Opc;

namespace MsixCore.CorpusRoundtrip;

internal static class PackageFootprints
{
    private static readonly HashSet<string> Footprints = new(StringComparer.OrdinalIgnoreCase)
    {
        OpcPartNames.AppxSignature,
        OpcPartNames.AppxBlockMap,
        OpcPartNames.ContentTypes,
        OpcPartNames.CodeIntegrityCatalog,
    };

    public static bool IsFootprint(string partName) => Footprints.Contains(partName);
}
