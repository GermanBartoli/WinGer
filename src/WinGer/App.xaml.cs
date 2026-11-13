using Microsoft.UI.Xaml;

namespace WinGer;

public partial class App : Application
{
    private Window? ventana;
    public App() => InitializeComponent();
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var instancia = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("WinGer");
        if (!instancia.IsCurrent)
        {
            await instancia.RedirectActivationToAsync(Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }
        ventana = new MainWindow();
        instancia.Activated += (_, _) => ventana.DispatcherQueue.TryEnqueue(() => { ventana.AppWindow.Show(); ventana.Activate(); });
        ventana.Activate();
    }
}
