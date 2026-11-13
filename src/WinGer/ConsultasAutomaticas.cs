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
    private async Task ConsultarVtai(Paquete paquete, string credencial, CancellationToken token)
    {
        var hash = await Gestor.ObtenerHashAsync(paquete, token);
        var informe = await Vtai.ConsultarAsync(hash, credencial, token);
        paquete.RegistrarConsulta("VTAI", hash, informe);
        paquete.Descarga = null;
        paquete.Detalle = $"Fuente: VirusTotal AI · consultado {DateTimeOffset.Now:g}\nSHA-256: {hash}\n\n{informe?.Resumen ?? "Sin informe existente."}\n\n{informe?.Json}\n\nEl instalador no se descargó ni se subió. Cero detecciones no garantiza seguridad.";
        paquete.Nivel = informe is { Maliciosos: > 0 } or { Sospechosos: > 0 } ? "rojo" : PoliticaInstalacion.InformeAprobado(informe, DateTimeOffset.UtcNow) ? "verde" : "amarillo";
        paquete.Estado = informe is null ? "VTAI · sin informe" : $"VTAI · {informe.Maliciosos} detecciones · {informe.Sospechosos} sospechas";
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
                    await ConsultarVtai(paquete, credencial, token);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                paquete.RegistrarErrorConsulta(modo == "VTAI" ? "VTAI" : "Web", error.Message);
                paquete.Nivel = "amarillo"; paquete.Estado = modo + " · pendiente"; paquete.Detalle = error.Message;
                pendientes++;
                // Detener la cola ante límites o errores del servicio, sin recurrir a la web para eludir cuotas.
                if (modo == "VTAI" && error is InvalidOperationException && error.Message.StartsWith("VTAI")) throw;
            }
            finally { GuardarCache(); }
        }
        if (pendientes > 0) throw new InvalidOperationException($"Búsqueda guardada; {pendientes} consultas pendientes. Revisá los detalles.");
    }
    private Task ConsultarFuentesSeleccion(string[] fuentes) => Ejecutar("Consulta de resultados seleccionados", async token =>
    {
        var seleccion = paquetes.Where(p => p.Seleccionado).ToArray();
        if (seleccion.Length == 0) throw new InvalidOperationException("Seleccioná aplicaciones o pulsá Seleccionar visibles antes de consultar.");
        var pendientes = 0;
        foreach (var fuente in fuentes)
        {
            foreach (var paquete in seleccion)
            {
                token.ThrowIfCancellationRequested();
                Modelo.Estado = $"Consultando {fuente} · {paquete.Nombre}";
                try
                {
                    switch (fuente)
                    {
                        case "API": await Gestor.ConsultarPublicadoAsync(paquete, LeerClave(), token); break;
                        case "VTAI": await ConsultarVtai(paquete, await ObtenerTokenVtai(token), token); break;
                        case "Web": await ConsultarWeb(paquete, token); break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    pendientes++;
                    paquete.RegistrarErrorConsulta(fuente, error.Message);
                    paquete.Detalle = error.Message;
                    // No pasar a otra fuente para eludir una cuota agotada o una indisponibilidad.
                    if (error.Message.Contains("límite de VirusTotal") || error.Message.StartsWith("VTAI no disponible")) throw;
                }
                finally { GuardarCache(); }
                almacen.Registrar("Consulta " + fuente + " · " + paquete.Id, paquete.DetallesConsultas);
            }
        }
        if (pendientes > 0) throw new InvalidOperationException($"{pendientes} consultas pendientes. Revisá Ver detalles / error; los resultados anteriores se conservaron.");
    });
    private async void LeerInformeWeb(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Paquete paquete)
            await Ejecutar("Lectura web experimental", token => ConsultarWeb(paquete, token));
    }
}
