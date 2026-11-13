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
