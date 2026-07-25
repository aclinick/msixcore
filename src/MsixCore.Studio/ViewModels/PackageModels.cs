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
    string Verification);
