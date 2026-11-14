using System.Text;
using WinGer.Core;

static class PruebasCatalogo
{
    public static async Task Ejecutar(Action<bool, string> verificar, bool integracion)
    {
        foreach (var cabecera in new[] { $"{ "Nombre",-30}{"Id",-36}{"Versión",-18}Coincidencia", $"{ "Name",-30}{"Id",-36}{"Version",-18}Match" })
        {
            var tabla = cabecera + "\n" + new string('-', 105) + "\n" + $"{"7-Zip",-30}{"7zip.7zip",-36}{"26.06",-18}Moniker: 7zip\n";
            var resultados = CatalogoAplicaciones.LeerBusqueda(tabla);
            verificar(resultados.Count == 1 && resultados[0].Id == "7zip.7zip" && resultados[0].Version == "26.06", "catálogo interpreta búsqueda localizada y columna de coincidencias");
            verificar(CatalogoAplicaciones.LeerBusqueda(tabla.Replace("7zip.7zip", "7zip.Paqu…")).Count == 0, "catálogo descarta identificadores truncados");
        }
        verificar(CatalogoAplicaciones.LeerBusqueda("No se encontró ningún paquete.").Count == 0, "catálogo sin resultados no inventa aplicaciones");
        verificar(CatalogoAplicaciones.LeerVersion("Encontrado Prueba\nVersión: 2.3.4\nEditor: Prueba") == "2.3.4", "versión de descarga en español");
        verificar(CatalogoAplicaciones.LeerVersion("Found App\nVersion: 2.3.4\nPublisher: App") == "2.3.4", "versión de descarga en inglés");
        var rechazo = false; try { CatalogoAplicaciones.LeerVersion("Versiones disponibles: 1, 2"); } catch (InvalidOperationException) { rechazo = true; }
        verificar(rechazo, "descarga exige una versión concreta");
        verificar(CatalogoAplicaciones.Populares.Select(p => p.Id).Distinct().Count() == CatalogoAplicaciones.Populares.Count, "selección popular no duplica identificadores");
        verificar(CatalogoAplicaciones.Populares.All(p => Uri.TryCreate(p.Sitio, UriKind.Absolute, out var uri) && uri.Scheme == "https"), "sitios de iconos usan HTTPS");
        foreach (var comando in ComandosReparacion.Todos)
        {
            var inicio = ComandosReparacion.CrearInicio(comando);
            var script = Encoding.Unicode.GetString(Convert.FromBase64String(inicio.ArgumentList.Last()));
            verificar(Path.IsPathFullyQualified(inicio.FileName) && inicio.UseShellExecute && inicio.Verb == (comando.Administrador ? "runas" : "open"), "lanzador usa ruta del sistema y elevación prevista: " + comando.Nombre);
            verificar(script.Contains(comando.Argumentos) && script.Contains("$LASTEXITCODE") && script.Contains("Read-Host"), "consola conserva resultado y código: " + comando.Nombre);
        }
        rechazo = false;
        try { ComandosReparacion.CrearInicio(ComandosReparacion.Todos[0] with { Argumentos = "/Online; comando-inyectado" }); } catch (InvalidOperationException) { rechazo = true; }
        verificar(rechazo, "lanzador rechaza comandos alterados antes de iniciar procesos");
        if (integracion)
        {
            var procesos = new EjecutorProcesos();
            var respuesta = await procesos.EjecutarAsync("winget.exe", ["search", "--query", "7zip.7zip", "--count", "5", "--source", "winget", "--disable-interactivity"], default);
            verificar(respuesta.Codigo == 0 && CatalogoAplicaciones.LeerBusqueda(respuesta.Salida).Any(p => p.Id == "7zip.7zip"), "búsqueda real en WinGet sin descargar ni instalar");
        }
    }
}
