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
    private readonly ServicioVirusTotal virus = new(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
    private readonly PasswordVault credenciales = new();
    private Configuracion configuracion;
    private CancellationTokenSource? cancelacion;
    private bool salir;
    private bool inicializado;
    private string salidaWinget = "Buscá actualizaciones para consultar los programas detectados por WinGet.";
    private string salidaVirus = "Consultá un hash o calculá el SHA-256 de un archivo local. Calcular el hash no sube el archivo.";
    private string hashActual = "";
    private TextBox? resultados;

    public MainWindow()
    {
        configuracion = almacen.LeerConfiguracion();
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        AppWindow.Closing += (_, e) =>
        {
            if (!salir && configuracion.MinimizarAlCerrar)
            {
                e.Cancel = true;
                (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
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
            case "virus": MostrarVirus(); break;
            case "historial": MostrarHistorial(); break;
            default: MostrarActualizaciones(); break;
        }
    }
    private void Encabezado(string titulo, string descripcion)
    {
        Contenido.Children.Clear();
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
        if (Modelo.Ocupado) { Modelo.Estado = "Ya hay una operación en curso. Esperá o cancelala."; return; }
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
        finally { cancelacion = null; Modelo.Ocupado = false; }
    }
    private void MostrarActualizaciones()
    {
        Encabezado("Tus actualizaciones", "Revisá lo que WinGet tiene disponible. Esta primera versión consulta; todavía no instala programas.");
        var tarjetas = Fila();
        foreach (var (titulo, detalle) in new[] { ("01 · Descubrir", "Actualizaciones con WinGet"), ("02 · Comprobar", "Hash e informes de VirusTotal"), ("03 · Instalar", "Próxima etapa · aún deshabilitada") })
        {
            var interior = new StackPanel { Spacing = 8 };
            interior.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            interior.Children.Add(Texto(detalle));
            tarjetas.Children.Add(new Border { Child = interior, Padding = new Thickness(16), CornerRadius = new CornerRadius(12),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] });
        }
        Contenido.Children.Add(tarjetas);
        Contenido.Children.Add(Boton("Buscar actualizaciones", () => Ejecutar("Consulta de WinGet", async token =>
        {
            var caja = resultados;
            var respuesta = await procesos.EjecutarAsync("winget.exe", ["list", "--upgrade-available", "--source", "winget", "--disable-interactivity"], token);
            salidaWinget = respuesta.Salida;
            if (caja is not null) caja.Text = salidaWinget;
            if (respuesta.Codigo != 0) throw new InvalidOperationException($"WinGet terminó con código {respuesta.Codigo}. Revisá su salida; puede requerir aceptar los acuerdos del origen desde la terminal.");
        })));
        Salida(salidaWinget);
        Contenido.Children.Add(Texto("Se respetan las exclusiones de WinGet y no se incluyen versiones desconocidas. No se ejecutan actualizaciones automáticamente."));
    }
    private void MostrarVirus()
    {
        Encabezado("Centro VirusTotal", "Dos accesos al mismo servicio: API para informes de hashes y vt-cli para consultar, subir archivos y recuperar análisis.");
        var hash = new TextBox { Header = "Hash MD5, SHA-1 o SHA-256", PlaceholderText = "Pegá el hash o calculalo desde un archivo", Text = hashActual };
        hash.TextChanged += (_, _) => hashActual = hash.Text;
        Contenido.Children.Add(hash);
        var fila = Fila();
        fila.Children.Add(Boton("Consultar hash · API", () => Ejecutar("Informe por API", async token =>
        {
            var caja = resultados;
            var hashValidado = Hashes.Validar(hash.Text);
            var informe = await virus.ConsultarAsync(hashValidado, LeerClave(), token);
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
        Encabezado("Historial", "Últimas 200 operaciones. No se guardan claves ni el contenido de los archivos.");
        var entradas = almacen.LeerHistorial();
        if (entradas.Count == 0) Contenido.Children.Add(Texto("Todavía no hay operaciones registradas."));
        foreach (var entrada in entradas) Contenido.Children.Add(Texto($"{entrada.Fecha.LocalDateTime:g}  ·  {entrada.Operacion}  ·  {entrada.Resultado}"));
    }
    private string LeerClave()
    {
        try { var credencial = credenciales.Retrieve("WinGer.VirusTotal", "usuario"); credencial.RetrievePassword(); return credencial.Password; }
        catch { throw new InvalidOperationException("Guardá tu clave de VirusTotal en Configuración."); }
    }
    private void MostrarConfiguracion()
    {
        Encabezado("Configuración", "Personalizá WinGer. Los ajustes se guardan para tu usuario de Windows.");
        var minimizar = new ToggleSwitch { Header = "Al cerrar con la cruz", OnContent = "Minimizar a la barra de tareas", OffContent = "Cerrar WinGer", IsOn = configuracion.MinimizarAlCerrar };
        minimizar.Toggled += (_, _) => GuardarAjuste(() => configuracion.MinimizarAlCerrar = minimizar.IsOn);
        Contenido.Children.Add(minimizar);
        Contenido.Children.Add(Texto("La ventana permanece en la barra de tareas; esta versión no utiliza un icono en la bandeja del sistema."));
        var tema = new ComboBox { Header = "Apariencia", ItemsSource = new[] { "Predeterminado", "Oscuro", "Claro" }, SelectedItem = configuracion.Tema, Width = 220 };
        tema.SelectionChanged += (_, _) => GuardarAjuste(() => { configuracion.Tema = tema.SelectedItem?.ToString() ?? "Predeterminado"; AplicarTema(); });
        Contenido.Children.Add(tema);
        var clave = new PasswordBox { Header = "Clave de API de VirusTotal", PlaceholderText = "Ingresá una clave para guardarla o reemplazarla", MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        Contenido.Children.Add(clave);
        Contenido.Children.Add(Texto("Se almacena en el Administrador de credenciales de Windows. Nunca se escribe en el repositorio ni en el historial. vt-cli la recibe únicamente en el entorno de su proceso."));
        var botones = Fila();
        botones.Children.Add(Boton("Guardar clave", () =>
        {
            var valor = clave.Password.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(valor, @"\A[a-fA-F0-9]{64}\z")) throw new ArgumentException("La clave de VirusTotal debe tener 64 caracteres hexadecimales.");
            credenciales.Add(new PasswordCredential("WinGer.VirusTotal", "usuario", valor));
            clave.Password = "";
            Modelo.Estado = "Clave protegida y guardada en Windows.";
            return Task.CompletedTask;
        }));
        botones.Children.Add(Boton("Eliminar clave", () =>
        {
            try { credenciales.Remove(credenciales.Retrieve("WinGer.VirusTotal", "usuario")); }
            catch (System.Runtime.InteropServices.COMException) { }
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
        Contenido.Children.Add(Boton("Salir de WinGer", () => { salir = true; Close(); return Task.CompletedTask; }));
    }
    private void GuardarAjuste(Action cambio)
    {
        try { cambio(); almacen.Guardar(configuracion); Modelo.Estado = "Configuración guardada."; }
        catch (Exception error) { Modelo.Estado = "No se pudo guardar: " + error.Message; }
    }
    private void AplicarTema() => Raiz.RequestedTheme = configuracion.Tema switch { "Oscuro" => ElementTheme.Dark, "Claro" => ElementTheme.Light, _ => ElementTheme.Default };
    private void Cancelar(object sender, RoutedEventArgs e) => cancelacion?.Cancel();
}
