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
            catch (OperationCanceledException) { if (!vida.IsCancellationRequested) sincronizacion.Text = "La consulta tardó demasiado. Volvé a sincronizar."; estado = null; }
            catch (Exception error) { estado = null; sincronizacion.Text = error.Message + " Los datos anteriores, si aparecen, no están actualizados."; }
            finally { consultando = false; Habilitar(); }
        }
        async Task EjecutarDefender(AccionDefender accion, string titulo)
        {
            if (defenderEnCurso || estado?.PuedeAnalizar != true) return;
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
            var dialogo = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = titulo, Content = explicacion + "\n\nWindows puede pedir permisos de administrador.", PrimaryButtonText = "Iniciar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close };
            if (await dialogo.ShowAsync() != ContentDialogResult.Primary || defenderEnCurso) return;
            defenderEnCurso = true; resultadoDefender = titulo + " · En curso…"; Habilitar();
            try
            {
                using var proceso = Process.Start(ProteccionDefender.CrearInicio(accion, carpeta)) ?? throw new InvalidOperationException("No se pudo iniciar Defender.");
                almacen.Registrar(titulo, "Solicitado por el usuario");
                // No se corta un análisis largo ni se simula un porcentaje de progreso.
                await proceso.WaitForExitAsync();
                resultadoDefender = proceso.ExitCode == 0
                    ? titulo + " · Comando finalizado. Revisá las fechas y el historial de protección para conocer el resultado."
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
        void Tarjeta(string titulo, string descripcion, string datos, AccionDefender accion)
        {
            var cuerpo = new StackPanel { Spacing = 12 };
            cuerpo.Children.Add(new FontIcon { Glyph = "\uEA18", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 28 });
            cuerpo.Children.Add(new TextBlock { Text = titulo, FontSize = 21, TextWrapping = TextWrapping.Wrap });
            cuerpo.Children.Add(Texto(descripcion)); cuerpo.Children.Add(Texto(datos));
            var boton = Boton(accion == AccionDefender.ActualizarFirmas ? "Actualizar definiciones…" : "Iniciar análisis…", () => EjecutarDefender(accion, titulo));
            botones.Add(boton); cuerpo.Children.Add(boton); tarjetas.Children.Add(MarcoHerramienta(cuerpo));
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
            Tarjeta("Análisis rápido", "Revisa las zonas donde suelen encontrarse amenazas.", Fechas(estado.InicioRapido, estado.FinRapido), AccionDefender.Rapido);
            Tarjeta("Análisis completo", "Revisa todos los archivos y programas. Puede tardar varias horas.", Fechas(estado.InicioCompleto, estado.FinCompleto), AccionDefender.Completo);
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
