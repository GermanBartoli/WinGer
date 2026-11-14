using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private string busquedaCatalogo = "";
    private string categoriaCatalogo = "Todas";
    private IReadOnlyList<AplicacionCatalogo> resultadosCatalogo = CatalogoAplicaciones.Populares;
    private bool resultadosDeWinGet;
    private void MostrarExplorar()
    {
        Encabezado("Explorar aplicaciones", "Descubrí navegadores, herramientas y aplicaciones de uso habitual, o buscá en el catálogo de WinGet.");
        var buscar = new TextBox { PlaceholderText = "Nombre o identificador de la aplicación", Text = busquedaCatalogo };
        var categorias = new ComboBox { Header = "Categoría", MinWidth = 240, ItemsSource = new[] { "Todas" }.Concat(CatalogoAplicaciones.Populares.Select(p => p.Categoria).Distinct()).Append("WinGet").ToArray(), SelectedItem = categoriaCatalogo };
        var tarjetas = new PanelAjustable { AnchoElemento = 340 };
        var resumen = Texto("");
        void Actualizar()
        {
            tarjetas.Children.Clear();
            var visibles = resultadosCatalogo.Where(p => (categoriaCatalogo == "Todas" || p.Categoria == categoriaCatalogo) && (p.Nombre.Contains(busquedaCatalogo, StringComparison.OrdinalIgnoreCase) || p.Id.Contains(busquedaCatalogo, StringComparison.OrdinalIgnoreCase))).ToArray();
            resumen.Text = $"{visibles.Length} aplicaciones · " + (resultadosDeWinGet ? "Resultados de WinGet (hasta 100 por búsqueda)." : "Selección de aplicaciones conocidas; no es un ranking de descargas.");
            foreach (var app in visibles) tarjetas.Children.Add(TarjetaCatalogo(app));
        }
        buscar.TextChanged += (_, _) => { busquedaCatalogo = buscar.Text; Actualizar(); };
        categorias.SelectionChanged += (_, _) => { categoriaCatalogo = categorias.SelectedItem?.ToString() ?? "Todas"; Actualizar(); };
        var acciones = new PanelAjustable();
        acciones.Children.Add(Boton("Buscar en WinGet", () => Ejecutar("Búsqueda de aplicaciones", async token =>
        {
            if (string.IsNullOrWhiteSpace(busquedaCatalogo)) throw new InvalidOperationException("Escribí un nombre o identificador para buscar.");
            var respuesta = await procesos.EjecutarAsync("winget.exe", ["search", "--query", busquedaCatalogo.Trim(), "--count", "100", "--source", "winget", "--disable-interactivity"], token);
            if (respuesta.Codigo != 0 && respuesta.Codigo != unchecked((int)0x8A150014)) throw new InvalidOperationException(respuesta.Salida);
            resultadosCatalogo = CatalogoAplicaciones.LeerBusqueda(respuesta.Salida); resultadosDeWinGet = true;
            categoriaCatalogo = "Todas"; categorias.SelectedItem = "Todas"; Actualizar();
        })));
        acciones.Children.Add(Boton("Ver selección popular", () => { resultadosCatalogo = CatalogoAplicaciones.Populares; resultadosDeWinGet = false; buscar.Text = ""; categorias.SelectedItem = "Todas"; Actualizar(); return Task.CompletedTask; }));
        Contenido.Children.Add(buscar); Contenido.Children.Add(acciones); Contenido.Children.Add(categorias); Contenido.Children.Add(resumen); Contenido.Children.Add(tarjetas); Actualizar();
    }
    private Border TarjetaCatalogo(AplicacionCatalogo app)
    {
        var cuerpo = new StackPanel { Spacing = 10 };
        var titulo = Fila(); titulo.Children.Add(IconoAplicacion(app));
        titulo.Children.Add(new TextBlock { Text = app.Nombre, TextWrapping = TextWrapping.Wrap, MaxWidth = 245, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        cuerpo.Children.Add(titulo); cuerpo.Children.Add(Texto(app.Descripcion)); cuerpo.Children.Add(Texto(app.Id + (app.Version == "" ? "" : " · " + app.Version)));
        var estado = Texto("Descarga el instalador sin ejecutarlo."); cuerpo.Children.Add(estado);
        string? archivo = null;
        var abrir = Boton("Abrir carpeta", () => { if (archivo is not null) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(archivo)!) { UseShellExecute = true }); return Task.CompletedTask; }); abrir.IsEnabled = false;
        var informe = Boton("Ver VirusTotal", () => Task.CompletedTask); informe.Visibility = Visibility.Collapsed;
        var descargar = Boton("Descargar y comprobar", () => Ejecutar("Descarga de " + app.Nombre, async token =>
        {
            estado.Text = "Consultando versión y descargando…";
            try
            {
                var version = app.Version;
                if (version == "")
                {
                    var respuesta = await procesos.EjecutarAsync("winget.exe", ["show", "--id", app.Id, "--exact", "--source", "winget", "--disable-interactivity"], token);
                    if (respuesta.Codigo != 0) throw new InvalidOperationException(respuesta.Salida);
                    version = CatalogoAplicaciones.LeerVersion(respuesta.Salida);
                }
                var paquete = new Paquete { Nombre = app.Nombre, Id = app.Id, Instalada = "", Disponible = version };
                var descarga = await Gestor.DescargarVerificadoAsync(paquete, token);
                var firma = await Gestor.FirmaAsync(descarga.Ruta, token);
                archivo = descarga.Ruta; abrir.IsEnabled = true;
                estado.Text = $"Descargado · {version}\nSHA-256 coincide con WinGet.\nFirma: {firma}\nVirusTotal: todavía no consultado.\n{archivo}";
                informe.Tag = descarga.Hash; informe.Visibility = Visibility.Visible;
            }
            catch (Exception error) { estado.Text = "No se completó la descarga: " + error.Message; throw; }
        }));
        descargar.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        informe.Click += async (_, _) => { if (informe.Tag is string hash) await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.virustotal.com/gui/file/" + hash + "/detection")); };
        cuerpo.Children.Add(descargar); var acciones = new PanelAjustable(); acciones.Children.Add(abrir); acciones.Children.Add(informe); cuerpo.Children.Add(acciones);
        return MarcoHerramienta(cuerpo);
    }
    private static Border MarcoHerramienta(UIElement contenido) => new()
    {
        Padding = new Thickness(16), CornerRadius = new CornerRadius(10), Child = contenido,
        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
    };
}
