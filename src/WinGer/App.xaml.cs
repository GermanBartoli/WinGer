using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace WinGer;

public partial class App : Application
{
    private Window? ventana;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ventana = new MainWindow();
        MostrarVentana();
    }

    internal void MostrarVentana()
    {
        ventana?.DispatcherQueue.TryEnqueue(() =>
        {
            ventana.AppWindow.Show();
            if (ventana.AppWindow.Presenter is OverlappedPresenter presentador &&
                presentador.State == OverlappedPresenterState.Minimized)
                presentador.Restore();
            ventana.Activate();
        });
    }
}
