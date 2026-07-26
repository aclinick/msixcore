using System.Xml;
using System.Xml.Linq;

namespace MsixCore.Packaging.Manifest;

public static partial class AppxManifestParser
{
    /// <summary>
    /// Parses an <c>&lt;Extensions&gt;</c> container, which is optional on both an
    /// <c>&lt;Application&gt;</c> and the <c>&lt;Package&gt;</c> root.
    /// </summary>
    /// <remarks>
    /// Extension elements are matched by local name, so the many namespace variants of the same
    /// element (<c>uap3:AppExecutionAlias</c> vs <c>uap5:AppExecutionAlias</c>,
    /// <c>desktop:StartupTask</c> vs <c>uap5:StartupTask</c>) collapse onto one model, consistent
    /// with the rest of this parser.
    /// </remarks>
    private static List<AppExtension> ParseExtensions(XElement? owner)
    {
        XElement? extensions = owner?.ElementByLocalName("Extensions");
        if (extensions is null)
        {
            return [];
        }

        var result = new List<AppExtension>();
        foreach (XElement element in extensions.ElementsByLocalName("Extension"))
        {
            string category = NullIfEmpty(element.AttributeValue("Category")?.Trim())
                ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                    "An 'Extension' element is missing the required 'Category' attribute.");

            result.Add(new AppExtension
            {
                Category = category,
                Executable = NullIfEmpty(element.AttributeValue("Executable")),
                EntryPoint = NullIfEmpty(element.AttributeValue("EntryPoint")),
                StartPage = NullIfEmpty(element.AttributeValue("StartPage")),
                ResourceGroup = NullIfEmpty(element.AttributeValue("ResourceGroup")),
                RuntimeType = NullIfEmpty(element.AttributeValue("RuntimeType")),
                Payload = ParseExtensionPayload(element, category),
            });
        }

        return result;
    }

    /// <summary>
    /// Parses the child element of a recognised category.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> both for an unrecognised category and for a recognised
    /// category whose child element is absent. Neither is treated as an error: the schema declares
    /// the child choice as <c>minOccurs="0"</c> (a bare
    /// <c>&lt;desktop:Extension Category="windows.fullTrustProcess" Executable="app.exe" /&gt;</c>
    /// is both valid and common), and rejecting categories from schema revisions newer than this
    /// library would make it fail on packages Windows accepts.
    /// </remarks>
    private static ExtensionPayload? ParseExtensionPayload(XElement extension, string category) =>
        category switch
        {
            "windows.fileTypeAssociation" =>
                ParseFileTypeAssociation(extension.ElementByLocalName("FileTypeAssociation")),
            "windows.protocol" =>
                ParseProtocol(extension.ElementByLocalName("Protocol")),
            "windows.appExecutionAlias" =>
                ParseAppExecutionAlias(extension.ElementByLocalName("AppExecutionAlias")),
            "windows.startupTask" =>
                ParseStartupTask(extension.ElementByLocalName("StartupTask")),
            "windows.fullTrustProcess" =>
                ParseFullTrustProcess(extension.ElementByLocalName("FullTrustProcess")),
            "windows.comServer" =>
                ParseComServer(extension.ElementByLocalName("ComServer")),
            "windows.shortcut" =>
                ParseShortcut(extension.ElementByLocalName("Shortcut")),
            _ => null,
        };

    private static FileTypeAssociationExtension? ParseFileTypeAssociation(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new FileTypeAssociationExtension
        {
            Name = RequiredAttribute(element, "Name"),
            DisplayName = NullIfEmpty(element.ElementByLocalName("DisplayName")?.Value.Trim()),
            Logo = NullIfEmpty(element.ElementByLocalName("Logo")?.Value.Trim()),
            InfoTip = NullIfEmpty(element.ElementByLocalName("InfoTip")?.Value.Trim()),
            FileTypes = ParseSupportedFileTypes(element.ElementByLocalName("SupportedFileTypes")),
        };
    }

    private static List<SupportedFileType> ParseSupportedFileTypes(XElement? element)
    {
        if (element is null)
        {
            return [];
        }

        var result = new List<SupportedFileType>();
        foreach (XElement fileType in element.ElementsByLocalName("FileType"))
        {
            string value = NullIfEmpty(fileType.Value.Trim())
                ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                    "A 'FileType' element is empty; a file extension is required.");

            result.Add(new SupportedFileType
            {
                Extension = value,
                ContentType = NullIfEmpty(fileType.AttributeValue("ContentType")?.Trim()),
            });
        }

        return result;
    }

    private static ProtocolExtension? ParseProtocol(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new ProtocolExtension
        {
            Name = RequiredAttribute(element, "Name"),
            DisplayName = NullIfEmpty(element.ElementByLocalName("DisplayName")?.Value.Trim()),
            Logo = NullIfEmpty(element.ElementByLocalName("Logo")?.Value.Trim()),
            DesiredView = NullIfEmpty(element.AttributeValue("DesiredView")?.Trim()),
            ReturnResults = NullIfEmpty(element.AttributeValue("ReturnResults")?.Trim()),
            Parameters = NullIfEmpty(element.AttributeValue("Parameters")),
        };
    }

    private static AppExecutionAliasExtension? ParseAppExecutionAlias(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var aliases = new List<string>();
        foreach (XElement alias in element.ElementsByLocalName("ExecutionAlias"))
        {
            aliases.Add(RequiredAttribute(alias, "Alias"));
        }

        return new AppExecutionAliasExtension { Aliases = aliases };
    }

    private static StartupTaskExtension? ParseStartupTask(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new StartupTaskExtension
        {
            TaskId = RequiredAttribute(element, "TaskId"),
            IsEnabled = ParseOptionalBoolean(element, "Enabled"),
            DisplayName = NullIfEmpty(element.AttributeValue("DisplayName")?.Trim()),
        };
    }

    private static FullTrustProcessExtension? ParseFullTrustProcess(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var groups = new List<ParameterGroup>();
        foreach (XElement group in element.ElementsByLocalName("ParameterGroup"))
        {
            groups.Add(new ParameterGroup
            {
                GroupId = RequiredAttribute(group, "GroupId"),
                Parameters = group.AttributeValue("Parameters")
                    ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                        "A 'ParameterGroup' element is missing the required 'Parameters' attribute."),
            });
        }

        return new FullTrustProcessExtension { ParameterGroups = groups };
    }

    private static ComServerExtension? ParseComServer(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var exeServers = new List<ComExeServer>();
        foreach (XElement server in element.ElementsByLocalName("ExeServer"))
        {
            exeServers.Add(new ComExeServer
            {
                Executable = RequiredAttribute(server, "Executable"),
                Arguments = NullIfEmpty(server.AttributeValue("Arguments")),
                DisplayName = NullIfEmpty(server.AttributeValue("DisplayName")?.Trim()),
                Classes = ParseComClasses(server),
            });
        }

        var surrogateServers = new List<ComSurrogateServer>();
        foreach (XElement server in element.ElementsByLocalName("SurrogateServer"))
        {
            surrogateServers.Add(new ComSurrogateServer
            {
                DisplayName = NullIfEmpty(server.AttributeValue("DisplayName")?.Trim()),
                AppId = NullIfEmpty(server.AttributeValue("AppId")?.Trim()),
                Classes = ParseComClasses(server),
            });
        }

        var progIds = new List<ComProgId>();
        foreach (XElement progId in element.ElementsByLocalName("ProgId"))
        {
            progIds.Add(new ComProgId
            {
                Id = RequiredAttribute(progId, "Id"),
                Clsid = NullIfEmpty(progId.AttributeValue("Clsid")?.Trim()),
            });
        }

        return new ComServerExtension
        {
            ExeServers = exeServers,
            SurrogateServers = surrogateServers,
            ProgIds = progIds,
        };
    }

    private static List<ComClass> ParseComClasses(XElement server)
    {
        var result = new List<ComClass>();
        foreach (XElement comClass in server.ElementsByLocalName("Class"))
        {
            result.Add(new ComClass
            {
                Id = RequiredAttribute(comClass, "Id"),
                DisplayName = NullIfEmpty(comClass.AttributeValue("DisplayName")?.Trim()),
                Path = NullIfEmpty(comClass.AttributeValue("Path")),
                ThreadingModel = NullIfEmpty(comClass.AttributeValue("ThreadingModel")?.Trim()),
                ProgId = NullIfEmpty(comClass.AttributeValue("ProgId")?.Trim()),
            });
        }

        return result;
    }

    private static ShortcutExtension? ParseShortcut(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new ShortcutExtension
        {
            File = RequiredAttribute(element, "File"),
            Icon = RequiredAttribute(element, "Icon"),
            Arguments = NullIfEmpty(element.AttributeValue("Arguments")),
            Description = NullIfEmpty(element.AttributeValue("Description")?.Trim()),
            PinToStartMenu = ParseOptionalBoolean(element, "PinToStartMenu"),
        };
    }

    private static string RequiredAttribute(XElement element, string name) =>
        NullIfEmpty(element.AttributeValue(name)?.Trim())
        ?? throw MsixError.Format(MsixErrorCode.ManifestSemantics,
            $"A '{element.Name.LocalName}' element is missing the required '{name}' attribute.");

    /// <summary>
    /// Reads an optional <c>xs:boolean</c> attribute, returning <see langword="null"/> when it is
    /// absent so that "unstated" stays distinguishable from "stated false".
    /// </summary>
    private static bool? ParseOptionalBoolean(XElement element, string name)
    {
        string? value = NullIfEmpty(element.AttributeValue(name)?.Trim());
        if (value is null)
        {
            return null;
        }

        try
        {
            // XmlConvert, not bool.Parse: xs:boolean also admits "1" and "0".
            return XmlConvert.ToBoolean(value);
        }
        catch (FormatException ex)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                $"'{element.Name.LocalName}' has an invalid {name} value '{value}'.", ex);
        }
    }
}
