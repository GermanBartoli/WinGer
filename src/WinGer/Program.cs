using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;

namespace WinGer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var instancia = AppInstance.FindOrRegisterForKey("WinGer");
        if (!instancia.IsCurrent)
        {
            // La redirección termina antes de salir, sin iniciar WinUI ni crear otro icono.
            AllowSetForegroundWindow(instancia.ProcessId);
            var activacion = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(async () => await instancia.RedirectActivationToAsync(activacion)).GetAwaiter().GetResult();
            return;
        }

        App? aplicacion = null;
        instancia.Activated += (_, _) => aplicacion?.MostrarVentana();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            aplicacion = new App();
        });
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
