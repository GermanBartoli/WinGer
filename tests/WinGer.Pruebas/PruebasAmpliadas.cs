using System.Net;
using System.Reflection;
using System.Text.Json;
using WinGer.Core;

static class PruebasAmpliadas
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        var ahora = DateTimeOffset.UtcNow;
        var hash = new string('a', 64);
        var carpeta = Path.Combine(Path.GetTempPath(), "WinGer-ampliadas-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        async Task Rechazar(Func<Task> accion, string nombre)
        {
            try { await accion(); }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or JsonException or OperationCanceledException or KeyNotFoundException)
            { verificar(true, nombre); return; }
            verificar(false, nombre);
        }
        string Api(object estadisticas, string? identidad = null) => JsonSerializer.Serialize(new { data = new { id = identidad ?? hash,
            attributes = new { last_analysis_stats = estadisticas, last_analysis_date = ahora.ToUnixTimeSeconds() } } });
        var json = Api(new { malicious = 0, suspicious = 0, undetected = 65, harmless = 0 });
        var limpio = new InformeVirusTotal(hash, 0, 0, 65, 0, ahora, json);
        try
        {
            // Ausencia explícita de informe: termina la espera sin confundirla con cero detecciones.
            var urlWeb = $"https://www.virustotal.com/gui/file/{hash}/detection";
            string Pagina(string url, object noEncontrado, string[] categorias) => JsonSerializer.Serialize(new { url, noEncontrado, categorias });
            verificar(LecturaInformeWeb.EsInformeAusente(Pagina(urlWeb, true, []), hash), "Item not found termina la espera web");
            verificar(!LecturaInformeWeb.EsInformeAusente(Pagina(urlWeb, false, []), hash), "página cargando no equivale a informe ausente");
            verificar(!LecturaInformeWeb.EsInformeAusente(Pagina(urlWeb, "true", []), hash), "indicador web exige booleano");
            verificar(!LecturaInformeWeb.EsInformeAusente(Pagina(urlWeb.Replace("virustotal.com", "ejemplo.com"), true, []), hash), "ausencia web exige origen oficial");
            verificar(!LecturaInformeWeb.EsInformeAusente(Pagina(urlWeb, true, []), new string('b', 64)), "ausencia web exige hash solicitado");
            verificar(!LecturaInformeWeb.EsInformeAusente(Pagina(urlWeb, true, ["malicious"]), hash), "evidencia de motores no se descarta como ausente");
            verificar(!LecturaInformeWeb.EsInformeAusente("null", hash), "web todavía vacía permanece pendiente");
            verificar(LecturaInformeWeb.Interpretar(Pagina(urlWeb, true, []), hash) is null, "sin informe no se interpreta como informe limpio");

            // Respuestas estructuradas: una estadística ausente no debe convertirse en cero.
            foreach (var incompleto in new object[] {
                new { suspicious = 0, undetected = 65, harmless = 0 },
                new { malicious = 0, undetected = 65, harmless = 0 },
                new { malicious = -1, suspicious = 0, undetected = 65, harmless = 0 },
                new { malicious = "cero", suspicious = 0, undetected = 65, harmless = 0 } })
                await Rechazar(() => { ServicioVirusTotal.Interpretar(Api(incompleto), hash); return Task.CompletedTask; }, "API rechaza estadísticas incompletas o inválidas");
            await Rechazar(() => { ServicioVirusTotal.Interpretar(Api(new { malicious = 0, suspicious = 0, undetected = 65, harmless = 0 }, new string('b', 64)), hash); return Task.CompletedTask; }, "API rechaza informe de otro hash");
            foreach (var entrada in new[] { "[]", "[{},{}]", "{}", "null", "{malformado" })
                await Rechazar(() => { ServicioVirusTotal.InterpretarCli(entrada, hash); return Task.CompletedTask; }, "CLI rechaza respuesta ambigua o malformada");

            // Cache persistente y errores: usar clientes simulados impide todo acceso a Internet.
            var almacen = new AlmacenLocal(carpeta);
            var sim = new HttpSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
            using var http = new HttpClient(sim);
            var servicio = new ServicioVirusTotal(http, almacen);
            await servicio.ConsultarAsync(hash, "clave-ficticia", default);
            await new ServicioVirusTotal(http, new(carpeta)).ConsultarAsync(hash, "clave-ficticia", default);
            verificar(sim.Llamadas == 1, "API reutiliza cache entre instancias");
            almacen.GuardarInformes(new() { [hash] = new(ahora.AddHours(2), limpio) });
            await new ServicioVirusTotal(http, almacen).ConsultarAsync(hash, "clave-ficticia", default);
            verificar(sim.Llamadas == 2, "API no reutiliza cache fechada en el futuro");
            almacen.GuardarInformes(new() { [hash] = new(ahora.AddHours(-2), limpio) });
            await new ServicioVirusTotal(http, almacen).ConsultarAsync(hash, "clave-ficticia", default);
            verificar(sim.Llamadas == 3, "API renueva cache vencida");
            var antes = sim.Llamadas;
            await Rechazar(async () => { await servicio.ConsultarAsync(hash, "", default); }, "API sin clave no inicia petición");
            verificar(sim.Llamadas == antes, "clave ausente no consume consultas");
            using (var cancelado = new CancellationTokenSource())
            {
                cancelado.Cancel();
                await Rechazar(async () => { await servicio.ConsultarAsync(hash, "clave-ficticia", cancelado.Token); }, "API respeta cancelación incluso con cache");
            }
            var sim404 = new HttpSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
            using var http404 = new HttpClient(sim404);
            var ausente = new ServicioVirusTotal(http404);
            verificar(await ausente.ConsultarAsync(hash, "ficticia", default) is null && await ausente.ConsultarAsync(hash, "ficticia", default) is null && sim404.Llamadas == 1, "ausencia de informe se reutiliza sin convertirla en aprobación");
            var esperando = new HttpSimulado(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); });
            using var httpEspera = new HttpClient(esperando);
            using (var cancelacion = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
                await Rechazar(async () => { await new ServicioVirusTotal(httpEspera).ConsultarAsync(hash, "ficticia", cancelacion.Token); }, "cancelación interrumpe una petición en curso");

            // Flujo de tarjeta completo con HTTP simulado y hash ya conocido: no ejecuta WinGet.
            var paquete = new Paquete { Id = "Editor.Prueba", Nombre = "Editor", Instalada = "1", Disponible = "2", HashPublicado = hash };
            paquete.RegistrarConsulta("VTAI", hash, limpio with { Maliciosos = 1 });
            var gestor = new ServicioPaquetes(new(), new ServicioVirusTotal(http));
            await gestor.ConsultarPublicadoAsync(paquete, "ficticia", default);
            verificar(paquete.ResultadoApi?.Informe?.SinDeteccion == 65 && paquete.ResultadoVtai?.Informe?.Maliciosos == 1, "consulta real del servicio conserva otra fuente");
            var notificaciones = new List<string?>(); paquete.PropertyChanged += (_, e) => notificaciones.Add(e.PropertyName);
            paquete.RegistrarErrorConsulta("API", "Error simulado");
            verificar(notificaciones.Contains(nameof(Paquete.LineaApi)) && notificaciones.Contains(nameof(Paquete.ColorApi)), "la interfaz recibe cambios de texto y color");
            verificar(paquete.ResultadoApi?.Informe is not null && paquete.DetallesConsultas.Contains("Error simulado"), "error visible conserva evidencia previa");
            await Rechazar(() => { paquete.RegistrarConsulta("Web", new string('b',64), limpio); return Task.CompletedTask; }, "una tarjeta rechaza otro hash");
            await Rechazar(() => { paquete.RegistrarConsulta("Desconocido", hash, limpio); return Task.CompletedTask; }, "fuente desconocida rechazada");
            verificar(new ResultadoConsulta(hash, "Web", limpio, ahora).Color == "#42C98B", "web sin detecciones se muestra en verde");
            verificar(new ResultadoConsulta(hash, "API", limpio with { Sospechosos = 1 }, ahora).Color == "#F27373", "sospechas visibles en rojo");

            // API faltantes: seis informes completos y una aplicación sin consultar.
            Paquete Tarjeta(string id) => new() { Id = id, Nombre = id, Instalada = "1", Disponible = "2", HashPublicado = hash, Seleccionado = true };
            var seis = Enumerable.Range(1, 6).Select(i => Tarjeta("Prueba." + i)).ToArray();
            foreach (var completa in seis) completa.RegistrarConsulta("API", hash, limpio);
            var sinConsultar = Tarjeta("Prueba.Faltante");
            var lote = seis.Append(sinConsultar).ToArray();
            var faltantes = lote.Where(p => p.NecesitaConsultaApi()).ToArray();
            verificar(faltantes.Length == 1 && ReferenceEquals(faltantes[0], sinConsultar), "API faltantes omite seis informes y selecciona solo el pendiente");
            var apiFaltantes = new HttpSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
            using var httpFaltantes = new HttpClient(apiFaltantes);
            var gestorFaltantes = new ServicioPaquetes(new(), new ServicioVirusTotal(httpFaltantes));
            foreach (var pendiente in faltantes) await gestorFaltantes.ConsultarPublicadoAsync(pendiente, "ficticia", default);
            verificar(apiFaltantes.Llamadas == 1 && lote.All(p => !p.NecesitaConsultaApi()), "API faltantes consulta una vez y una segunda pasada no consulta ninguna");
            var caso = Tarjeta("Prueba.Caso");
            caso.RegistrarConsulta("Web", hash, limpio); caso.RegistrarConsulta("VTAI", hash, limpio);
            verificar(caso.NecesitaConsultaApi(), "Web y VTAI completos no sustituyen un informe API");
            caso.RegistrarConsulta("API", hash, null);
            verificar(caso.NecesitaConsultaApi(), "API sin informe sigue pendiente");
            caso.RegistrarConsulta("API", hash, limpio);
            caso.RegistrarErrorConsulta("API", "Error simulado");
            verificar(caso.NecesitaConsultaApi(), "API con último intento fallido se reintenta");
            caso.ResultadoApi = new(hash, "vt-cli", limpio, ahora.AddDays(-2));
            verificar(!caso.NecesitaConsultaApi(), "API faltantes conserva informes antiguos y de vt-cli sin renovarlos");
            caso.ResultadoApi = new(hash, "API", limpio with { Maliciosos = 1 }, ahora);
            verificar(!caso.NecesitaConsultaApi() && caso.ColorApi == "#F27373", "informe con detecciones se conserva y permanece rojo");
            caso.ResultadoApi = new(new string('b', 64), "API", limpio, ahora);
            verificar(caso.NecesitaConsultaApi(), "API de otro hash no cuenta como completa");
            caso.ResultadoApi = new(hash, "API", limpio with { Hash = new string('b', 64) }, ahora);
            verificar(caso.NecesitaConsultaApi(), "evidencia API de otro hash no cuenta como completa");
            caso.ResultadoApi = new(hash, "API", limpio, ahora);
            caso.HashPublicado = new string('b', 64);
            verificar(caso.NecesitaConsultaApi(), "cambiar hash exige una nueva consulta API");
            verificar(new ResultadoConsulta(hash, "Web", limpio, ahora).Resumen.Contains("lectura informativa"), "Web mantiene el carácter informativo aun en verde");
            verificar(!new ResultadoConsulta(hash, "API", limpio, ahora).Resumen.Contains("lectura informativa"), "aclaración Web no modifica el renglón API");

            var lectura = new ControlLecturaWeb();
            verificar(lectura.PasarAutomaticamente && !lectura.DebeAvanzar, "paso web activado por defecto espera el resultado");
            lectura.PasarAutomaticamente = false;
            lectura.RegistrarResultado();
            verificar(lectura.ResultadoDisponible && !lectura.DebeAvanzar, "pausa conserva el resultado sin avanzar");
            lectura.PasarAutomaticamente = true;
            verificar(lectura.DebeAvanzar, "reactivar paso automático permite continuar con resultado guardado");
            lectura.PasarAutomaticamente = false;
            verificar(!lectura.DebeAvanzar, "desmarcar vuelve a frenar aun con resultado disponible");
            verificar(new Configuracion().PasarWebAutomaticamente, "preferencia de paso automático comienza marcada");
            almacen.Guardar(new Configuracion { PasarWebAutomaticamente = false });
            verificar(!new AlmacenLocal(carpeta).LeerConfiguracion().PasarWebAutomaticamente, "preferencia de pausa se conserva entre aperturas");
            verificar(new ResultadoConsulta(hash, "Web", null, ahora).Color == "#E8B44D", "web sin informe sigue amarillo");
            verificar(new ResultadoConsulta(hash, "Web", limpio with { Maliciosos = 1 }, ahora).Color == "#F27373", "web con detecciones sigue rojo");
            verificar(new ResultadoConsulta(hash, "Web", limpio with { Sospechosos = 1 }, ahora).Color == "#F27373", "web con sospechas sigue rojo");
            verificar(new ResultadoConsulta(hash, "Web", limpio, ahora, "Error").Color == "#E8B44D", "web con error no se pinta verde");
            verificar(new ResultadoConsulta(hash, "Web", limpio with { SinDeteccion = 0 }, ahora).Color == "#E8B44D", "web sin motores no se pinta verde");

            var posicion = new UbicacionVentana(110, 105, 1250, 880);
            verificar(posicion.Ajustar(0, 0, 1920, 1040) == posicion, "navegador conserva posición y tamaño elegidos");
            var movida = posicion.Registrar(220, 150, 1000, 700, false, false);
            verificar(movida == new UbicacionVentana(220, 150, 1000, 700), "mover y redimensionar actualiza la posición guardada");
            var maximizada = movida.Registrar(0, 0, 1920, 1040, true, false);
            verificar(maximizada == movida with { Maximizada = true }, "maximizar conserva el tamaño normal y guarda el estado");
            verificar(maximizada.Registrar(-32000, -32000, 0, 0, false, true) == maximizada, "minimizar no guarda coordenadas invisibles");
            verificar(maximizada.Registrar(220, 150, 1000, 700, false, false) == movida, "restaurar recupera el rectángulo normal");
            var secundaria = new UbicacionVentana(-1800, 100, 1000, 700);
            verificar(secundaria.Ajustar(-1920, 0, 1920, 1040) == secundaria, "monitor izquierdo conserva coordenadas negativas válidas");
            verificar(secundaria.Ajustar(0, 0, 1920, 1040).X == 0, "monitor desconectado devuelve la ventana a una pantalla visible");
            verificar(new UbicacionVentana(1800, 900, 2000, 1500).Ajustar(0, 0, 1280, 720) == new UbicacionVentana(0, 0, 1280, 720), "pantalla menor adapta posición y tamaño");
            almacen.Guardar(new Configuracion { UbicacionWeb = maximizada });
            verificar(new AlmacenLocal(carpeta).LeerConfiguracion().UbicacionWeb == maximizada, "posición, tamaño y maximizado persisten entre reinicios");
            verificar(new Configuracion().UbicacionWeb is null, "configuraciones anteriores permiten ubicación inicial por defecto");

            // Registro VTAI y límites: peticiones interceptadas, sin crear identidades reales.
            var registro = new HttpSimulado(async (solicitud, token) =>
            {
                verificar(solicitud.Method == HttpMethod.Post && solicitud.RequestUri?.AbsolutePath == "/api/v3/agents/register", "registro VTAI usa el endpoint esperado");
                verificar((await solicitud.Content!.ReadAsStringAsync(token)).Contains("WinGer"), "registro identifica la aplicación");
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"agent_token\":\"vtai_simulado\"}") };
            });
            using var httpRegistro = new HttpClient(registro);
            verificar(await new ServicioVirusTotalAi(httpRegistro, almacen).RegistrarAsync(default) == "vtai_simulado", "registro VTAI interpreta el token simulado");
            foreach (var respuestaRegistro in new[] { "{\"agent_token\":\"otro\"}", "{\"agent_token\":\"\"}" })
            {
                using var invalido = new HttpClient(new HttpSimulado((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respuestaRegistro) })));
                await Rechazar(async () => { await new ServicioVirusTotalAi(invalido, almacen).RegistrarAsync(default); }, "registro VTAI rechaza credencial inválida");
            }
            var limite = new HttpSimulado((_, _) =>
            {
                var respuesta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                respuesta.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
                return Task.FromResult(respuesta);
            });
            using var httpLimite = new HttpClient(limite);
            var vtai = new ServicioVirusTotalAi(httpLimite, almacen);
            await Rechazar(async () => { await vtai.ConsultarAsync(hash, "vtai_simulado", default); }, "VTAI informa cuota agotada");
            using (var cancelarEspera = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
                await Rechazar(async () => { await vtai.ConsultarAsync(hash, "vtai_simulado", cancelarEspera.Token); }, "espera Retry-After es cancelable");
            verificar(limite.Llamadas == 1, "VTAI no repite peticiones durante Retry-After");

            // Bloqueos anteriores a la instalación: los archivos son texto de prueba, nunca se ejecutan.
            await Rechazar(async () => { await gestor.ActualizarAsync(paquete, "ficticia", default); }, "actualización sin descarga comprobada se rechaza");
            var archivoModificado = Path.Combine(carpeta, "archivo.exe");
            await File.WriteAllTextAsync(archivoModificado, "texto de prueba, no ejecutable");
            paquete.Descarga = new(archivoModificado, hash, "exe", "user", true, "Valid", limpio, ahora);
            var llamadasAntes = sim.Llamadas;
            await Rechazar(async () => { await gestor.ActualizarAsync(paquete, "ficticia", default); }, "archivo modificado bloqueado antes de firma e instalación");
            verificar(sim.Llamadas == llamadasAntes && paquete.Descarga is null && paquete.Estado == "Actualización detenida", "fallo de integridad limpia autorización y no consulta red");

            var panel = new WinGer.PanelViewModel();
            var cambiosPanel = new List<string?>(); panel.PropertyChanged += (_, e) => cambiosPanel.Add(e.PropertyName);
            panel.Ocupado = true; panel.Estado = "Prueba";
            verificar(!panel.Disponible && cambiosPanel.Contains("Disponible") && cambiosPanel.Contains("Estado"), "modelo de interfaz notifica ocupación y estado");
            panel.Ocupado = false;
            verificar(panel.Disponible, "modelo vuelve a habilitar acciones al finalizar");

            // Matriz de instaladores, fechas y motivos sin ejecutar ningún instalador.
            foreach (var (tipo, extension) in new[] { ("msi",".msi"), ("wix",".msi"), ("exe",".exe"), ("inno",".exe"), ("nullsoft",".exe"), ("burn",".exe"), ("msix",".msixbundle"), ("appx",".appxbundle") })
            {
                var descarga = new DescargaVerificada("archivo" + extension, hash, tipo, "user", true, "Valid", limpio, ahora);
                verificar(PoliticaInstalacion.PuedeInstalar(descarga, ahora), "formato admitido: " + tipo);
                verificar(!PoliticaInstalacion.PuedeInstalar(descarga with { Ruta = "archivo.zip" }, ahora, true), "extensión incompatible bloqueada: " + tipo);
            }
            foreach (var variante in new[] { limpio with { Fecha = ahora.AddMinutes(6) }, limpio with { Fecha = null }, limpio with { Inofensivos = 0, SinDeteccion = 0 }, limpio with { Maliciosos = 2 }, limpio with { Fecha = ahora.AddDays(-31) } })
                verificar(!PoliticaInstalacion.InformeAprobado(variante, ahora), "informe insuficiente o fuera de fecha bloqueado");
            var manifiesto = $"PackageIdentifier: Editor.Prueba\nPackageVersion: 2\nInstallerType: msix\nInstallers:\n- Architecture: x64\n  InstallerSha256: {hash}\n";
            verificar(Manifiestos.Leer(manifiesto, "Editor.Prueba", "2").Tipo == "msix", "manifiesto hereda formato de la raíz");
            verificar(!Manifiestos.Leer(manifiesto + "UpgradeBehavior: uninstallPrevious\n", "Editor.Prueba", "2").SinDependencias, "desinstalación previa no se omite en MSIX");
            await Rechazar(() => { Manifiestos.Leer(manifiesto + $"- Architecture: x86\n  InstallerSha256: {hash}\n", "Editor.Prueba", "2"); return Task.CompletedTask; }, "manifiesto con varios instaladores rechazado");
            await Rechazar(() => { Manifiestos.Leer(manifiesto.Replace(hash, "invalido"), "Editor.Prueba", "2"); return Task.CompletedTask; }, "hash de manifiesto inválido rechazado");
            foreach (var categorias in new[] { new[] { "failure", "timeout" }, Array.Empty<string>(), new[] { "otra" } })
            {
                var web = JsonSerializer.Serialize(new { url = $"https://www.virustotal.com/gui/file/{hash}/detection", hash, categorias, fecha = ahora.ToString("O") });
                verificar(LecturaInformeWeb.Interpretar(web, hash) is null, "web sin motores utilizables queda pendiente");
            }
            foreach (var archivo in new[] { "historial.json", "cache-informes.json", "cache-vtai.json" })
                await File.WriteAllTextAsync(Path.Combine(carpeta, archivo), "{roto");
            verificar(almacen.LeerHistorial().Count == 0 && almacen.LeerInformes().Count == 0 && almacen.LeerInformesVtai().Count == 0, "recuperación de archivos locales dañados");

            // Proceso hijo del propio runner: no usa ventanas, credenciales ni programas instalables.
            var ejecutor = new EjecutorProcesos();
            var ensamblado = Assembly.GetExecutingAssembly().Location;
            var salida = await ejecutor.EjecutarAsync("dotnet", [ensamblado, "--hijo-salida"], default, "secreto-ficticio");
            verificar(salida.Codigo == 7 && salida.Salida.Contains("salida") && salida.Salida.Contains("error"), "captura stdout, stderr y código de salida");
            verificar(!salida.Salida.Contains("secreto-ficticio") && salida.Salida.Contains("[clave protegida]"), "el ejecutor oculta credenciales en salida");
            using (var cancelarHijo = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                await Rechazar(async () => { await ejecutor.EjecutarAsync("dotnet", [ensamblado, "--hijo-espera"], cancelarHijo.Token); }, "cancelación termina el proceso auxiliar");
        }
        finally { Directory.Delete(carpeta, true); }
    }
}

sealed class HttpSimulado(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    public int Llamadas { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken token)
    { Llamadas++; return responder(solicitud, token); }
}
