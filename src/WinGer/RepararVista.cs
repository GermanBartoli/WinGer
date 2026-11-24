using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private const string CarpetaHerramientas = @"C:\Cosas\Reparar PC";
    private bool reparacionEnCurso;
    private void MostrarReparar(int indice = 0)
    {
        Encabezado("Reparar PC", "Diagnóstico y reparación de Windows, junto con tus herramientas instaladas.");
        var pestanas = new Pivot();
        pestanas.Items.Add(new PivotItem { Header = "Comandos de Windows", Content = CrearComandos() });
        pestanas.Items.Add(new PivotItem { Header = "Mis herramientas", Content = CrearHerramientas() });
        pestanas.Items.Add(new PivotItem { Header = "Solución de problemas", Content = CrearSolucionadores(pestanas) });
        pestanas.SelectedIndex = indice;
        Contenido.Children.Add(pestanas);
    }
    private StackPanel CrearComandos()
    {
        var contenido = new StackPanel { Spacing = 14, Margin = new Thickness(0,16,0,0) };
        contenido.Children.Add(Texto("Para reparar archivos de Windows: ejecutá primero DISM · Reparar imagen y después SFC · Reparar archivos de sistema. Cada comando abre una consola con su progreso y resultado. Los que modifican Windows piden permisos de administrador."));
        var cierre = new CheckBox { Content = "Cerrar la consola automáticamente al terminar", IsChecked = configuracion.CerrarConsolaReparacion };
        cierre.Click += (_, _) => GuardarAjuste(() => configuracion.CerrarConsolaReparacion = cierre.IsChecked == true);
        contenido.Children.Add(cierre);
        foreach (var seccion in new[] { "Análisis y comprobaciones", "Reparación y mantenimiento", "Información del equipo" })
        {
            contenido.Children.Add(new TextBlock { Text = seccion, FontSize = 22 });
            var tarjetas = new PanelAjustable { AnchoElemento = 340 };
            foreach (var comando in ComandosReparacion.Todos.Where(c => ResultadosReparacion.Seccion(c) == seccion))
            {
                var cuerpo = new StackPanel { Spacing = 10 };
                var cabecera = Fila(); cabecera.Children.Add(new FontIcon { Glyph = comando.Icono, FontSize = 28 });
                cabecera.Children.Add(new TextBlock { Text = comando.Nombre, MaxWidth = 240, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                cuerpo.Children.Add(cabecera); cuerpo.Children.Add(Texto(comando.Descripcion));
                cuerpo.Children.Add(new TextBlock { Text = comando.Comando, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") });
                cuerpo.Children.Add(Texto(comando.Administrador ? "Requiere administrador" : "Consulta sin elevación"));
                cuerpo.Children.Add(Boton("Ejecutar…", () => EjecutarReparacion(comando)));
                var marco = MarcoHerramienta(cuerpo);
                AgregarResultado(comando, cuerpo, marco);
                tarjetas.Children.Add(marco);
            }
            contenido.Children.Add(tarjetas);
        }
        return contenido;
    }
    private StackPanel CrearSolucionadores(Pivot pestanas)
    {
        var contenido = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var comando = ComandosReparacion.Camara;
        var cuerpo = new StackPanel { Spacing = 12 };
        var titulo = Fila();
        titulo.Children.Add(new FontIcon { Glyph = comando.Icono });
        titulo.Children.Add(new TextBlock { Text = "Cámara", FontSize = 22 });
        cuerpo.Children.Add(titulo);
        cuerpo.Children.Add(Texto("Si la cámara desaparece o queda sin imagen al cambiar de aplicación, podés reiniciarla desde WinGer o abrir el asistente de Windows."));
        var reparar = Boton("Reparar por comandos…", () => EjecutarReparacion(comando));
        reparar.IsEnabled = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
        cuerpo.Children.Add(reparar);
        if (!reparar.IsEnabled) cuerpo.Children.Add(Texto("La reparación por comandos requiere Windows 10 versión 2004 o posterior."));
        cuerpo.Children.Add(Boton("Obtener ayuda de Windows", async () =>
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-contact-support://smc-to-emerald/TroubleshootCamera")))
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://aka.ms/GetHelp-CameraTroubleshooter"));
        }));
        var marco = MarcoHerramienta(cuerpo);
        AgregarResultado(comando, cuerpo, marco);
        var tarjetas = new PanelAjustable { AnchoElemento = 420 };
        tarjetas.Children.Add(marco);
        var fondo = new StackPanel { Spacing = 12 };
        fondo.Children.Add(new TextBlock { Text = "Fondo de escritorio", FontSize = 22 });
        fondo.Children.Add(Texto("Volvé a aplicar tu foto desde una copia local. Si la moviste, podés elegirla en su nueva ubicación."));
        var estado = Texto("");
        var marcoFondo = MarcoHerramienta(fondo);
        void MostrarFondoRestaurado()
        {
            marcoFondo.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 160, 90));
            marcoFondo.BorderThickness = new Thickness(2);
            marcoFondo.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(35, 46, 160, 90));
            estado.Text = "Windows usa la copia local del fondo.";
        }
        if (FondoEscritorio.UsaCopiaLocal()) MostrarFondoRestaurado();
        fondo.Children.Add(Boton("Restaurar fondo de escritorio", async () =>
        {
            var ruta = FondoEscritorio.RutaActual;
            if (!File.Exists(ruta))
            {
                estado.Text = string.IsNullOrWhiteSpace(ruta) ? "Elegí la foto que querés usar." : $"La imagen configurada ya no está disponible en {ruta}. Elegí la foto para recuperarla.";
                var selector = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));
                foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
                    selector.FileTypeFilter.Add(extension);
                ruta = (await selector.PickSingleFileAsync())?.Path;
                if (ruta is null) return;
            }
            await Task.Run(() => FondoEscritorio.Restaurar(ruta));
            MostrarFondoRestaurado();
        }));
        fondo.Children.Add(estado);
        tarjetas.Children.Add(marcoFondo);

        var amd = new StackPanel { Spacing = 12 };
        amd.Children.Add(new TextBlock { Text = "Fallos gráficos con AMD", FontSize = 22 });
        amd.Children.Add(Texto("Si sospechás que un juego tiene problemas con los sombreadores, abrí AMD Software y buscá «Restablecer caché de sombreadores» en Gráficos → Avanzado. La próxima carga puede tardar más."));
        var rutaAmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"AMD\CNext\CNext\RadeonSoftware.exe");
        var abrirAmd = Boton("Abrir AMD Adrenalin", () =>
        {
            Process.Start(new ProcessStartInfo(rutaAmd) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        abrirAmd.IsEnabled = File.Exists(rutaAmd);
        amd.Children.Add(abrirAmd);
        if (!abrirAmd.IsEnabled) amd.Children.Add(Texto("WinGer no encontró AMD Software en su carpeta de instalación habitual."));
        tarjetas.Children.Add(MarcoHerramienta(amd));

        var explorador = new StackPanel { Spacing = 12 };
        explorador.Children.Add(new TextBlock { Text = "Iconos en blanco o miniaturas incorrectas", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        explorador.Children.Add(Texto("Elegí qué se ve mal en el Explorador. WinGer revisa esa caché y te muestra sus archivos para que decidas cuáles borrar."));
        var botones = Fila();
        botones.Children.Add(Boton("Revisar iconos", () => RevisarCacheExplorador(Limpieza.Iconos)));
        botones.Children.Add(Boton("Revisar miniaturas", () => RevisarCacheExplorador(Limpieza.Miniaturas)));
        explorador.Children.Add(botones);
        tarjetas.Children.Add(MarcoHerramienta(explorador));
        contenido.Children.Add(tarjetas);
        var enlace = new HyperlinkButton { Content = "Revisar archivos del sistema (DISM y SFC)", Margin = new Thickness(0, 12, 0, 0) };
        enlace.Click += (_, _) => pestanas.SelectedIndex = 0;
        contenido.Children.Add(enlace);
        return contenido;
    }

    private void AgregarResultado(ComandoReparacion comando, StackPanel cuerpo, Border marco)
    {
        if (almacen.LeerReparaciones().TryGetValue(comando.Comando, out var resultado))
        {
            resultado = ResultadosReparacion.Interpretar(comando, resultado.Inicio, resultado.Fin, resultado.Codigo, resultado.Salida);
            cuerpo.Children.Add(Texto(resultado.Mensaje));
            cuerpo.Children.Add(Texto($"Última ejecución: {resultado.Inicio.ToLocalTime():g} · Duración: {(resultado.Fin - resultado.Inicio).TotalSeconds:N0} s · Código: {resultado.Codigo?.ToString() ?? "sin confirmar"}"));
            var color = resultado.Nivel == "verde" ? Windows.UI.Color.FromArgb(255, 46, 160, 90) : resultado.Nivel == "rojo" ? Windows.UI.Color.FromArgb(255, 220, 65, 65) : Windows.UI.Color.FromArgb(255, 210, 160, 35);
            marco.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color); marco.BorderThickness = new Thickness(2);
            marco.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(35, color.R, color.G, color.B));
            cuerpo.Children.Add(Boton("Ver último resultado", async () => {
                var detalle = new StackPanel { Spacing = 16 };
                detalle.Children.Add(Texto(resultado.Mensaje));
                detalle.Children.Add(Texto("Resultado guardado de la última ejecución en WinGer."));
                detalle.Children.Add(new TextBlock { Text = resultado.Salida, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") });
                var dialogo = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = comando.Nombre, PrimaryButtonText = "Copiar todo", CloseButtonText = "Cerrar", Content = new ScrollViewer { MaxHeight = Math.Max(200, Raiz.ActualHeight - 230), Content = detalle }, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                dialogo.PrimaryButtonClick += (_, args) =>
                {
                    args.Cancel = true;
                    try
                    {
                        System.Windows.Forms.Clipboard.SetText(($"{comando.Nombre}\n{comando.Comando}\nÚltima ejecución: {resultado.Inicio.ToLocalTime():g} · Duración: {(resultado.Fin - resultado.Inicio).TotalSeconds:N0} s · Código: {resultado.Codigo?.ToString() ?? "sin confirmar"}\n\n{resultado.Mensaje}\n\n").Replace("\n", Environment.NewLine) + resultado.Salida);
                        dialogo.PrimaryButtonText = "Copiado";
                    }
                    catch (Exception)
                    {
                        dialogo.PrimaryButtonText = "Reintentar copia";
                        Modelo.Estado = "No se pudo copiar el resultado. Volvé a intentarlo.";
                    }
                };
                dialogo.Resources["ContentDialogMaxWidth"] = 860d;
                detalle.Width = Math.Max(240, Math.Min(780, Raiz.ActualWidth - 100));
                await dialogo.ShowAsync();
            }));
        }
        else cuerpo.Children.Add(Texto("Todavía no se ejecutó desde esta versión de WinGer."));
    }

    private async Task EjecutarReparacion(ComandoReparacion comando)
    {
        if (reparacionEnCurso || Modelo.Ocupado) { Modelo.Estado = "Esperá a que termine la operación actual."; return; }
        if (!ComandosReparacion.EsConocido(comando)) throw new InvalidOperationException("Comando no reconocido.");
        var confirmacion = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = comando.Nombre,
            Content = comando.Descripcion + "\n\n" + (comando == ComandosReparacion.Camara ? "pnputil /restart-device <cámara seleccionada>" : comando.Comando) + "\n\nLa consola mostrará el resultado. No cierres la consola durante una reparación.",
            PrimaryButtonText = "Ejecutar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close };
        if (await confirmacion.ShowAsync() != ContentDialogResult.Primary) return;
        if (reparacionEnCurso || Modelo.Ocupado) return;
        reparacionEnCurso = true;
        var fechaInicio = DateTimeOffset.Now;
        var carpeta = Path.Combine(almacen.RutaDatos, "Reparaciones");
        var archivo = Path.Combine(carpeta, Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(carpeta);
            var inicio = ComandosReparacion.CrearInicio(comando, archivo, configuracion.CerrarConsolaReparacion);
            using var proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo abrir la consola.");
            Modelo.Estado = "En curso: " + comando.Nombre + ". Revisá la consola.";
            almacen.Registrar("Reparación · " + comando.Nombre, "Iniciada por el usuario");
            // Sin timeout ni cancelación forzada: DISM y SFC pueden tardar considerablemente.
            while (!proceso.HasExited && !File.Exists(archivo)) await Task.Delay(500);
            int? codigo = null; var salida = "La consola se cerró sin dejar un resultado completo."; var fin = DateTimeOffset.Now;
            if (File.Exists(archivo))
            {
                using var documento = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(archivo));
                var raiz = documento.RootElement; codigo = raiz.GetProperty("Codigo").GetInt32(); salida = raiz.GetProperty("Salida").GetString() ?? "";
                if (comando == ComandosReparacion.Camara && codigo == 0)
                {
                    try
                    {
                        await VistaPreviaCamara.Mostrar(raiz.GetProperty("InstanciaCamara").GetString()!);
                        salida += Environment.NewLine + ResultadosReparacion.VideoConfirmado;
                    }
                    catch (Exception error)
                    {
                        salida += Environment.NewLine + ResultadosReparacion.VideoSinConfirmar + " " + error.Message;
                    }
                    fin = DateTimeOffset.Now;
                }
                else fin = DateTimeOffset.Parse(raiz.GetProperty("Fin").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            }
            var resultado = ResultadosReparacion.Interpretar(comando, fechaInicio, fin, codigo, salida);
            almacen.GuardarReparacion(comando, resultado);
            File.Delete(archivo);
            Modelo.Estado = comando.Nombre + " · " + resultado.Mensaje;
            almacen.Registrar("Reparación · " + comando.Nombre, resultado.Mensaje);
            if (Navegacion.SelectedItem is NavigationViewItem seleccion && seleccion.Tag?.ToString() == "reparar")
                MostrarReparar(Contenido.Children.OfType<Pivot>().FirstOrDefault()?.SelectedIndex ?? 0);
        }
        finally { reparacionEnCurso = false; }
    }
    private StackPanel CrearHerramientas()
    {
        var contenido = new StackPanel { Spacing = 14, Margin = new Thickness(0,16,0,0) };
        contenido.Children.Add(Texto(CarpetaHerramientas));
        contenido.Children.Add(Texto("Abre tus accesos directos con sus opciones originales. Cada herramienta administra sus propios permisos y operaciones."));
        var filtro = new TextBox { PlaceholderText = "Buscar herramienta" }; contenido.Children.Add(filtro);
        var acciones = new PanelAjustable(); var tarjetas = new PanelAjustable { AnchoElemento = 300 }; var estado = Texto("");
        void Cargar()
        {
            tarjetas.Children.Clear();
            if (!Directory.Exists(CarpetaHerramientas)) { estado.Text = "La carpeta no existe en este equipo. Creala o usá estas herramientas en la PC donde están instaladas."; return; }
            var entradas = Directory.EnumerateFileSystemEntries(CarpetaHerramientas).Where(p => Directory.Exists(p) || new[] { ".lnk", ".url", ".exe" }.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(Path.GetFileName).Where(p => Path.GetFileName(p).Contains(filtro.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            estado.Text = entradas.Length + " herramientas";
            foreach (var entrada in entradas)
            {
                var destino = entrada;
                if (Directory.Exists(entrada))
                {
                    var nombre = Path.GetFileName(entrada);
                    var preferido = Path.Combine(entrada, nombre.Equals("ProcessExplorer", StringComparison.OrdinalIgnoreCase) ? "procexp64.exe" : nombre + ".exe");
                    if (File.Exists(preferido)) destino = preferido;
                }
                var cuerpo = new StackPanel { Spacing = 10 }; var fila = Fila(); fila.Children.Add(IconoArchivo(destino));
                fila.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(entrada), TextWrapping = TextWrapping.Wrap, MaxWidth = 205, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                cuerpo.Children.Add(fila); cuerpo.Children.Add(Texto(Directory.Exists(destino) ? "Carpeta de herramientas" : "Herramienta local"));
                cuerpo.Children.Add(Boton(Directory.Exists(destino) ? "Abrir carpeta" : "Abrir", () => { Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true, WorkingDirectory = Directory.Exists(destino) ? destino : Path.GetDirectoryName(destino)! }); return Task.CompletedTask; }));
                tarjetas.Children.Add(MarcoHerramienta(cuerpo));
            }
        }
        acciones.Children.Add(Boton("Actualizar accesos", () => { Cargar(); return Task.CompletedTask; }));
        acciones.Children.Add(Boton("Abrir carpeta de herramientas", () => { if (!Directory.Exists(CarpetaHerramientas)) throw new DirectoryNotFoundException(CarpetaHerramientas); Process.Start(new ProcessStartInfo(CarpetaHerramientas) { UseShellExecute = true }); return Task.CompletedTask; }));
        filtro.TextChanged += (_, _) => Cargar(); contenido.Children.Add(acciones); contenido.Children.Add(estado); contenido.Children.Add(tarjetas); Cargar(); return contenido;
    }
}
