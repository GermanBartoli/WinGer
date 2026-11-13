using System.Net;
using System.Text;
using WinGer.Core;

var cantidad = 0;
void Verificar(bool condicion, string nombre)
{
    if (!condicion) throw new Exception("Falló: " + nombre);
    Console.WriteLine("Correcto: " + nombre);
    cantidad++;
}
async Task Rechazar(Func<Task> accion, string nombre)
{
    try { await accion(); }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException or OperationCanceledException or HttpRequestException)
    { Verificar(true, nombre); return; }
    throw new Exception("No se rechazó: " + nombre);
}
const string hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
Verificar(Hashes.Validar("  " + hash.ToUpperInvariant() + "  ") == hash, "normalización del hash");
foreach (var invalido in new[] { "", "--apikey", "../archivo", new string('g', 64), new string('a', 63), hash + "\ncomando" })
    await Rechazar(() => { Hashes.Validar(invalido); return Task.CompletedTask; }, "rechazo de hash inválido");
var carpeta = Path.Combine(Path.GetTempPath(), "WinGer-pruebas-" + Guid.NewGuid());
Directory.CreateDirectory(carpeta);
try
{
    var archivo = Path.Combine(carpeta, "muestra.txt");
    await File.WriteAllTextAsync(archivo, "abc", new UTF8Encoding(false));
    Verificar(await Hashes.CalcularAsync(archivo, default) == hash, "SHA-256 conocido de archivo real");
    using var cancelacion = new CancellationTokenSource(); cancelacion.Cancel();
    await Rechazar(async () => { await Hashes.CalcularAsync(archivo, cancelacion.Token); }, "cancelación del cálculo");
    var almacen = new AlmacenLocal(carpeta);
    almacen.Guardar(new Configuracion { MinimizarAlCerrar = true, Tema = "Oscuro" });
    var configuracion = new AlmacenLocal(carpeta).LeerConfiguracion();
    Verificar(configuracion.MinimizarAlCerrar && configuracion.Tema == "Oscuro", "persistencia de ajustes");
    for (var i = 0; i < 205; i++) almacen.Registrar("Prueba " + i, "Correcto");
    Verificar(almacen.LeerHistorial().Count == 200 && almacen.LeerHistorial()[0].Operacion == "Prueba 204", "historial limitado y ordenado");
    await File.WriteAllTextAsync(Path.Combine(carpeta, "configuracion.json"), "{inválido");
    Verificar(!almacen.LeerConfiguracion().MinimizarAlCerrar, "recuperación de configuración inválida");
}
finally { Directory.Delete(carpeta, true); }
const string json = """{"data":{"attributes":{"last_analysis_stats":{"malicious":2,"suspicious":1,"undetected":60,"harmless":3},"last_analysis_date":1700000000}}}""";
var informe = ServicioVirusTotal.Interpretar(json, hash);
Verificar(informe.Maliciosos == 2 && informe.Sospechosos == 1 && informe.Fecha.HasValue, "interpretación de detecciones y fecha");
foreach (var estado in new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
{
    using var cliente = new HttpClient(new Simulador(estado, json));
    var servicio = new ServicioVirusTotal(cliente);
    if (estado == HttpStatusCode.OK) Verificar((await servicio.ConsultarAsync(hash, "clave-de-prueba", default))?.Maliciosos == 2, "respuesta HTTP correcta");
    else if (estado == HttpStatusCode.NotFound) Verificar(await servicio.ConsultarAsync(hash, "clave-de-prueba", default) is null, "ausencia de informe no se interpreta como seguro");
    else await Rechazar(async () => { await servicio.ConsultarAsync(hash, "clave-de-prueba", default); }, "error HTTP " + (int)estado);
}
var ejecutor = new EjecutorProcesos();
var proceso = await ejecutor.EjecutarAsync("dotnet", ["--version"], default);
Verificar(proceso.Codigo == 0 && !string.IsNullOrWhiteSpace(proceso.Salida), "ejecución real y captura de salida");
await Rechazar(async () => { await ejecutor.EjecutarAsync("winger-programa-inexistente", [], default); }, "herramienta ausente");
Console.WriteLine($"\n{cantidad} verificaciones correctas. No se enviaron archivos ni se consultó VirusTotal real.");

sealed class Simulador(HttpStatusCode estado, string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken cancelacion)
    {
        if (solicitud.Method != HttpMethod.Get || solicitud.RequestUri?.Host != "www.virustotal.com" || !solicitud.Headers.Contains("x-apikey"))
            throw new Exception("Solicitud de VirusTotal incorrecta");
        return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(json) });
    }
}
