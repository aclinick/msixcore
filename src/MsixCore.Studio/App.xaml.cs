using Uno.Resizetizer;

namespace MsixCore.Studio;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    internal static Window MainWindow { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.Title = "MSIX Core Studio";
        MainWindow.Content = new MainPage();
        MainWindow.SetWindowIcon();
        MainWindow.Activate();
    }
}
