using System.Net;
using System.Text;
using WinGer.Core;

if (args.Contains("--hijo-salida"))
{
    Console.WriteLine("salida " + Environment.GetEnvironmentVariable("VTCLI_APIKEY"));
    Console.Error.WriteLine("error"); Environment.ExitCode = 7; return;
}
if (args.Contains("--hijo-espera")) { await Task.Delay(Timeout.Infinite); return; }
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
await PruebasCache.Ejecutar(Verificar);
await PruebasDependencias.Ejecutar(Verificar, args.Contains("--integracion-dependencias"));
await PruebasAmpliadas.Ejecutar(Verificar);
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
    var simulador = new Simulador(estado, json);
    using var cliente = new HttpClient(simulador);
    var servicio = new ServicioVirusTotal(cliente);
    if (estado == HttpStatusCode.OK)
    {
        Verificar((await servicio.ConsultarAsync(hash, "clave-de-prueba", default))?.Maliciosos == 2, "respuesta HTTP correcta");
        await servicio.ConsultarAsync(hash, "clave-de-prueba", default);
        Verificar(simulador.Llamadas == 1, "reutilización del informe sin consumir otra petición de API");
    }
    else if (estado == HttpStatusCode.NotFound) Verificar(await servicio.ConsultarAsync(hash, "clave-de-prueba", default) is null, "ausencia de informe no se interpreta como seguro");
    else await Rechazar(async () => { await servicio.ConsultarAsync(hash, "clave-de-prueba", default); }, "error HTTP " + (int)estado);
}
var ejecutor = new EjecutorProcesos();
if (args.Contains("--integracion-firma"))
{
    using var clienteFirma = new HttpClient();
    var servicioFirma = new ServicioPaquetes(ejecutor, new ServicioVirusTotal(clienteFirma));
    var rutaFirma = Path.Combine(Path.GetTempPath(), "WinGer-firma-" + Guid.NewGuid() + ".ps1");
    try
    {
        await File.WriteAllTextAsync(rutaFirma, "# Archivo de prueba sin firma; no se ejecuta.");
        Verificar(await servicioFirma.FirmaAsync(rutaFirma, default) == "NotSigned", "firma real: archivo sin firmar identificado correctamente");
    }
    finally { File.Delete(rutaFirma); }
}
var tabla = "Nombre                  Id                  Versión       Disponible    Origen\n" +
    "----------------------------------------------------------------------------\n" +
    "Editor de prueba        Editor.Prueba       1.0           2.0           winget\n" +
    "Versión desconocida     Otro.Editor         Unknown       3.0           winget\n" +
    "Paquete truncado        Otro.Paqu…          1.0           2.0           winget\n";
var paquetes = TablaWinGet.Leer(tabla);
Verificar(TablaWinGet.LeerHashPublicado("  Instalador SHA256: " + hash) == hash, "hash previo a la descarga desde WinGet en español");
Verificar(TablaWinGet.LeerHashPublicado("  Installer SHA256: " + hash) == hash, "hash previo a la descarga desde WinGet en inglés");
await Rechazar(() => { TablaWinGet.LeerHashPublicado("Sin hash publicado"); return Task.CompletedTask; }, "no se inventa el hash si falta en WinGet");
Verificar(paquetes.Count == 1 && paquetes[0].Id == "Editor.Prueba" && paquetes[0].Disponible == "2.0", "tabla española, versiones desconocidas e IDs truncados");
var tablaInstalada = "Name                    Id                  Version       Source\n" +
    "------------------------------------------------------------------\n" +
    "Editor de prueba        Editor.Prueba       2.0           winget\n";
Verificar(TablaWinGet.Leer(tablaInstalada, false).Single().Instalada == "2.0", "confirmación de versión instalada en inglés");
var manifiesto = $"PackageIdentifier: Editor.Prueba\nPackageVersion: 2.0\nInstallers:\n- Architecture: x64\n  InstallerType: msi\n  InstallerSha256: {hash}\n  Scope: machine\n";
var datos = Manifiestos.Leer(manifiesto, "Editor.Prueba", "2.0");
Verificar(datos.Hash == hash && datos.Tipo == "msi" && datos.SinDependencias, "manifiesto e identidad exacta del instalador");
await Rechazar(() => { Manifiestos.Leer(manifiesto, "Otro.Editor", "2.0"); return Task.CompletedTask; }, "rechazo de manifiesto de otro paquete");
await Rechazar(() => { Manifiestos.Leer(manifiesto, "Editor.Prueba", "3.0"); return Task.CompletedTask; }, "rechazo de manifiesto de otra versión");
Verificar(!Manifiestos.Leer(manifiesto + "Dependencies:\n  PackageDependencies:\n  - PackageIdentifier: Otro.Editor\n", "Editor.Prueba", "2.0").SinDependencias, "dependencias exigen revisión");
var ahora = DateTimeOffset.UtcNow;
var limpio = new InformeVirusTotal(hash, 0, 0, 65, 0, ahora, "{}");
var descarga = new DescargaVerificada("archivo.msi", hash, "msi", "machine", true, "Valid", limpio, ahora);
Verificar(PoliticaInstalacion.PuedeInstalar(descarga, ahora), "solo instalador íntegro con firma e informe habilita actualización");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Firma = "NotSigned" }, ahora), "sin firma no se habilita actualización");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { SinDependencias = false }, ahora), "dependencias no comprobadas bloquean actualización");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Informe = null }, ahora), "sin informe no se habilita actualización");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Tipo = "zip" }, ahora), "archivo comprimido requiere otra implementación");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Informe = limpio with { Maliciosos = 1 } }, ahora), "detección bloquea actualización");
Verificar(!PoliticaInstalacion.InformeAprobado(limpio with { Sospechosos = 1 }, ahora), "sospecha bloquea actualización");
Verificar(!PoliticaInstalacion.InformeAprobado(limpio with { Fecha = ahora.AddDays(-31) }, ahora), "informe antiguo exige revisión");
Verificar(!PoliticaInstalacion.InformeAprobado(limpio with { SinDeteccion = 0 }, ahora), "sin motores analizados no hay aprobación");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Consultado = ahora.AddHours(-2) }, ahora), "aprobación vencida debe renovarse");
foreach (var firmaPendiente in new[] { "NotSigned", "UnknownError", "NotSupportedFileFormat" })
{
    Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Firma = firmaPendiente }, ahora), "firma pendiente exige aceptación");
    Verificar(PoliticaInstalacion.PuedeInstalar(descarga with { Firma = firmaPendiente }, ahora, true), "aceptación permite firma pendiente");
}
foreach (var firmaBloqueada in new[] { "HashMismatch", "NotTrusted", "OtroEstado" })
    Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Firma = firmaBloqueada }, ahora, true), "aceptación no omite firma bloqueada");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Firma = "NotSigned", Informe = limpio with { Maliciosos = 1 } }, ahora, true), "aceptación no omite detecciones");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Firma = "NotSigned", Informe = null }, ahora, true), "aceptación no omite informe ausente");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Firma = "NotSigned", Consultado = ahora.AddHours(-2) }, ahora, true), "aceptación no omite vencimiento");
Verificar(PoliticaInstalacion.PuedeInstalar(descarga with { Tipo = "msix", Ruta = "terminal.msix", Firma = "UnknownError" }, ahora, true), "MSIX admite advertencia y conserva validación de Windows");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Tipo = "msix", Ruta = "terminal.exe" }, ahora, true), "MSIX rechaza extensión incorrecta");
Verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { SinDependencias = false }, ahora, true), "aceptación no omite dependencias no gestionadas");
// La desinstalación previa se distingue de dependencias y exige una aceptación separada.
var reemplazo = Manifiestos.Leer(manifiesto + "UpgradeBehavior: uninstallPrevious\n", "Editor.Prueba", "2.0");
Verificar(reemplazo.DesinstalarAnterior && reemplazo.SinDependencias, "desinstalación previa sin dependencias habilita preparación");
Verificar(Manifiestos.Leer(manifiesto.Replace("  Scope: machine", "  UpgradeBehavior: uninstallPrevious\n  Scope: machine"), "Editor.Prueba", "2.0").DesinstalarAnterior, "desinstalación declarada en instalador");
Verificar(!Manifiestos.Leer(manifiesto.Replace("  Scope: machine", "  UpgradeBehavior: install\n  Scope: machine") + "UpgradeBehavior: uninstallPrevious\n", "Editor.Prueba", "2.0").DesinstalarAnterior, "comportamiento del instalador prevalece sobre raíz");
Verificar(!datos.DesinstalarAnterior, "actualización habitual no desinstala");
Verificar(!Manifiestos.Leer(manifiesto + "UpgradeBehavior: uninstallPrevious\nDependencies:\n  PackageDependencies:\n  - PackageIdentifier: Otro.Editor\n", "Editor.Prueba", "2.0").SinDependencias, "reemplazo no omite dependencias reales");
Verificar(PoliticaInstalacion.PuedeInstalar(descarga with { DesinstalarAnterior = true }, ahora), "reemplazo admite instalador verificado");
var paqueteReemplazo = new Paquete { Nombre = "Prueba", Id = "Editor.Prueba", Instalada = "1", Disponible = "2", Descarga = descarga with { DesinstalarAnterior = true } };
var gestorReemplazo = new ServicioPaquetes(new EjecutorProcesos(), new ServicioVirusTotal(new HttpClient()));
await Rechazar(() => gestorReemplazo.ActualizarAsync(paqueteReemplazo, "ficticia", default), "sin confirmación no toca archivos ni desinstala");
var verdeOrden = new Paquete { Nombre = "Verde", Id = "Prueba.Verde", Instalada = "1", Disponible = "2", ResultadoApi = new(hash, "API", limpio, ahora) };
var rojoOrden = new Paquete { Nombre = "Rojo", Id = "Prueba.Rojo", Instalada = "1", Disponible = "2", ResultadoApi = new(hash, "API", limpio, ahora), ResultadoWeb = new(hash, "Web", limpio with { Maliciosos = 3 }, ahora) };
var pendienteOrden = new Paquete { Nombre = "Pendiente", Id = "Prueba.Pendiente", Instalada = "1", Disponible = "2" };
Verificar(new[] { rojoOrden, pendienteOrden, verdeOrden }.OrderBy(OrdenPaquetes.Grupo).SequenceEqual(new[] { verdeOrden, pendienteOrden, rojoOrden }), "ordena sin detecciones, pendientes y rojos");
Verificar(OrdenPaquetes.Detecciones(rojoOrden) == 3, "una fuente verde no oculta detecciones de otra fuente");
rojoOrden.ResultadoVtai = new(hash, "VTAI", limpio with { Maliciosos = 2 }, ahora);
Verificar(OrdenPaquetes.Detecciones(rojoOrden) == 3, "no suma detecciones duplicadas entre fuentes");
verdeOrden.Nivel = "amarillo";
Verificar(OrdenPaquetes.Grupo(verdeOrden) == 1, "advertencia de instalación conserva prioridad pendiente");
var jsonCli = "[{\"id\":\"" + hash + "\",\"attributes\":{\"last_analysis_stats\":{\"malicious\":0,\"suspicious\":0,\"undetected\":65,\"harmless\":0},\"last_analysis_date\":" + ahora.ToUnixTimeSeconds() + "}}]";
Verificar(ServicioVirusTotal.InterpretarCli(jsonCli, hash).SinDeteccion == 65, "informe de vt-cli vinculado al hash");
await Rechazar(() => { ServicioVirusTotal.InterpretarCli(jsonCli, new string('a', 64)); return Task.CompletedTask; }, "vt-cli con hash distinto se rechaza");
var atributosCli = "\"last_analysis_stats\":{\"malicious\":0,\"suspicious\":0,\"undetected\":65,\"harmless\":0},\"last_analysis_date\":" + ahora.ToUnixTimeSeconds();
var planoCli = "{\"_id\":\"" + hash + "\",\"sha256\":\"" + hash + "\"," + atributosCli + "}";
Verificar(ServicioVirusTotal.InterpretarCli(planoCli, hash).SinDeteccion == 65, "formato real plano de vt-cli");
Verificar(ServicioVirusTotal.InterpretarCli("[" + planoCli + "]", hash).SinDeteccion == 65, "formato real de vt-cli en lista");
await Rechazar(() => { ServicioVirusTotal.InterpretarCli(planoCli, new string('a', 64)); return Task.CompletedTask; }, "formato plano con hash distinto bloqueado");
await Rechazar(() => { ServicioVirusTotal.InterpretarCli(planoCli.Replace("\"_id\"", "\"otro\""), hash); return Task.CompletedTask; }, "formato plano sin identidad bloqueado");
await Rechazar(() => { ServicioVirusTotal.InterpretarCli(planoCli.Replace("\"sha256\":\"" + hash, "\"sha256\":\"" + new string('a', 64)), hash); return Task.CompletedTask; }, "identidades contradictorias bloqueadas");
if (args.Contains("--integracion-winget"))
{
    var real = await ejecutor.EjecutarAsync("winget.exe", ["list", "--upgrade-available", "--source", "winget", "--disable-interactivity"], default);
    var reales = TablaWinGet.Leer(real.Salida);
    Verificar(real.Codigo == 0 && reales.Count > 0, "lectura real de actualizaciones de WinGet");
    Console.WriteLine($"Programas interpretados: {reales.Count}. No se instaló ninguno.");
}
var proceso = await ejecutor.EjecutarAsync("dotnet", ["--version"], default);
Verificar(proceso.Codigo == 0 && !string.IsNullOrWhiteSpace(proceso.Salida), "ejecución real y captura de salida");
await Rechazar(async () => { await ejecutor.EjecutarAsync("winger-programa-inexistente", [], default); }, "herramienta ausente");
Console.WriteLine($"\n{cantidad} verificaciones correctas. No se enviaron archivos ni se consultó VirusTotal real.");

sealed class Simulador(HttpStatusCode estado, string json) : HttpMessageHandler
{
    public int Llamadas { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken cancelacion)
    {
        Llamadas++;
        if (solicitud.Method != HttpMethod.Get || solicitud.RequestUri?.Host != "www.virustotal.com" || !solicitud.Headers.Contains("x-apikey"))
            throw new Exception("Solicitud de VirusTotal incorrecta");
        return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(json) });
    }
}
