using System.Runtime.InteropServices;
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
        if (ventana is { } principal)
            principal.DispatcherQueue.TryEnqueue(() => MostrarEnPrimerPlano(principal));
    }

    internal static void MostrarEnPrimerPlano(Window ventana)
    {
        if (ventana.AppWindow.Presenter is OverlappedPresenter presentador &&
            presentador.State == OverlappedPresenterState.Minimized)
            presentador.Restore();

        ventana.AppWindow.Show();
        ventana.Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(ventana));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint ventana);
}
