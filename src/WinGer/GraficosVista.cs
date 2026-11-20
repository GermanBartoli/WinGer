using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace WinGer;

public sealed partial class MainWindow
{
    private readonly List<AplicacionGrafica> catalogoGraficos = [];
    private Task<List<AplicacionGrafica>>? deteccionGraficos;
    private string busquedaGraficos = "";
    private int filtroGraficos;
    private readonly DispatcherTimer temporizadorGraficos = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool sincronizandoGraficos;

    private void IniciarGraficos()
    {
        temporizadorGraficos.Tick += (_, _) => RevisarReglasGraficas();
        temporizadorGraficos.Start();
        Closed += (_, _) => temporizadorGraficos.Stop();
        RevisarReglasGraficas();
    }

    private async void RevisarReglasGraficas()
    {
        if (sincronizandoGraficos) return;
        sincronizandoGraficos = true;
        try { await Task.Run(GraficosWindows.Sincronizar); }
        catch (Exception error) { Modelo.Estado = "Preferencias gráficas: " + error.Message; }
        finally { sincronizandoGraficos = false; }
    }

    private async Task<List<AplicacionGrafica>> DetectarGraficos()
    {
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var resultado = await procesos.EjecutarAsync(powershell, ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(AppContext.BaseDirectory, "InventarioGraficos.ps1")], CancellationToken.None);
        if (resultado.Codigo != 0) throw new InvalidOperationException(resultado.Salida.Trim());
        return JsonSerializer.Deserialize<List<AplicacionGrafica>>(resultado.Salida) ?? [];
    }

    private void MostrarGraficos()
    {
        Encabezado("Gráficos", "Administrá la GPU por aplicación. Los favoritos aparecen primero.");
        var buscar = new TextBox { Header = "Buscar", PlaceholderText = "Nombre o ruta", Text = busquedaGraficos };
        var filtro = new ComboBox { Header = "Mostrar", ItemsSource = new[] { "Todas", "Favoritos", "Sin preferencia", "Rutas antiguas" }, SelectedIndex = filtroGraficos };
        var resumen = Texto("");
        var lista = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 340,
            ItemTemplate = (DataTemplate)Raiz.Resources["FilaGrafica"] };
        var acciones = new PanelAjustable();
        var herramientas = new PanelAjustable();
        Contenido.Children.Add(buscar);
        herramientas.Children.Add(filtro);
        Contenido.Children.Add(herramientas);
        Contenido.Children.Add(resumen);
        Contenido.Children.Add(lista);
        Contenido.Children.Add(acciones);
        Contenido.Children.Add(Texto("Recordar aplica alto rendimiento a versiones nuevas al abrir WinGer y cada cinco minutos mientras esté en ejecución. Reiniciá la aplicación afectada para que tome el cambio."));
        List<FilaGrafica> filas = [];
        var seleccion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool filtrando = false;
        void Filtrar()
        {
            var visibles = filas.Where(f => (filtroGraficos switch { 1 => f.Favorita, 2 => f.Pendiente, 3 => f.Ausente, _ => true })
                && (f.Nombre.Contains(busquedaGraficos, StringComparison.OrdinalIgnoreCase) || f.Ruta.Contains(busquedaGraficos, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.Favorita).ThenBy(f => f.Nombre, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Ruta).ToList();
            filtrando = true;
            lista.ItemsSource = visibles;
            foreach (var f in visibles.Where(f => seleccion.Contains(f.Ruta))) lista.SelectedItems.Add(f);
            filtrando = false;
            resumen.Text = $"{visibles.Count} visibles · {filas.Count(f => f.Favorita)} favoritos · {lista.SelectedItems.Count} seleccionadas";
        }
        void Actualizar()
        {
            filas = GraficosWindows.Leer(catalogoGraficos);
            Filtrar();
        }
        async Task Detectar()
        {
            resumen.Text = "Buscando aplicaciones y juegos…";
            try
            {
                var encontradas = await (deteccionGraficos ??= DetectarGraficos());
                if (!Contenido.Children.Contains(lista)) return;
                catalogoGraficos.Clear();
                catalogoGraficos.AddRange(encontradas);
                Actualizar();
            }
            finally { deteccionGraficos = null; }
        }
        FilaGrafica[] Elegidas() => lista.SelectedItems.Cast<FilaGrafica>().ToArray();
        Button Accion(string nombre, Action<FilaGrafica[]> hacer) => Boton(nombre, () =>
        {
            var elegidas = Elegidas();
            if (elegidas.Length == 0) { Modelo.Estado = "Seleccioná una o más aplicaciones."; return Task.CompletedTask; }
            hacer(elegidas);
            Actualizar();
            return Task.CompletedTask;
        });

        herramientas.Children.Add(Boton("Detectar aplicaciones", Detectar));
        herramientas.Children.Add(Boton("Agregar ejecutable…", async () =>
        {
            var selector = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));
            selector.FileTypeFilter.Add(".exe");
            var archivo = await selector.PickSingleFileAsync();
            if (archivo == null) return;
            GraficosWindows.Aplicar(archivo.Path, true);
            Actualizar();
        }));
        acciones.Children.Add(Accion("Alto rendimiento", fs => { foreach (var f in fs.Where(f => f.Disponible)) GraficosWindows.Aplicar(f.Ruta, false); }));
        acciones.Children.Add(Boton("Aplicar a todas las faltantes", () =>
        {
            foreach (var f in filas.Where(f => f.Pendiente)) GraficosWindows.Aplicar(f.Ruta, true);
            Actualizar();
            return Task.CompletedTask;
        }));
        acciones.Children.Add(Accion("★ Favorito", fs => { foreach (var f in fs) GraficosWindows.Marcar("Favoritos", f.Ruta, true); }));
        acciones.Children.Add(Accion("Quitar favorito", fs => { foreach (var f in fs) GraficosWindows.Marcar("Favoritos", f.Ruta, false); }));
        acciones.Children.Add(Accion("Recordar alto rendimiento", fs =>
        {
            foreach (var f in fs.Where(f => f.Disponible))
            {
                GraficosWindows.Aplicar(f.Ruta, false);
                GraficosWindows.Marcar("Recordadas", f.Ruta, true);
            }
        }));
        acciones.Children.Add(Accion("Dejar de recordar", fs => { foreach (var f in fs) GraficosWindows.Marcar("Recordadas", f.Ruta, false); }));
        acciones.Children.Add(Accion("Limpiar rutas seleccionadas", fs => { foreach (var f in fs.Where(f => f.Ausente)) GraficosWindows.Limpiar(f.Ruta); }));
        lista.SelectionChanged += (_, _) =>
        {
            if (filtrando) return;
            seleccion.Clear();
            foreach (var f in Elegidas()) seleccion.Add(f.Ruta);
            resumen.Text = $"{lista.Items.Count} visibles · {filas.Count(f => f.Favorita)} favoritos · {seleccion.Count} seleccionadas";
        };
        buscar.TextChanged += (_, _) => { busquedaGraficos = buscar.Text.Trim(); Filtrar(); };
        filtro.SelectionChanged += (_, _) => { filtroGraficos = filtro.SelectedIndex; Filtrar(); };
        Actualizar();
        if (catalogoGraficos.Count == 0) _ = CargaInicial();
        async Task CargaInicial()
        {
            try { await Detectar(); }
            catch (Exception error) { if (Contenido.Children.Contains(lista)) resumen.Text = "No se pudo completar la búsqueda. " + error.Message; }
        }
    }
}
