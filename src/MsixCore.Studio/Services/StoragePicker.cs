using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MsixCore.Studio.Services;

public sealed class StoragePicker(Func<Window> windowProvider) : IStoragePicker
{
    public async Task<string?> PickPackageAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".msix");
        picker.FileTypeFilter.Add(".appx");
        InitializeWithWindow(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow(picker);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void InitializeWithWindow(object picker)
    {
#if WINDOWS
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(windowProvider());
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
#else
        _ = windowProvider;
#endif
    }
}
