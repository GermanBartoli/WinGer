using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private async void PrepararSubidaPaquete(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: Paquete paquete } || !paquete.PuedePrepararSubida) return;
        await Ejecutar("Preparar instalador para VirusTotal", async token =>
        {
            Modelo.Estado = "Descargando y comprobando SHA-256 · " + paquete.Nombre;
            var (ruta, hash, _) = await Gestor.DescargarVerificadoAsync(paquete, token);
            var firma = await Gestor.FirmaAsync(ruta, token);
            var web = new VentanaWeb(configuracion, () => GuardarAjuste(() => { }), "Subir a VirusTotal · " + paquete.Nombre,
                informe: $"https://www.virustotal.com/gui/file/{hash}/detection");
            var cerrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            web.Ventana.Closed += (_, _) => cerrada.TrySetResult();
            using var carga = CancellationTokenSource.CreateLinkedTokenSource(token);
            web.Ventana.Closed += (_, _) => carga.Cancel();
            var seleccionar = new Button { Content = "Seleccionar este instalador en VirusTotal", IsEnabled = false };
            seleccionar.Click += async (_, _) =>
            {
                seleccionar.IsEnabled = false;
                try
                {
                    if (!EsPaginaSubida(web.Navegador.Source)) throw new InvalidOperationException("Volvé a la página de subida de VirusTotal para seleccionar el archivo.");
                    var confirmar = new ContentDialog { XamlRoot = ((FrameworkElement)web.Ventana.Content).XamlRoot,
                        Title = "¿Compartir el instalador con VirusTotal?",
                        Content = $"{paquete.Nombre} · {paquete.Disponible}\nArchivo: {Path.GetFileName(ruta)}\nSHA-256: {hash}\nFirma: {firma}\n\nAl seleccionar este archivo, VirusTotal podrá recibirlo y compartirlo con terceros. El sitio puede pedir una confirmación adicional. No se instalará el programa.",
                        PrimaryButtonText = "Compartir este instalador", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close };
                    if (await confirmar.ShowAsync() != ContentDialogResult.Primary) return;
                    carga.Token.ThrowIfCancellationRequested();
                    if (!EsPaginaSubida(web.Navegador.Source)) throw new InvalidOperationException("La página cambió; no se compartió el archivo.");
                    if (await Hashes.CalcularAsync(ruta, carga.Token) != hash) throw new InvalidOperationException("El archivo cambió; no se compartió.");
                    const string localizar = "(() => { if (location.origin !== 'https://www.virustotal.com' || location.pathname !== '/gui/home/upload') return null; function buscar(r) { const i = r.querySelector('input[type=file]'); if(i) return i; for(const e of r.querySelectorAll('*')) { if(e.shadowRoot) { const v=buscar(e.shadowRoot); if(v) return v; } } return null; } return buscar(document); })()";
                    var core = web.Navegador.CoreWebView2;
                    using var respuesta = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = localizar })));
                    if (!respuesta.RootElement.GetProperty("result").TryGetProperty("objectId", out var objeto))
                        throw new InvalidOperationException("No se encontró el selector de archivos. Esperá que cargue la página o usá su botón Elegir archivo con la ruta indicada arriba.");
                    var id = objeto.GetString();
                    try { await core.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", JsonSerializer.Serialize(new { files = new[] { ruta }, objectId = id })); }
                    finally { await core.CallDevToolsProtocolMethodAsync("Runtime.releaseObject", JsonSerializer.Serialize(new { objectId = id })); }
                    web.Aviso.Text = "Instalador seleccionado. Completá la confirmación de VirusTotal si aparece y esperá el análisis. Después cerrá esta ventana y pulsá Leer web para guardar el resultado; seleccionar el archivo no confirma que el análisis haya terminado.";
                    almacen.Registrar("Subida web · " + paquete.Id, "Archivo seleccionado por el usuario; análisis pendiente de confirmar");
                }
                catch (Exception error) { if (!web.Cerrada) web.Aviso.Text = error.Message; }
                finally { if (!web.Cerrada) seleccionar.IsEnabled = true; }
            };
            web.Pie.Children.Add(seleccionar);
            web.Aviso.Text = $"SHA-256 comprobado · Firma: {firma}\nArchivo preparado: {ruta}\nTodavía no se compartió. Usá el botón inferior cuando cargue la página.";
            web.Ventana.Activate();
            try
            {
                await web.Iniciar("https://www.virustotal.com/gui/home/upload", carga.Token);
                seleccionar.IsEnabled = true;
                await cerrada.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (web.Cerrada && !token.IsCancellationRequested) { }
            finally { web.Cerrar(); }
        });
    }

    private static bool EsPaginaSubida(Uri? uri) => uri is { Scheme: "https", Host: "www.virustotal.com", AbsolutePath: "/gui/home/upload" };
}
