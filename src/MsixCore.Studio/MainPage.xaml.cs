using Microsoft.UI.Xaml.Controls;
using MsixCore.Studio.Services;
using MsixCore.Studio.ViewModels;

namespace MsixCore.Studio;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        ViewModel = new MainWindowViewModel(new StoragePicker(() => App.MainWindow));
        DataContext = ViewModel;
        InitializeComponent();
    }

    public MainWindowViewModel ViewModel { get; }
}
