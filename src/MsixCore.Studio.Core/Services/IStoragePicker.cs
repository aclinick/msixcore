namespace MsixCore.Studio.Services;

public interface IStoragePicker
{
    Task<string?> PickPackageAsync();

    Task<string?> PickFolderAsync();
}
