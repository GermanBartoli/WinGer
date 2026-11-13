using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Security.Credentials;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private ServicioVirusTotalAi? vtai;
    private ServicioVirusTotalAi Vtai => vtai ??= new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) }, almacen);
    private async Task<string> ObtenerTokenVtai(CancellationToken token)
    {
        try
        {
            var guardado = credenciales.Retrieve("WinGer.VTAI", "usuario");
            guardado.RetrievePassword(); return guardado.Password;
        }
        catch (System.Runtime.InteropServices.COMException) { }
        if (configuracion.RegistroVtaiIntentado)
            throw new InvalidOperationException("El registro VTAI anterior no tiene credencial recuperable. Importá tu token VTAI en Configuración; no se creará otra identidad automáticamente.");
        configuracion.RegistroVtaiIntentado = true;
        almacen.Guardar(configuracion);
        var valor = await Vtai.RegistrarAsync(token);
        credenciales.Add(new PasswordCredential("WinGer.VTAI", "usuario", valor));
        return valor;
    }
    private async Task ConsultarAutomaticamente(CancellationToken token)
    {
        if (configuracion.ConsultaAutomatica == "Desactivado" || paquetes.Count == 0) return;
        var modo = configuracion.ConsultaAutomatica;
        var credencial = modo == "VTAI" ? await ObtenerTokenVtai(token) : "";
        var pendientes = 0;
        foreach (var paquete in paquetes)
        {
            token.ThrowIfCancellationRequested();
            Modelo.Estado = "Consultando " + modo + " · " + paquete.Nombre;
            try
            {
                if (modo == "Web experimental") await ConsultarWeb(paquete, token);
                else
                {
                    var hash = await Gestor.ObtenerHashAsync(paquete, token);
                    var informe = await Vtai.ConsultarAsync(hash, credencial, token);
                    paquete.Descarga = null;
                    paquete.Detalle = $"Fuente: VirusTotal AI · consultado {DateTimeOffset.Now:g}\nSHA-256: {hash}\n\n{informe?.Resumen ?? "Sin informe existente."}\n\n{informe?.Json}\n\nEl instalador no se descargó ni se subió. Cero detecciones no garantiza seguridad.";
                    paquete.Nivel = informe is { Maliciosos: > 0 } or { Sospechosos: > 0 } ? "rojo" : PoliticaInstalacion.InformeAprobado(informe, DateTimeOffset.UtcNow) ? "verde" : "amarillo";
                    paquete.Estado = informe is null ? "VTAI · sin informe" : $"VTAI · {informe.Maliciosos} detecciones · {informe.Sospechosos} sospechas";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                paquete.Nivel = "amarillo"; paquete.Estado = modo + " · pendiente"; paquete.Detalle = error.Message;
                pendientes++;
                // Detener la cola ante límites o errores del servicio, sin recurrir a la web para eludir cuotas.
                if (modo == "VTAI" && error is InvalidOperationException && error.Message.StartsWith("VTAI")) throw;
            }
            finally { GuardarCache(); }
        }
        if (pendientes > 0) throw new InvalidOperationException($"Búsqueda guardada; {pendientes} consultas pendientes. Revisá los detalles.");
    }
    private async void LeerInformeWeb(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Paquete paquete)
            await Ejecutar("Lectura web experimental", token => ConsultarWeb(paquete, token));
    }
}
