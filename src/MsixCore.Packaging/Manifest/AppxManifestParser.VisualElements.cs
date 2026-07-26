using System.Xml.Linq;

namespace MsixCore.Packaging.Manifest;

public static partial class AppxManifestParser
{
    private static VisualElements ParseVisualElements(XElement? element)
    {
        if (element is null)
        {
            return new VisualElements();
        }

        return new VisualElements
        {
            DisplayName = element.AttributeValue("DisplayName") ?? string.Empty,
            Description = element.AttributeValue("Description") ?? string.Empty,
            Square150x150Logo = NullIfEmpty(element.AttributeValue("Square150x150Logo")),
            Square44x44Logo = NullIfEmpty(element.AttributeValue("Square44x44Logo")),
            BackgroundColor = NullIfEmpty(element.AttributeValue("BackgroundColor")),
            AppListEntry = !string.Equals(element.AttributeValue("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase),
            VisualGroup = NullIfEmpty(element.AttributeValue("VisualGroup")),
            DefaultTile = ParseDefaultTile(element.ElementByLocalName("DefaultTile")),
            SplashScreen = ParseSplashScreen(element.ElementByLocalName("SplashScreen")),
            LockScreen = ParseLockScreen(element.ElementByLocalName("LockScreen")),
            InitialRotationPreferences = ParseRotationPreferences(element.ElementByLocalName("InitialRotationPreference")),
        };
    }

    private static DefaultTile? ParseDefaultTile(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new DefaultTile
        {
            Wide310x150Logo = NullIfEmpty(element.AttributeValue("Wide310x150Logo")),
            Square310x310Logo = NullIfEmpty(element.AttributeValue("Square310x310Logo")),
            Square71x71Logo = NullIfEmpty(element.AttributeValue("Square71x71Logo")),
            ShortName = NullIfEmpty(element.AttributeValue("ShortName")),
            ShowNameOnTiles = ParseShowNameOnTiles(element.ElementByLocalName("ShowNameOnTiles")),
        };
    }

    private static List<string> ParseShowNameOnTiles(XElement? element)
    {
        if (element is null)
        {
            return [];
        }

        var tiles = new List<string>();
        foreach (XElement showOn in element.ElementsByLocalName("ShowOn"))
        {
            tiles.Add(RequiredAttribute(showOn, "Tile"));
        }

        // The schema requires 1..4 ShowOn children, so an empty container is malformed. Rejecting it
        // also keeps the empty list unambiguously meaning "ShowNameOnTiles was not declared".
        if (tiles.Count == 0)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                "A 'ShowNameOnTiles' element must declare at least one 'ShowOn' child.");
        }

        return tiles;
    }

    private static SplashScreen? ParseSplashScreen(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new SplashScreen
        {
            Image = RequiredAttribute(element, "Image"),
            BackgroundColor = NullIfEmpty(element.AttributeValue("BackgroundColor")),
            // Matched by local name, so this picks up the uap5-qualified 'Optional' attribute.
            IsOptional = ParseOptionalBoolean(element, "Optional"),
        };
    }

    private static LockScreen? ParseLockScreen(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new LockScreen
        {
            BadgeLogo = RequiredAttribute(element, "BadgeLogo"),
            Notification = RequiredAttribute(element, "Notification"),
        };
    }

    private static List<string> ParseRotationPreferences(XElement? element)
    {
        if (element is null)
        {
            return [];
        }

        var preferences = new List<string>();
        foreach (XElement rotation in element.ElementsByLocalName("Rotation"))
        {
            preferences.Add(RequiredAttribute(rotation, "Preference"));
        }

        // 1..4 Rotation children are required, so an empty container is malformed; rejecting it also
        // keeps the empty list unambiguously meaning "no preference was declared".
        if (preferences.Count == 0)
        {
            throw MsixError.Format(MsixErrorCode.ManifestSemantics,
                "An 'InitialRotationPreference' element must declare at least one 'Rotation' child.");
        }

        return preferences;
    }
}
