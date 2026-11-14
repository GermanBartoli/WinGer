using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Security.Credentials;
using Windows.Storage.Pickers;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow : Window
{
    public PanelViewModel Modelo { get; } = new();
    private readonly AlmacenLocal almacen = new();
    private readonly EjecutorProcesos procesos = new();
    private readonly ServicioVirusTotal virus;
    private DateTimeOffset? ultimaBusqueda;
    private readonly PasswordVault credenciales = new();
    private Configuracion configuracion;
    private CancellationTokenSource? cancelacion;
    private bool salir;
    private bool inicializado;
    private string salidaVirus = "Consultá un hash o calculá el SHA-256 de un archivo local. Calcular el hash no sube el archivo.";
    private string hashActual = "";
    private TextBox? resultados;
    private BandejaSistema? bandeja;
    private readonly List<Paquete> paquetes = [];
    private ItemsControl? listaPaquetes;
    private TextBlock? contadorPaquetes;
    private bool ordenarPorDetecciones = true;
    private bool soloSeleccionados;
    private string filtroPaquetes = "";
    private ServicioPaquetes Gestor => new(procesos, virus);

    public MainWindow()
    {
        configuracion = almacen.LeerConfiguracion();
        virus = new(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, almacen);
        RestaurarCache();
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "WinGer.ico"));
        bandeja = new BandejaSistema(this, Salir);
        Closed += (_, _) => bandeja.Dispose();
        AppWindow.Closing += (_, e) =>
        {
            GuardarCache();
            if (!salir && configuracion.MinimizarAlCerrar)
            {
                e.Cancel = true;
                AppWindow.Hide();
            }
            else cancelacion?.Cancel();
        };
        AplicarTema();
        inicializado = true;
        MostrarActualizaciones();
    }
    private void CambiarSeccion(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!inicializado) return;
        if (args.IsSettingsSelected) MostrarConfiguracion();
        else switch ((args.SelectedItem as NavigationViewItem)?.Tag?.ToString())
        {
            case "explorar": MostrarExplorar(); break;
            case "reparar": MostrarReparar(); break;
            case "store": MostrarStore(); break;
            case "virus": MostrarVirus(); break;
            case "historial": MostrarHistorial(); break;
            default: MostrarActualizaciones(); break;
        }
    }
    private void Encabezado(string titulo, string descripcion)
    {
        Contenido.Children.Clear();
        listaPaquetes = null;
        contadorPaquetes = null;
        resultados = null;
        Contenido.Children.Add(new TextBlock { Text = titulo, FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Contenido.Children.Add(new TextBlock { Text = descripcion, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
    }
    private static TextBlock Texto(string texto) => new() { Text = texto, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Fila() => new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private Button Boton(string texto, Func<Task> accion)
    {
        var boton = new Button { Content = texto };
        boton.Click += async (_, _) =>
        {
            try { await accion(); }
            catch (Exception error) { Modelo.Estado = "No se pudo completar la acción: " + error.Message; }
        };
        return boton;
    }
    private TextBox Salida(string texto)
    {
        var caja = new TextBox { Text = texto, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 280, MaxHeight = 440,
            FontFamily = new FontFamily("Consolas"), FontSize = 13 };
        Contenido.Children.Add(caja);
        resultados = caja;
        return caja;
    }
    private async Task Ejecutar(string nombre, Func<CancellationToken, Task> accion)
    {
        if (Modelo.Ocupado || reparacionEnCurso) { Modelo.Estado = "Ya hay una operación en curso. Esperá o cancelala."; return; }
        Modelo.Ocupado = true;
        Modelo.Estado = nombre + "…";
        using var actual = new CancellationTokenSource();
        cancelacion = actual;
        try
        {
            await accion(actual.Token);
            Modelo.Estado = nombre + ": completado";
            almacen.Registrar(nombre, "Completado");
        }
        catch (OperationCanceledException) { Modelo.Estado = "Operación cancelada o tiempo de espera agotado."; }
        catch (Exception error)
        {
            Modelo.Estado = error.Message;
            almacen.Registrar(nombre, "No completado");
        }
        finally { cancelacion = null; Modelo.Ocupado = false; FiltrarPaquetes(); GuardarCache(); }
    }
    private void MostrarActualizaciones()
    {
        Encabezado("Tus actualizaciones", "Buscá actualizaciones y consultá sus hashes automáticamente con VTAI, sin clave clásica. Podés elegir lectura web experimental en Configuración.");
        var acciones = new PanelAjustable();
        acciones.Children.Add(Boton("Buscar actualizaciones", BuscarPaquetes));
        var actualizar = Boton("Actualizar programas seleccionados…", () => ActualizarGrupo(paquetes.Where(p => p.Seleccionado).ToArray()));
        actualizar.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        acciones.Children.Add(actualizar);
        Contenido.Children.Add(acciones);
        var accionesApi = new PanelAjustable();
        accionesApi.Children.Add(Boton("Consultar API", () => ConsultarFuentesSeleccion(["API"])));
        accionesApi.Children.Add(Boton("Consultar API faltantes", () => ConsultarFuentesSeleccion(["API"], soloApiFaltantes: true)));
        Contenido.Children.Add(accionesApi);
        var fuentes = new PanelAjustable();
        fuentes.Children.Add(Boton("Consultar VTAI", () => ConsultarFuentesSeleccion(["VTAI"])));
        fuentes.Children.Add(Boton("Leer Web", () => ConsultarFuentesSeleccion(["Web"])));
        fuentes.Children.Add(Boton("Consultar las 3", () => ConsultarFuentesSeleccion(["API", "VTAI", "Web"])));
        Contenido.Children.Add(fuentes);
        Contenido.Children.Add(Texto("Consultas sobre las aplicaciones seleccionadas. Consultar las 3 actualiza los resultados, no instala programas. Las consultas manuales renuevan el informe sin usar caché. Web puede pedir intervención."));
        Contenido.Children.Add(Texto("API faltantes: consulta las seleccionadas sin informe o con error. Conserva los informes existentes, incluso antiguos; Consultar API permite renovarlos. Web verde indica un informe sin detecciones ni sospechas; el instalador todavía no fue verificado."));
        var buscar = new TextBox { PlaceholderText = "Buscar por nombre o identificador", Text = filtroPaquetes };
        buscar.TextChanged += (_, _) => { filtroPaquetes = buscar.Text; FiltrarPaquetes(); GuardarCache(); };
        Contenido.Children.Add(buscar);
        var filtros = new PanelAjustable();
        var orden = new ComboBox { Header = "Ordenar", ItemsSource = new[] { "Sin detecciones → pendientes → rojos", "Nombre A–Z" }, SelectedIndex = ordenarPorDetecciones ? 0 : 1, MinWidth = 290 };
        orden.SelectionChanged += (_, _) => { ordenarPorDetecciones = orden.SelectedIndex == 0; FiltrarPaquetes(); GuardarCache(); };
        var seleccionados = new CheckBox { Content = "Solo seleccionadas", IsChecked = soloSeleccionados };
        seleccionados.Checked += (_, _) => { soloSeleccionados = true; FiltrarPaquetes(); GuardarCache(); };
        seleccionados.Unchecked += (_, _) => { soloSeleccionados = false; FiltrarPaquetes(); GuardarCache(); };
        filtros.Children.Add(orden); filtros.Children.Add(seleccionados); Contenido.Children.Add(filtros);
        Contenido.Children.Add(Texto("Se guardan búsqueda, orden y filtro. Se toma el mayor número de detecciones entre API, VTAI y Web; los resultados sin consultar quedan con los pendientes."));
        var seleccion = new PanelAjustable();
        seleccion.Children.Add(Boton("Seleccionar visibles", () => { foreach (var p in Visibles()) p.Seleccionado = true; ActualizarContador(); return Task.CompletedTask; }));
        seleccion.Children.Add(Boton("Limpiar selección", () => { foreach (var p in paquetes) p.Seleccionado = false; ActualizarContador(); return Task.CompletedTask; }));
        contadorPaquetes = Texto(""); seleccion.Children.Add(contadorPaquetes); Contenido.Children.Add(seleccion);
        Contenido.Children.Add(Texto("● Verde: informe sin detecciones, archivo aún sin verificar   ● Rojo: bloqueado   ● Amarillo: revisar   ● Gris: sin consulta vigente"));
        listaPaquetes = new ItemsControl { ItemsPanel = (ItemsPanelTemplate)Raiz.Resources["PanelTarjetas"],
            ItemTemplate = (DataTemplate)Raiz.Resources["TarjetaPaquete"], HorizontalAlignment = HorizontalAlignment.Stretch };
        Contenido.Children.Add(listaPaquetes);
        FiltrarPaquetes();
        Contenido.Children.Add(Texto("Consultar un informe existente no vuelve a analizar ni sube el archivo. El botón web no consume tu cuota de API. Al actualizar se descarga y verifica el archivo real; verde no garantiza seguridad."));
    }
    private void MostrarVirus()
    {
        Encabezado("Centro VirusTotal", "Dos accesos al mismo servicio: API para informes de hashes y vt-cli para consultar, subir archivos y recuperar análisis.");
        var hash = new TextBox { Header = "Hash MD5, SHA-1 o SHA-256", PlaceholderText = "Pegá el hash o calculalo desde un archivo", Text = hashActual };
        hash.TextChanged += (_, _) => { hashActual = hash.Text; GuardarCache(); };
        Contenido.Children.Add(hash);
        var fila = Fila();
        fila.Children.Add(Boton("Consultar hash · API", () => Ejecutar("Informe por API", async token =>
        {
            var caja = resultados;
            var hashValidado = Hashes.Validar(hash.Text);
            var informe = await virus.ConsultarAsync(hashValidado, LeerClave(), token, forzarConsulta: true);
            salidaVirus = informe is null ? "VirusTotal no tiene informe para este hash. No significa que sea seguro. No se subió ningún archivo." : informe.Resumen + "\n\n" + informe.Json;
            if (caja is not null) caja.Text = salidaVirus;
        })));
        fila.Children.Add(Boton("Consultar hash · vt-cli", () => EjecutarCli("Consulta con vt-cli", ["file", Hashes.Validar(hash.Text), "--format", "json"])));
        fila.Children.Add(Boton("Calcular SHA-256 local", async () =>
        {
            if (Modelo.Ocupado) return;
            var archivo = await ElegirArchivo();
            if (archivo is null) return;
            await Ejecutar("Cálculo de SHA-256", async token => { hash.Text = await Hashes.CalcularAsync(archivo, token); });
        }));
        Contenido.Children.Add(fila);
        var herramientas = Fila();
        herramientas.Children.Add(Boton("Subir archivo con vt-cli…", SubirArchivo));
        var analisis = new TextBox { PlaceholderText = "ID del análisis devuelto por vt-cli", MinWidth = 300 };
        herramientas.Children.Add(analisis);
        herramientas.Children.Add(Boton("Ver análisis", () =>
        {
            var id = analisis.Text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[a-zA-Z0-9_=-]{8,300}\z"))
                throw new ArgumentException("Ingresá un ID de análisis válido.");
            return EjecutarCli("Resultado de análisis", ["analysis", id, "--format", "json"]);
        }));
        Contenido.Children.Add(herramientas);
        Salida(salidaVirus);
        Contenido.Children.Add(Texto("Consultar un hash no envía el archivo. Subirlo puede compartir su contenido con terceros. Un análisis en cola todavía no es un resultado, y cero detecciones no garantiza seguridad."));
    }
    private async Task EjecutarCli(string nombre, string[] argumentos)
    {
        await Ejecutar(nombre, async token =>
        {
            var caja = resultados;
            var respuesta = await procesos.EjecutarAsync("vt.exe", argumentos, token, LeerClave());
            salidaVirus = respuesta.Salida;
            if (caja is not null) caja.Text = salidaVirus;
            if (respuesta.Codigo != 0) throw new InvalidOperationException($"vt-cli terminó con código {respuesta.Codigo}. Revisá la salida.");
        });
    }
    private async Task<string?> ElegirArchivo()
    {
        var selector = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));
        selector.FileTypeFilter.Add("*");
        return (await selector.PickSingleFileAsync())?.Path;
    }
    private async Task SubirArchivo()
    {
        if (Modelo.Ocupado) return;
        var archivo = await ElegirArchivo();
        if (archivo is null) return;
        var dialogo = new ContentDialog
        {
            XamlRoot = Raiz.XamlRoot, Title = "¿Compartir este archivo con VirusTotal?",
            Content = $"Archivo: {Path.GetFileName(archivo)}\n\nSu contenido se enviará a VirusTotal y puede compartirse con investigadores y terceros. No subas documentos privados ni credenciales.\n\nLa subida devuelve un ID; consultá después su resultado.",
            PrimaryButtonText = "Subir archivo", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close
        };
        if (await dialogo.ShowAsync() == ContentDialogResult.Primary)
            await EjecutarCli("Subida a VirusTotal", ["scan", "file", archivo, "--format", "json"]);
    }
    private void MostrarHistorial()
    {
        Encabezado("Historial", "Tus actualizaciones confirmadas y el registro de operaciones de WinGer.");
        var pestañas = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false };
        var actualizaciones = new StackPanel { Spacing = 14, Margin = new Thickness(0, 16, 0, 0) };
        var realizadas = almacen.LeerActualizaciones();
        if (realizadas.Count == 0) actualizaciones.Children.Add(Texto("Todavía no hay actualizaciones confirmadas registradas."));
        foreach (var entrada in realizadas.OrderByDescending(e => e.Fecha))
            actualizaciones.Children.Add(Texto($"{entrada.Nombre}  ·  {entrada.Anterior} → {entrada.Version}\n{entrada.Fecha.LocalDateTime:g}  ·  {entrada.Resultado}"));
        var operaciones = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        operaciones.Children.Add(Texto("Últimas 200 operaciones. No se guardan claves ni el contenido de los archivos."));
        foreach (var entrada in almacen.LeerHistorial()) operaciones.Children.Add(Texto($"{entrada.Fecha.LocalDateTime:g}  ·  {entrada.Operacion}  ·  {entrada.Resultado}"));
        pestañas.TabItems.Add(new TabViewItem { Header = "Historial de mis actualizaciones", IsClosable = false, Content = actualizaciones });
        pestañas.TabItems.Add(new TabViewItem { Header = "Registro de operaciones", IsClosable = false, Content = operaciones });
        Contenido.Children.Add(pestañas);
    }
    private string LeerClave()
    {
        try { var credencial = credenciales.Retrieve("WinGer.VirusTotal", "usuario"); credencial.RetrievePassword(); return credencial.Password; }
        catch { throw new InvalidOperationException("Guardá tu clave de VirusTotal en Configuración."); }
    }
    private void MostrarConfiguracion()
    {
        Encabezado("Configuración", "Personalizá WinGer. Los ajustes se guardan para tu usuario de Windows.");
        var minimizar = new ToggleSwitch { Header = "Al cerrar con la cruz", OnContent = "Mantener en la bandeja del sistema", OffContent = "Cerrar WinGer", IsOn = configuracion.MinimizarAlCerrar };
        minimizar.Toggled += (_, _) => GuardarAjuste(() => configuracion.MinimizarAlCerrar = minimizar.IsOn);
        Contenido.Children.Add(minimizar);
        Contenido.Children.Add(Texto("El icono W aparece junto al reloj, posiblemente dentro de la flecha de iconos ocultos. Un clic abre WinGer; clic derecho permite salir."));
        var tema = new ComboBox { Header = "Apariencia", ItemsSource = new[] { "Predeterminado", "Oscuro", "Claro" }, SelectedItem = configuracion.Tema, Width = 220 };
        tema.SelectionChanged += (_, _) => GuardarAjuste(() => { configuracion.Tema = tema.SelectedItem?.ToString() ?? "Predeterminado"; AplicarTema(); });
        Contenido.Children.Add(tema);
        var clave = new PasswordBox { Header = "Clave de API de VirusTotal", PlaceholderText = "Ingresá una clave para guardarla o reemplazarla", MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        Contenido.Children.Add(clave);
        var estadoClave = Texto(EstadoClave());
        Contenido.Children.Add(estadoClave);
        Contenido.Children.Add(Texto("Se almacena en el Administrador de credenciales de Windows. Nunca se escribe en el repositorio ni en el historial. vt-cli la recibe únicamente en el entorno de su proceso."));
        var botones = Fila();
        botones.Children.Add(Boton("Guardar clave", () =>
        {
            var valor = clave.Password.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(valor, @"\A[a-fA-F0-9]{64}\z")) throw new ArgumentException("La clave de VirusTotal debe tener 64 caracteres hexadecimales.");
            credenciales.Add(new PasswordCredential("WinGer.VirusTotal", "usuario", valor));
            clave.Password = "";
            estadoClave.Text = EstadoClave();
            Modelo.Estado = "Clave protegida y guardada en Windows.";
            return Task.CompletedTask;
        }));
        botones.Children.Add(Boton("Eliminar clave", () =>
        {
            try { credenciales.Remove(credenciales.Retrieve("WinGer.VirusTotal", "usuario")); }
            catch (System.Runtime.InteropServices.COMException) { }
            estadoClave.Text = EstadoClave();
            Modelo.Estado = "No hay una clave guardada para WinGer.";
            return Task.CompletedTask;
        }));
        Contenido.Children.Add(botones);
        Contenido.Children.Add(Boton("Comprobar herramientas", () => Ejecutar("Comprobación de herramientas", async token =>
        {
            var winget = await procesos.EjecutarAsync("winget.exe", ["--version"], token);
            var vt = await procesos.EjecutarAsync("vt.exe", ["version"], token);
            await new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = "Herramientas disponibles", Content = $"WinGet: {winget.Salida}\nvt-cli: {vt.Salida}", CloseButtonText = "Cerrar" }.ShowAsync();
        })));
        MostrarOpcionesCache();
        Contenido.Children.Add(Boton("Salir de WinGer", () => { Salir(); return Task.CompletedTask; }));
    }
    private void Salir() { salir = true; Close(); }
    private string EstadoClave()
    {
        try { credenciales.Retrieve("WinGer.VirusTotal", "usuario"); return "✓ Clave guardada y protegida en Windows. No se muestra por seguridad."; }
        catch { return "Sin clave guardada. Ingresá tu clave para habilitar los análisis."; }
    }
    private void GuardarAjuste(Action cambio)
    {
        try { cambio(); almacen.Guardar(configuracion); Modelo.Estado = "Configuración guardada."; }
        catch (Exception error) { Modelo.Estado = "No se pudo guardar: " + error.Message; }
    }
    private void AplicarTema() => Raiz.RequestedTheme = configuracion.Tema switch { "Oscuro" => ElementTheme.Dark, "Claro" => ElementTheme.Light, _ => ElementTheme.Default };
    private void Cancelar(object sender, RoutedEventArgs e) => cancelacion?.Cancel();
}
