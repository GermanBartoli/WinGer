using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Windows.Storage.Pickers;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private bool defenderEnCurso;
    private string resultadoDefender = "";

    private void MostrarProteccion()
    {
        Encabezado("Protección", "Microsoft Defender · Estado y análisis sincronizados con Windows.");
        var acciones = new PanelAjustable();
        var sincronizacion = Texto("Consultando Microsoft Defender…");
        var operacion = Texto(resultadoDefender);
        var tarjetas = new PanelAjustable { AnchoElemento = 340 };
        var detalles = new StackPanel { Spacing = 12 };
        var vida = new CancellationTokenSource();
        var consultando = false;
        var temporizador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        EstadoDefender? estado = null;
        var botones = new List<Button>();
        void Habilitar()
        {
            foreach (var boton in botones) boton.IsEnabled = estado?.PuedeAnalizar == true && !defenderEnCurso;
            operacion.Text = resultadoDefender;
        }
        async Task Actualizar()
        {
            if (consultando || vida.IsCancellationRequested) return;
            consultando = true;
            try
            {
                var nuevo = await ProteccionDefender.ConsultarAsync(vida.Token);
                if (vida.IsCancellationRequested) return;
                estado = nuevo;
                sincronizacion.Text = $"Sincronizado: {nuevo.Consultado.ToLocalTime():g} · Se actualiza cada 30 segundos mientras esta sección está abierta.";
                Pintar();
            }
            catch (OperationCanceledException) { if (!vida.IsCancellationRequested) sincronizacion.Text = "La consulta tardó demasiado. Volvé a sincronizar."; estado = null; tarjetas.Children.Clear(); detalles.Children.Clear(); }
            catch (Exception error) { estado = null; tarjetas.Children.Clear(); detalles.Children.Clear(); sincronizacion.Text = error.Message; }
            finally { consultando = false; Habilitar(); }
        }
        async Task EjecutarDefender(AccionDefender accion, string titulo)
        {
            if (defenderEnCurso || estado?.PuedeAnalizar != true) return;
            if (accion == AccionDefender.SinConexion && (Modelo.Ocupado || reparacionEnCurso)) { Modelo.Estado = "Esperá a que terminen las operaciones de WinGer antes de reiniciar."; return; }
            string? carpeta = null;
            if (accion == AccionDefender.Carpeta)
            {
                var selector = new FolderPicker(); selector.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));
                carpeta = (await selector.PickSingleFolderAsync())?.Path;
                if (carpeta is null) return;
            }
            var explicacion = accion == AccionDefender.ActualizarFirmas
                ? "Windows buscará y descargará las últimas definiciones de Microsoft Defender."
                : "Defender analizará " + (carpeta ?? (accion == AccionDefender.Completo ? "todo el equipo. Puede tardar varias horas" : "las zonas más habituales de amenazas")) + ". Aplicará las acciones de protección configuradas en Windows.";
            if (accion == AccionDefender.SinConexion)
                explicacion = "La PC se reiniciará para analizar amenazas fuera de Windows. Guardá tus documentos y cerrá las aplicaciones. Puede tardar unos 15 minutos y necesita el entorno de recuperación de Windows.\n\nSi BitLocker está protegido o no se puede comprobar, WinGer bloqueará esta acción. Revisá BitLocker y tu clave de recuperación antes de usar el análisis sin conexión. WinGer no suspende el cifrado.\n\nAl volver, consultá el historial en Seguridad de Windows.";
            var dialogo = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = titulo, Content = explicacion + "\n\nWindows puede pedir permisos de administrador.", PrimaryButtonText = accion == AccionDefender.SinConexion ? "Reiniciar y analizar" : "Iniciar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close };
            if (accion == AccionDefender.SinConexion)
            {
                var contenido = new StackPanel { Spacing = 16 }; contenido.Children.Add(Texto(explicacion));
                var guardado = new CheckBox { Content = "Guardé mi trabajo y acepto reiniciar la PC", IsChecked = false };
                dialogo.IsPrimaryButtonEnabled = false;
                guardado.Checked += (_, _) => dialogo.IsPrimaryButtonEnabled = true;
                guardado.Unchecked += (_, _) => dialogo.IsPrimaryButtonEnabled = false;
                contenido.Children.Add(guardado); dialogo.Content = contenido;
            }
            if (await dialogo.ShowAsync() != ContentDialogResult.Primary || defenderEnCurso) return;
            if (accion == AccionDefender.SinConexion && (Modelo.Ocupado || reparacionEnCurso)) return;
            defenderEnCurso = true; resultadoDefender = titulo + " · En curso…"; Habilitar();
            try
            {
                if (accion == AccionDefender.SinConexion) { GuardarCache(); almacen.Registrar(titulo, "Reinicio solicitado por el usuario; resultado pendiente de Windows"); }
                using var proceso = Process.Start(ProteccionDefender.CrearInicio(accion, carpeta)) ?? throw new InvalidOperationException("No se pudo iniciar Defender.");
                almacen.Registrar(titulo, "Solicitado por el usuario");
                // No se corta un análisis largo ni se simula un porcentaje de progreso.
                await proceso.WaitForExitAsync();
                resultadoDefender = proceso.ExitCode == 2 && accion == AccionDefender.SinConexion
                    ? "No se reinició: BitLocker está protegido o no pudo comprobarse. Revisá BitLocker y Seguridad de Windows."
                    : proceso.ExitCode == 0
                    ? titulo + (accion == AccionDefender.SinConexion ? " · Solicitud enviada a Windows; el resultado se consulta después del reinicio." : " · Comando finalizado. Revisá las fechas y el historial de protección para conocer el resultado.")
                    : titulo + " · No se completó. Revisá Seguridad de Windows: puede haber otro análisis en curso o una restricción del sistema.";
                almacen.Registrar(titulo, resultadoDefender);
            }
            catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223) { resultadoDefender = titulo + " · Permiso cancelado; no se inició desde WinGer."; }
            catch (Exception error) { resultadoDefender = titulo + " · " + error.Message; }
            finally
            {
                defenderEnCurso = false; Habilitar(); Modelo.Estado = resultadoDefender;
                await Actualizar();
            }
        }
        void Tarjeta(string titulo, string descripcion, string datos, AccionDefender accion, DateTimeOffset? fin = null)
        {
            var cuerpo = new StackPanel { Spacing = 12 };
            cuerpo.Children.Add(new FontIcon { Glyph = "\uEA18", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 28 });
            cuerpo.Children.Add(new TextBlock { Text = titulo, FontSize = 21, TextWrapping = TextWrapping.Wrap });
            cuerpo.Children.Add(Texto(descripcion)); cuerpo.Children.Add(Texto(datos));
            var boton = Boton(accion == AccionDefender.ActualizarFirmas ? "Actualizar definiciones…" : accion == AccionDefender.SinConexion ? "Preparar reinicio y análisis…" : "Iniciar análisis…", () => EjecutarDefender(accion, titulo));
            botones.Add(boton); cuerpo.Children.Add(boton);
            var marco = MarcoHerramienta(cuerpo);
            if (accion is AccionDefender.Rapido or AccionDefender.Completo)
            {
                var vigencia = ProteccionDefender.Vigencia(accion, fin, DateTimeOffset.Now);
                var color = vigencia.Nivel switch
                {
                    "verde" => Windows.UI.Color.FromArgb(255, 46, 160, 90),
                    "amarillo" => Windows.UI.Color.FromArgb(255, 210, 160, 35),
                    "rojo" => Windows.UI.Color.FromArgb(255, 220, 65, 65),
                    _ => Windows.UI.Color.FromArgb(255, 135, 135, 135)
                };
                marco.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color); marco.BorderThickness = new Thickness(2);
                marco.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(35, color.R, color.G, color.B));
                cuerpo.Children.Insert(2, Texto(vigencia.Mensaje));
            }
            tarjetas.Children.Add(marco);
        }
        void Pintar()
        {
            if (estado is null) return;
            tarjetas.Children.Clear(); detalles.Children.Clear(); botones.Clear();
            string SiNo(bool? valor) => valor switch { true => "Activada", false => "Desactivada", _ => "No disponible" };
            detalles.Children.Add(new InfoBar { IsOpen = true, IsClosable = false,
                Severity = estado.Antivirus == true && estado.TiempoReal == true && estado.Modo == "Normal" ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                Title = estado.Antivirus == true && estado.TiempoReal == true && estado.Modo == "Normal" ? "Protección en tiempo real activada" : "Revisá la protección de Windows",
                Message = $"Modo de Defender: {estado.Modo ?? "No disponible"}. Este estado no equivale a un análisis sin amenazas." });
            detalles.Children.Add(Texto($"Antivirus: {SiNo(estado.Antivirus)} · Tiempo real: {SiNo(estado.TiempoReal)}\nSupervisión del comportamiento: {SiNo(estado.Comportamiento)} · Protección contra alteraciones: {SiNo(estado.Antimanipulacion)}"));
            detalles.Children.Add(Texto("Último análisis rápido o completo finalizado registrado por Windows: " + EstadoDefender.Fecha(estado.UltimoFin)));
            string Fechas(DateTimeOffset? inicio, DateTimeOffset? fin) => $"Último inicio: {EstadoDefender.Fecha(inicio)}\nÚltimo fin registrado: {EstadoDefender.Fecha(fin)}" + (EstadoDefender.FechaValida(inicio) is not null && (EstadoDefender.FechaValida(fin) is null || inicio > fin) ? "\nEl último inicio aún no tiene un fin registrado; puede seguir en curso o haberse interrumpido." : "");
            detalles.Children.Add(Texto("Colores por antigüedad del último análisis terminado; no indican ausencia de amenazas. Rápido: amarillo a los 5 días, rojo a los 7. Completo: amarillo a los 15 días, rojo a los 30. Sin fecha: gris."));
            Tarjeta("Análisis rápido", "Revisa las zonas donde suelen encontrarse amenazas.", Fechas(estado.InicioRapido, estado.FinRapido), AccionDefender.Rapido, estado.FinRapido);
            Tarjeta("Análisis completo", "Revisa todos los archivos y programas. Puede tardar varias horas.", Fechas(estado.InicioCompleto, estado.FinCompleto), AccionDefender.Completo, estado.FinCompleto);
            Tarjeta("Microsoft Defender sin conexión", "Reinicia la PC y analiza fuera de Windows para buscar amenazas difíciles de quitar.", "Requiere guardar tu trabajo. No tiene una fecha global disponible aquí; revisá el historial de Seguridad de Windows después del reinicio.", AccionDefender.SinConexion);
            Tarjeta("Analizar una carpeta", "Elegí una carpeta y revisá su contenido con Defender.", "Windows no expone aquí la fecha del último análisis personalizado. Consultá Seguridad de Windows.", AccionDefender.Carpeta);
            Tarjeta("Definiciones de seguridad", "Actualizaciones para reconocer amenazas nuevas.", $"Versión: {estado.VersionFirmas ?? "No disponible"}\nActualizadas: {EstadoDefender.Fecha(estado.FechaFirmas)}\n{(estado.FirmasAntiguas switch { true => "Windows indica que están desactualizadas.", false => "Windows no las marca como desactualizadas.", _ => "Vigencia no disponible." })}", AccionDefender.ActualizarFirmas);
            if (!estado.PuedeAnalizar) detalles.Children.Add(Texto("Los análisis desde WinGer requieren Microsoft Defender activo en modo normal. Revisá tu proveedor de antivirus en Seguridad de Windows."));
            detalles.Children.Add(Texto(estado.ErrorAmenazas ?? $"Registro de Defender: {estado.Amenazas.Length} amenazas conocidas; {estado.Amenazas.Count(a => a.Activa)} activas. Este registro no es el resultado de un análisis concreto."));
            foreach (var amenaza in estado.Amenazas.Take(10)) detalles.Children.Add(Texto($"{amenaza.Nombre} · {(amenaza.Activa ? "Activa: requiere atención" : "No activa según Defender")}"));
            if (estado.Amenazas.Length > 10) detalles.Children.Add(Texto("Se muestran las primeras 10. Abrí Seguridad de Windows para ver el historial completo."));
            Habilitar();
        }
        acciones.Children.Add(Boton("Sincronizar ahora", Actualizar));
        acciones.Children.Add(Boton("Abrir Seguridad de Windows", () => { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true }); return Task.CompletedTask; }));
        Contenido.Children.Add(acciones); Contenido.Children.Add(sincronizacion); Contenido.Children.Add(operacion);
        Contenido.Children.Add(detalles); Contenido.Children.Add(tarjetas);
        temporizador.Tick += async (_, _) => await Actualizar();
        tarjetas.Loaded += async (_, _) => { temporizador.Start(); await Actualizar(); };
        tarjetas.Unloaded += (_, _) => { temporizador.Stop(); vida.Cancel(); };
    }
}
