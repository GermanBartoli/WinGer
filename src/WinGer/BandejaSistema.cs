using Microsoft.UI.Xaml;

namespace WinGer;

public sealed class BandejaSistema : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon icono;
    private readonly System.Drawing.Icon dibujo;
    public BandejaSistema(Window ventana, Action salir)
    {
        dibujo = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "WinGer.ico"), 32, 32);
        void Mostrar() => ventana.DispatcherQueue.TryEnqueue(() => { ventana.AppWindow.Show(); ventana.Activate(); });
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir WinGer", null, (_, _) => Mostrar());
        menu.Items.Add("Salir", null, (_, _) => ventana.DispatcherQueue.TryEnqueue(() => salir()));
        icono = new System.Windows.Forms.NotifyIcon { Text = "WinGer · Actualizaciones y VirusTotal", Icon = dibujo, ContextMenuStrip = menu, Visible = true };
        icono.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Mostrar(); };
    }
    public void Dispose() { icono.Visible = false; icono.ContextMenuStrip?.Dispose(); icono.Dispose(); dibujo.Dispose(); }
}
