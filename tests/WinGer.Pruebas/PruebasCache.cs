using System.Net;
using System.Text.Json;
using WinGer.Core;

static class PruebasCache
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "WinGer-cache-" + Guid.NewGuid());
        const string hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        const string json = """{"data":{"id":"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","last_analysis_stats":{"malicious":0,"suspicious":0,"undetected":60,"harmless":0},"analysis_date":"2026-10-27T17:38:37Z"}}""";
        try
        {
            var almacen = new AlmacenLocal(carpeta);
            var fecha = DateTimeOffset.UtcNow;
            almacen.Guardar(new Configuracion { Tema = "Claro" }); almacen.Registrar("Prueba", "Guardada");
            almacen.GuardarCache(new CacheSesion { UltimaBusqueda = fecha, Filtro = "editor", Hash = hash, SalidaVirus = "informe",
                Paquetes = [new Paquete { Id = "Editor.Prueba", Nombre = "Editor", Instalada = "1", Disponible = "2", Seleccionado = true,
                    HashPublicado = hash, Estado = "VTAI · 1 detección", Nivel = "rojo", Detalle = "Detalle persistido", Descarga = new("archivo.exe", hash, "exe", "user", true, "Valid", null, fecha) }] });
            var restaurada = new AlmacenLocal(carpeta).LeerCache();
            verificar(restaurada.UltimaBusqueda == fecha && restaurada.Filtro == "editor" && restaurada.Hash == hash && restaurada.SalidaVirus == "informe", "sesión completa restaurada desde otra instancia");
            verificar(restaurada.Paquetes.Single() is { Seleccionado: true, Disponible: "2", Estado: "VTAI · 1 detección", Nivel: "rojo", Detalle: "Detalle persistido", Descarga: null }, "paquetes y selección persisten sin autorización de instalación");
            var paquete = restaurada.Paquetes.Single();
            var informeFuente = new InformeVirusTotal(hash, 0, 0, 60, 0, fecha, "{}");
            paquete.RegistrarConsulta("API", hash, informeFuente);
            var apiOriginal = paquete.ResultadoApi;
            paquete.RegistrarConsulta("VTAI", hash, informeFuente with { Maliciosos = 1 });
            paquete.RegistrarConsulta("Web", hash, informeFuente with { Sospechosos = 2 });
            verificar(paquete.ResultadoApi == apiOriginal && paquete.ResultadoVtai?.Informe?.Maliciosos == 1 && paquete.ResultadoWeb?.Informe?.Sospechosos == 2, "resultados de tres fuentes independientes");
            paquete.RegistrarErrorConsulta("API", "Cuota agotada");
            verificar(paquete.ResultadoApi?.Informe == informeFuente && paquete.ResultadoApi.Consultado == apiOriginal!.Consultado && paquete.ResultadoApi.Error == "Cuota agotada", "un error conserva la evidencia anterior y su fecha");
            var renovado = new Paquete { Id = paquete.Id, Nombre = paquete.Nombre, Instalada = paquete.Instalada, Disponible = paquete.Disponible, HashPublicado = hash };
            renovado.ConservarConsultas(paquete);
            verificar(renovado.ResultadoApi == paquete.ResultadoApi && renovado.ResultadoWeb == paquete.ResultadoWeb, "buscar actualizaciones conserva resultados por versión");
            var nuevaVersion = new Paquete { Id = paquete.Id, Nombre = paquete.Nombre, Instalada = "1", Disponible = "3" };
            nuevaVersion.ConservarConsultas(paquete);
            verificar(nuevaVersion.ResultadoApi is null && nuevaVersion.LineaWeb == "Web: N/a", "otra versión no hereda resultados anteriores");
            almacen.GuardarCache(new CacheSesion { Paquetes = [renovado] });
            var persistido = new AlmacenLocal(carpeta).LeerCache().Paquetes.Single();
            verificar(persistido.ResultadoApi == renovado.ResultadoApi && persistido.ResultadoVtai == renovado.ResultadoVtai && persistido.ResultadoWeb == renovado.ResultadoWeb, "las tres fuentes sobreviven al reinicio");
            persistido.HashPublicado = new string('c', 64);
            verificar(persistido.ResultadoApi is null && persistido.ResultadoVtai is null && persistido.ResultadoWeb is null, "un hash diferente invalida las tres fuentes");
            paquete.RegistrarConsulta("vt-cli", hash, informeFuente);
            verificar(paquete.ResultadoApi?.Metodo == "vt-cli" && paquete.ResultadoWeb?.Informe?.Sospechosos == 2, "vt-cli comparte fila API sin borrar Web");
            var simulador = new RespuestasVtai(HttpStatusCode.OK, json);
            using var cliente = new HttpClient(simulador);
            await new ServicioVirusTotalAi(cliente, almacen).ConsultarAsync(hash, "vtai_prueba", default);
            await new ServicioVirusTotalAi(cliente, new(carpeta)).ConsultarAsync(hash, "vtai_prueba", default);
            verificar(simulador.Llamadas == 1, "VTAI reutiliza informe entre instancias");
            almacen.GuardarInformesVtai(new() { [hash] = new(fecha.AddHours(-2), ServicioVirusTotalAi.Interpretar(json, hash)) });
            await new ServicioVirusTotalAi(cliente, almacen).ConsultarAsync(hash, "vtai_prueba", default);
            verificar(simulador.Llamadas == 2, "VTAI renueva informe vencido");
            almacen.BorrarCache();
            verificar(almacen.LeerCache().Paquetes.Count == 0 && almacen.LeerInformesVtai().Count == 0 && almacen.LeerInformes().Count == 0, "borrado completo de cachés");
            verificar(almacen.LeerConfiguracion().Tema == "Claro" && almacen.LeerHistorial().Count == 1, "borrar caché conserva ajustes e historial");
            File.WriteAllText(Path.Combine(carpeta, "cache-sesion.json"), "{roto");
            verificar(new AlmacenLocal(carpeta).LeerCache().Paquetes.Count == 0, "caché dañada recuperable");
            foreach (var codigo in new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
            {
                almacen.BorrarCache();
                using var otro = new HttpClient(new RespuestasVtai(codigo, "{}"));
                var rechazado = false;
                try { verificar(await new ServicioVirusTotalAi(otro, almacen).ConsultarAsync(hash, "vtai_prueba", default) is null && codigo == HttpStatusCode.NotFound, "VTAI sin informe permanece desconocido"); }
                catch (InvalidOperationException) { rechazado = true; }
                if (codigo != HttpStatusCode.NotFound) verificar(rechazado && almacen.LeerInformesVtai().Count == 0, "VTAI rechaza y no almacena HTTP " + (int)codigo);
            }
            foreach (var previo in new InformeVirusTotal?[] { null, informeFuente with { Maliciosos = 9 } })
            {
                almacen.GuardarInformesVtai(new() { [hash] = new(fecha, previo) });
                using var remotoVtai = new HttpClient(new RespuestasVtai(HttpStatusCode.OK, json));
                var servicioVtai = new ServicioVirusTotalAi(remotoVtai, almacen);
                verificar(await servicioVtai.ConsultarAsync(hash, "vtai_prueba", default) == previo, "consulta automática VTAI conserva caché");
                verificar((await servicioVtai.ConsultarAsync(hash, "vtai_prueba", default, forzarConsulta: true))?.Maliciosos == 0, "consulta manual VTAI reemplaza informe o ausencia guardados");
                verificar(almacen.LeerInformesVtai()[hash].Informe?.Maliciosos == 0, "VTAI persiste informe renovado");
                almacen.GuardarInformes(new() { [hash] = new(fecha, previo) });
                var jsonApi = """{"data":{"attributes":{"last_analysis_stats":{"malicious":0,"suspicious":0,"undetected":60,"harmless":0}}}}""";
                using var remotoApi = new HttpClient(new Simulador(HttpStatusCode.OK, jsonApi));
                var servicioApi = new ServicioVirusTotal(remotoApi, almacen);
                verificar(await servicioApi.ConsultarAsync(hash, "prueba", default) == previo, "consulta automática API conserva caché");
                verificar((await servicioApi.ConsultarAsync(hash, "prueba", default, forzarConsulta: true))?.Maliciosos == 0, "consulta manual API reemplaza informe o ausencia guardados");
                verificar(almacen.LeerInformes()[hash].Informe?.Maliciosos == 0, "API persiste informe renovado");
            }
            var finalizado = new Paquete { Id = "Prueba.App", Nombre = "Prueba", Instalada = "1", Disponible = "2", Estado = "Guardado · Actualizado · 2 · con advertencia de firma" };
            var pendiente = new Paquete { Id = "Pendiente.App", Nombre = "Pendiente", Instalada = "1", Disponible = "2", Seleccionado = true, Estado = "Instalador finalizado · versión pendiente de confirmar" };
            almacen.RegistrarActualizacion(pendiente);
            verificar(almacen.LeerActualizaciones().Count == 0, "una instalación sin versión confirmada no entra en mis actualizaciones");
            almacen.RegistrarActualizacion(finalizado, fecha);
            almacen.RegistrarActualizacion(finalizado, fecha);
            var actualizaciones = new AlmacenLocal(carpeta).LeerActualizaciones();
            verificar(actualizaciones.Count == 1 && actualizaciones[0].Fecha == fecha && actualizaciones[0].Anterior == "1" && actualizaciones[0].Version == "2", "historial persistente con versiones, fecha y migración sin duplicados");
            var lista = new List<Paquete> { finalizado, pendiente };
            lista.RemoveAll(p => p.ActualizacionConfirmada);
            verificar(lista.Count == 1 && lista[0].Seleccionado, "retirar confirmadas conserva pendientes seleccionadas");
            almacen.BorrarCache();
            verificar(almacen.LeerActualizaciones().Count == 1, "borrar caché conserva mis actualizaciones");
            var antiguo = new ResultadoConsulta(hash, "API", informeFuente with { Fecha = fecha.AddDays(-31) }, fecha);
            verificar(antiguo.Resumen.Contains("Análisis antiguo · más de 30 días") && antiguo.Color == "#E8B44D", "el amarillo explica que el análisis supera 30 días");
            var sinInforme = new Paquete { Id = "Sin.Informe", Nombre = "Sin informe", Instalada = "1", Disponible = "2", HashPublicado = hash };
            verificar(!sinInforme.PuedePrepararSubida, "sin consultar no se ofrece subir por ausencia supuesta");
            sinInforme.RegistrarErrorConsulta("API", "Cuota");
            verificar(!sinInforme.PuedePrepararSubida, "un error de API no significa informe ausente");
            sinInforme.RegistrarConsulta("Web", hash, null);
            verificar(sinInforme.PuedePrepararSubida, "ausencia explícita permite preparar subida");
            sinInforme.RegistrarConsulta("VTAI", hash, informeFuente);
            verificar(!sinInforme.PuedePrepararSubida, "un informe existente evita ofrecer subida por otro resultado antiguo");
            var web = JsonSerializer.Serialize(new { url = $"https://www.virustotal.com/gui/file/{hash}/detection", hash, categorias = new[] { "undetected", "malicious", "type-unsupported" }, fecha = "2026-10-27 17:38:37 UTC" });
            verificar(LecturaInformeWeb.Interpretar(web, hash) is { Maliciosos: 1, SinDeteccion: 1, Fecha: not null }, "lectura web conserva detecciones y fecha");
            verificar(LecturaInformeWeb.Interpretar(web, new string('a', 64)) is null, "web rechaza otro hash");
            verificar(LecturaInformeWeb.Interpretar("null", hash) is null && LecturaInformeWeb.Interpretar(web.Replace("malicious", "desconocido"), hash) is null, "web no aprueba páginas incompletas o cambiadas");
            verificar(LecturaInformeWeb.Interpretar(web.Replace("www.virustotal.com", "otro.example"), hash) is null, "web rechaza otro origen");
        }
        finally { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true); }
    }
}

sealed class RespuestasVtai(HttpStatusCode codigo, string json) : HttpMessageHandler
{
    public int Llamadas { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken token)
    {
        if (solicitud.RequestUri?.Host != "ai.virustotal.com" || solicitud.Method != HttpMethod.Get || solicitud.Headers.Authorization?.Scheme != "Bearer" || solicitud.Headers.Contains("x-apikey"))
            throw new Exception("Autenticación VTAI incorrecta.");
        Llamadas++;
        return Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent(json) });
    }
}
