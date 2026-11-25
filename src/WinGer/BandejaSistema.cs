using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace WinGer;

public sealed class BandejaSistema : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon icono;
    private readonly System.Drawing.Icon dibujo;
    private readonly EventoDeVentana observarFoco;
    private readonly nint ganchoFoco;
    private readonly nint principal;
    private bool principalAlFrente;

    public BandejaSistema(Window ventana, Action salir)
    {
        dibujo = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "WinGer.ico"), 32, 32);
        void Mostrar() => App.MostrarEnPrimerPlano(ventana);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir WinGer", null, (_, _) => Mostrar());
        menu.Items.Add("Salir", null, (_, _) => ventana.DispatcherQueue.TryEnqueue(() => salir()));
        icono = new System.Windows.Forms.NotifyIcon { Text = "WinGer · Actualizaciones y VirusTotal", Icon = dibujo, ContextMenuStrip = menu, Visible = true };
        principal = WinRT.Interop.WindowNative.GetWindowHandle(ventana);
        observarFoco = (_, _, activa, _, _, _, _) => RegistrarFoco(activa);
        const uint cambioDePrimerPlano = 3; // EVENT_SYSTEM_FOREGROUND
        ganchoFoco = SetWinEventHook(cambioDePrimerPlano, cambioDePrimerPlano, 0, observarFoco, 0, 0, 0);
        RegistrarFoco(GetForegroundWindow());

        icono.MouseClick += (_, e) =>
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left) return;
            RegistrarFoco(GetForegroundWindow());
            if (principalAlFrente && ventana.AppWindow.IsVisible &&
                ventana.AppWindow.Presenter is OverlappedPresenter presentador &&
                presentador.State != OverlappedPresenterState.Minimized)
                presentador.Minimize();
            else
                Mostrar();
        };
    }

    private void RegistrarFoco(nint activa)
    {
        if (activa == 0) return;
        var clase = new StringBuilder(128);
        GetClassName(activa, clase, clase.Capacity);
        // Al abrir los iconos ocultos, la bandeja toma el foco temporalmente.
        if (clase.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or
            "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland") return;
        principalAlFrente = activa == principal;
    }

    private delegate void EventoDeVentana(nint gancho, uint evento, nint ventana,
        int objeto, int hijo, uint hilo, uint tiempo);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint desde, uint hasta, nint modulo,
        EventoDeVentana callback, uint proceso, uint hilo, uint opciones);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint gancho);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint ventana, StringBuilder clase, int capacidad);

    public void Dispose()
    {
        if (ganchoFoco != 0) UnhookWinEvent(ganchoFoco);
        icono.Visible = false;
        icono.ContextMenuStrip?.Dispose();
        icono.Dispose();
        dibujo.Dispose();
    }
}
