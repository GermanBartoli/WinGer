using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private static void MarcarWebPendiente(Paquete paquete)
    {
        paquete.RegistrarErrorConsulta("Web", "Lectura dejada pendiente por el usuario.");
        paquete.Estado = "Web · lectura pendiente";
        paquete.Nivel = "amarillo";
        paquete.Detalle = "La lectura web quedó pendiente. Se conservan los resultados anteriores; podés volver a consultarla.";
    }

    private async Task<bool> ConsultarWeb(Paquete paquete, CancellationToken token)
    {
        var hash = await Gestor.ObtenerHashAsync(paquete, token);
        var navegador = new WebView2 { MinHeight = 420, MinWidth = 600 };
        var aviso = Texto("Leyendo el informe visible. Si la página requiere verificación, completala aquí o usá Dejar pendiente y seguir. Podés dejar pendientes todas las restantes.");
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(aviso); panel.Children.Add(navegador);
        var dialogo = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = "VirusTotal web · " + paquete.Nombre,
            Content = panel, CloseButtonText = "Dejar pendiente y seguir", SecondaryButtonText = "Dejar pendientes todas" };
        using var lectura = CancellationTokenSource.CreateLinkedTokenSource(token);
        var omitirTodas = false;
        dialogo.SecondaryButtonClick += (_, _) => omitirTodas = true;
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
                lectura.Token.ThrowIfCancellationRequested();
                var informe = LecturaInformeWeb.Interpretar(json, hash);
                if (informe is null)
                {
                    if (!LecturaInformeWeb.EsInformeAusente(json, hash)) continue;
                    paquete.RegistrarConsulta("Web", hash, null);
                    paquete.Descarga = null;
                    paquete.Nivel = "amarillo";
                    paquete.Estado = "Web · sin informe";
                    paquete.Detalle = $"VirusTotal muestra Item not found para SHA-256: {hash}. No hay informe disponible; no significa que el archivo sea seguro. Se continúa con la siguiente aplicación.";
                    return true;
                }
                paquete.RegistrarConsulta("Web", hash, informe);
                paquete.Descarga = null;
                paquete.Nivel = informe.Maliciosos > 0 || informe.Sospechosos > 0 ? "rojo" : "amarillo";
                paquete.Estado = $"Web · {informe.Maliciosos} detecciones · {informe.Sospechosos} sospechas";
                paquete.Detalle = $"Lectura web experimental · {DateTimeOffset.Now:g}\nSHA-256: {hash}\n\n{informe.Resumen}\n\n{informe.Json}\n\nLectura de la página, sin clave API. No habilita la instalación ni verifica el archivo real.";
                return true;
            }
            throw new InvalidOperationException("No se pudo leer un informe completo en la web. Queda pendiente; no significa que el archivo sea seguro.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            MarcarWebPendiente(paquete);
            return !omitirTodas;
        }
        catch (Exception error)
        {
            paquete.RegistrarErrorConsulta("Web", error.Message);
            paquete.Estado = "Web · pendiente de lectura"; paquete.Nivel = "amarillo";
            paquete.Detalle = "Lectura web no completada. Puede requerir verificación, no tener informe o haber cambiado su estructura.";
            throw;
        }
        finally { dialogo.Hide(); await visible; navegador.Close(); }
    }
}
