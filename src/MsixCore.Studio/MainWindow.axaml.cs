using Avalonia.Controls;
using MsixCore.Studio.Services;
using MsixCore.Studio.ViewModels;

namespace MsixCore.Studio;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(new AvaloniaStoragePicker(() => StorageProvider));
    }
}
