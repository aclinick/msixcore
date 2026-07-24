using System.Xml.Linq;

namespace MsixCore.Packaging.Manifest;

/// <summary>
/// Helpers for reading MSIX manifests with <see cref="XElement"/> while tolerating the many schema
/// namespaces MSIX uses (foundation, <c>uap</c>, <c>uap2..uap17</c>, <c>desktop</c>, <c>rescap</c>,
/// <c>com</c>, ...). Elements are matched by local name so a single parser copes with any schema
/// revision.
/// </summary>
internal static class ManifestXml
{
    /// <summary>Returns the immediate child elements with the given local name, ignoring namespace.</summary>
    public static IEnumerable<XElement> ElementsByLocalName(this XElement element, string localName) =>
        element.Elements().Where(e => e.Name.LocalName == localName);

    /// <summary>Returns the first immediate child element with the given local name, or <see langword="null"/>.</summary>
    public static XElement? ElementByLocalName(this XElement element, string localName) =>
        element.ElementsByLocalName(localName).FirstOrDefault();

    /// <summary>Returns all descendant elements with the given local name, ignoring namespace.</summary>
    public static IEnumerable<XElement> DescendantsByLocalName(this XElement element, string localName) =>
        element.Descendants().Where(e => e.Name.LocalName == localName);

    /// <summary>
    /// Returns the value of the attribute with the given local name (namespace-insensitive), or
    /// <see langword="null"/> if absent.
    /// </summary>
    public static string? AttributeValue(this XElement element, string localName)
    {
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.Name.LocalName == localName)
            {
                return attribute.Value;
            }
        }

        return null;
    }
}
