using System.Globalization;
using System.Text;
using System.Xml;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Authoring;

internal static class BundleManifestWriter
{
    private const string BundleNamespace = "http://schemas.microsoft.com/appx/2013/bundle";
    private const string Bundle2018Namespace = "http://schemas.microsoft.com/appx/2018/bundle";
    private const string Bundle2019Namespace = "http://schemas.microsoft.com/appx/2019/bundle";

    public static byte[] Write(PackageIdentity identity, IReadOnlyList<BundlePackageEntry> packages)
    {
        using var output = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(output, CreateSettings()))
        {
            writer.WriteStartDocument(standalone: false);
            writer.WriteStartElement("Bundle", BundleNamespace);
            writer.WriteAttributeString("SchemaVersion", "5.0");
            writer.WriteAttributeString("xmlns", "b4", null, Bundle2018Namespace);
            writer.WriteAttributeString("xmlns", "b5", null, Bundle2019Namespace);
            writer.WriteAttributeString("IgnorableNamespaces", "b4 b5");

            writer.WriteStartElement("Identity", BundleNamespace);
            writer.WriteAttributeString("Name", identity.Name);
            writer.WriteAttributeString("Publisher", identity.Publisher);
            writer.WriteAttributeString("Version", FormatVersion(identity.Version));
            writer.WriteEndElement();

            writer.WriteStartElement("Packages", BundleNamespace);
            foreach (BundlePackageEntry package in packages)
            {
                WritePackage(writer, package);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToArray();
    }

    private static void WritePackage(XmlWriter writer, BundlePackageEntry package)
    {
        writer.WriteStartElement("Package", BundleNamespace);
        writer.WriteAttributeString(
            "Type",
            package.Type == BundlePackageType.Application ? "application" : "resource");
        writer.WriteAttributeString("Version", FormatVersion(package.Version));
        if (package.Type == BundlePackageType.Application)
        {
            writer.WriteAttributeString(
                "Architecture",
                PackageIdentity.ArchitectureMoniker(package.Architecture));
        }
        else
        {
            writer.WriteAttributeString("ResourceId", package.ResourceId);
        }

        writer.WriteAttributeString("FileName", package.FileName);
        writer.WriteAttributeString("Offset", package.Offset.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("Size", package.Size.ToString(CultureInfo.InvariantCulture));

        if (package.Resources.Count > 0)
        {
            writer.WriteStartElement("Resources", BundleNamespace);
            foreach (BundleResource resource in package.Resources)
            {
                writer.WriteStartElement("Resource", BundleNamespace);
                WriteOptionalAttribute(writer, "Language", resource.Language);
                WriteOptionalAttribute(writer, "Scale", resource.Scale);
                WriteOptionalAttribute(writer, "DXFeatureLevel", resource.DXFeatureLevel);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        if (package.TargetDeviceFamilies.Count > 0)
        {
            writer.WriteStartElement("b4", "Dependencies", Bundle2018Namespace);
            foreach (TargetDeviceFamily family in package.TargetDeviceFamilies)
            {
                writer.WriteStartElement("b4", "TargetDeviceFamily", Bundle2018Namespace);
                writer.WriteAttributeString("Name", family.Name);
                writer.WriteAttributeString("MinVersion", FormatVersion(family.MinVersion));
                writer.WriteAttributeString("MaxVersionTested", FormatVersion(family.MaxVersionTested));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteOptionalAttribute(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteAttributeString(name, value);
        }
    }

    private static string FormatVersion(Version version) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");

    private static XmlWriterSettings CreateSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
