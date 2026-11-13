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
