using WinGer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinGer;

public sealed partial class MainWindow
{
    private string salidaStore = "Todavía no se consultó Microsoft Store.";
    private string estadoStore = "Consultá las actualizaciones disponibles o abrí la tienda.";
    private InfoBarSeverity nivelStore = InfoBarSeverity.Informational;

    private void MostrarStore()
    {
        Encabezado("Microsoft Store", "Consultá las aplicaciones de la tienda y sus actualizaciones usando la conexión de Windows.");
        var aviso = new InfoBar { IsOpen = true, IsClosable = false, Title = "Estado de Microsoft Store",
            Message = estadoStore, Severity = nivelStore };
        Contenido.Children.Add(aviso);
        var acciones = Fila();
        var salida = new TextBox { Text = salidaStore, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap, MinHeight = 200, MaxHeight = 420,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 13 };
        ScrollViewer.SetHorizontalScrollBarVisibility(salida, ScrollBarVisibility.Auto);
        acciones.Children.Add(Boton("Buscar actualizaciones", () => ConsultarStore("updates", aviso, salida)));
        acciones.Children.Add(Boton("Ver aplicaciones instaladas", () => ConsultarStore("installed", aviso, salida)));
        Contenido.Children.Add(acciones);
        Contenido.Children.Add(Boton("Abrir Store para actualizar", async () =>
        {
            var abierto = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-windows-store://downloadsandupdates"));
            if (!abierto)
            {
                estadoStore = "Windows no pudo abrir Microsoft Store. Comprobá que la tienda esté instalada y habilitada.";
                nivelStore = InfoBarSeverity.Error;
            }
            else
            {
                estadoStore = "Continuá la actualización en Microsoft Store. Al terminar, volvé y pulsá Buscar actualizaciones para comprobar el resultado.";
                nivelStore = InfoBarSeverity.Informational;
            }
            aviso.Message = estadoStore; aviso.Severity = nivelStore;
        }));
        Contenido.Children.Add(Texto("Microsoft Store descarga e instala estas aplicaciones. Esta sección no analiza sus archivos con VirusTotal ni consume tu cuota de API. La consulta no instala actualizaciones."));
        Contenido.Children.Add(new TextBlock { Text = "Respuesta de Microsoft Store", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Contenido.Children.Add(salida);
    }

    private Task ConsultarStore(string comando, InfoBar aviso, TextBox salida) => Ejecutar("Consulta de Microsoft Store", async token =>
    {
        estadoStore = "Consultando Microsoft Store… Puede tardar unos minutos. No se instalarán actualizaciones.";
        aviso.Message = estadoStore;
        salidaStore = "Iniciando consulta…";
        salida.Text = salidaStore;
        aviso.Severity = InfoBarSeverity.Informational;
        try
        {
            var progreso = new Progress<string>(texto => { salidaStore = texto; salida.Text = texto; });
            // Store puede pedir instalar al terminar la búsqueda: la consulta siempre rechaza esa acción.
            var respuesta = await procesos.EjecutarAsync("store.exe", [comando], token, entrada: "n\n", progreso: progreso);
            salidaStore = ConsultaMicrosoftStore.Presentar(respuesta);
            salida.Text = salidaStore;
            if (respuesta.Codigo != 0) throw new InvalidOperationException("Microsoft Store no pudo completar la consulta. Revisá la respuesta de la tienda que aparece debajo.");
            estadoStore = comando == "installed"
                ? "Aplicaciones informadas por Microsoft Store. Consulta: " + DateTimeOffset.Now.ToString("g")
                : salidaStore.Contains("No updates found.", StringComparison.OrdinalIgnoreCase)
                    ? "Microsoft Store no informa actualizaciones pendientes. Consulta: " + DateTimeOffset.Now.ToString("g")
                    : "Consulta finalizada. Revisá la respuesta y abrí Store para gestionar las actualizaciones. Consulta: " + DateTimeOffset.Now.ToString("g");
            nivelStore = InfoBarSeverity.Informational;
        }
        catch (OperationCanceledException)
        {
            estadoStore = "Consulta cancelada. La salida parcial no confirma que la búsqueda haya terminado.";
            nivelStore = InfoBarSeverity.Warning;
            throw;
        }
        catch (TimeoutException)
        {
            estadoStore = "Microsoft Store no terminó la consulta en 5 minutos. Se conserva la salida parcial; podés reintentar o abrir Store.";
            nivelStore = InfoBarSeverity.Warning;
            throw;
        }
        catch (Exception error)
        {
            estadoStore = error.Message + " Si store.exe no está disponible, actualizá Microsoft Store desde su biblioteca. También podés usar Abrir Store para actualizar.";
            nivelStore = InfoBarSeverity.Error;
            throw;
        }
        finally { aviso.Message = estadoStore; aviso.Severity = nivelStore; }
    });
}
