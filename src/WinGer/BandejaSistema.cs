using Microsoft.UI.Xaml;

namespace WinGer;

public sealed class BandejaSistema : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon icono;
    private readonly System.Drawing.Icon dibujo;
    public BandejaSistema(Window ventana, Action salir)
    {
        using var imagen = new System.Drawing.Bitmap(32, 32);
        using (var pincel = System.Drawing.Graphics.FromImage(imagen))
        {
            pincel.Clear(System.Drawing.Color.FromArgb(24, 115, 190));
            using var fuente = new System.Drawing.Font("Segoe UI", 18, System.Drawing.FontStyle.Bold);
            pincel.DrawString("W", fuente, System.Drawing.Brushes.White, -1, 0);
        }
        var identificador = imagen.GetHicon();
        using (var temporal = System.Drawing.Icon.FromHandle(identificador)) dibujo = (System.Drawing.Icon)temporal.Clone();
        DestroyIcon(identificador);
        void Mostrar() => ventana.DispatcherQueue.TryEnqueue(() => { ventana.AppWindow.Show(); ventana.Activate(); });
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir WinGer", null, (_, _) => Mostrar());
        menu.Items.Add("Salir", null, (_, _) => ventana.DispatcherQueue.TryEnqueue(() => salir()));
        icono = new System.Windows.Forms.NotifyIcon { Text = "WinGer · Actualizaciones y VirusTotal", Icon = dibujo, ContextMenuStrip = menu, Visible = true };
        icono.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Mostrar(); };
    }
    public void Dispose() { icono.Visible = false; icono.ContextMenuStrip?.Dispose(); icono.Dispose(); dibujo.Dispose(); }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icono);
}
