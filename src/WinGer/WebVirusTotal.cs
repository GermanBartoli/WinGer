using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private async Task ConsultarWeb(Paquete paquete, CancellationToken token)
    {
        var hash = await Gestor.ObtenerHashAsync(paquete, token);
        var navegador = new WebView2 { MinHeight = 420, MinWidth = 600 };
        var aviso = Texto("Leyendo el informe visible. Si la página requiere verificación, completala aquí o cerrá para dejarlo pendiente.");
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(aviso); panel.Children.Add(navegador);
        var dialogo = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = "VirusTotal web · " + paquete.Nombre,
            Content = panel, CloseButtonText = "Dejar pendiente" };
        using var lectura = CancellationTokenSource.CreateLinkedTokenSource(token);
        dialogo.Closing += (_, _) => lectura.Cancel();
        var visible = dialogo.ShowAsync();
        try
        {
            var entorno = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGer", "WebView2"), null);
            await navegador.EnsureCoreWebView2Async(entorno);
            navegador.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            navegador.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            navegador.Source = new Uri($"https://www.virustotal.com/gui/file/{hash}/detection");
            for (var intento = 0; intento < 90; intento++)
            {
                await Task.Delay(1000, lectura.Token);
                var json = await navegador.ExecuteScriptAsync(LecturaInformeWeb.Script);
                var informe = LecturaInformeWeb.Interpretar(json, hash);
                if (informe is null) continue;
                paquete.Descarga = null;
                paquete.Nivel = informe.Maliciosos > 0 || informe.Sospechosos > 0 ? "rojo" : "amarillo";
                paquete.Estado = $"Web · {informe.Maliciosos} detecciones · {informe.Sospechosos} sospechas";
                paquete.Detalle = $"Lectura web experimental · {DateTimeOffset.Now:g}\nSHA-256: {hash}\n\n{informe.Resumen}\n\n{informe.Json}\n\nLectura de la página, sin clave API. No habilita la instalación ni verifica el archivo real.";
                return;
            }
            throw new InvalidOperationException("No se pudo leer un informe completo en la web. Queda pendiente; no significa que el archivo sea seguro.");
        }
        catch
        {
            paquete.Estado = "Web · pendiente de lectura"; paquete.Nivel = "amarillo";
            paquete.Detalle = "Lectura web no completada. Puede requerir verificación, no tener informe o haber cambiado su estructura.";
            throw;
        }
        finally { dialogo.Hide(); await visible; navegador.Close(); }
    }
}
