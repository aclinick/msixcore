using Avalonia.Platform.Storage;

namespace MsixCore.Studio.Services;

public sealed class AvaloniaStoragePicker(Func<IStorageProvider> storageProvider) : IStoragePicker
{
    private static readonly FilePickerFileType MsixFileType = new("MSIX packages")
    {
        Patterns = ["*.msix", "*.appx"],
    };

    public async Task<string?> PickPackageAsync()
    {
        IReadOnlyList<IStorageFile> files = await storageProvider().OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open an MSIX or APPX package",
                AllowMultiple = false,
                FileTypeFilter = [MsixFileType],
            });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await storageProvider().OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open a loose package folder",
                AllowMultiple = false,
            });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
