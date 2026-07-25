using System.Globalization;

namespace MsixCore.Studio.ViewModels;

public sealed record ApplicationItem(
    string Id,
    string DisplayName,
    string Executable,
    string EntryPoint);

public sealed record BlockMapFileItem(
    string Name,
    long Size,
    int BlockCount,
    string Verification)
{
    public string SizeText => $"{Size.ToString("N0", CultureInfo.CurrentCulture)} bytes";

    public string BlockCountText => $"{BlockCount.ToString(CultureInfo.CurrentCulture)} blocks";
}
