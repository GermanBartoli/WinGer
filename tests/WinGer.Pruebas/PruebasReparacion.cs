using WinGer.Core;

static class PruebasReparacion
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        var comando = ComandosReparacion.Todos[0]; var ahora = DateTimeOffset.Now;
        verificar(ComandosReparacion.Todos.Count(c => ResultadosReparacion.Seccion(c) == "Análisis y comprobaciones") == 5 && ComandosReparacion.Todos.Count(c => ResultadosReparacion.Seccion(c) == "Reparación y mantenimiento") == 3 && ComandosReparacion.Todos.Count(c => ResultadosReparacion.Seccion(c) == "Información del equipo") == 2, "todas las tarjetas pertenecen a una sección visible");
        foreach (var (codigo, salida, nivel) in new (int?, string, string)[] {
            (0, "No component store corruption detected. The operation completed successfully.", "verde"),
            (0, "The component store is repairable.", "amarillo"), (0, "Salida desconocida", "amarillo"),
            (0, "Windows Resource Protection found corrupt files but was unable to fix some of them.", "rojo"),
            (0, "Windows Resource Protection successfully repaired them.", "verde"),
            (87, "Error", "rojo"), (3010, "Completado", "amarillo"), (null, "", "amarillo") })
            verificar(ResultadosReparacion.Interpretar(comando, ahora, ahora, codigo, salida).Nivel == nivel, "clasifica resultado de reparación: " + salida);
        var carpeta = Path.Combine(Path.GetTempPath(), "WinGer-reparacion-" + Guid.NewGuid());
        try
        {
            var almacen = new AlmacenLocal(carpeta);
            almacen.Guardar(new Configuracion { Tema = "Oscuro" });
            almacen.GuardarReparacion(comando, ResultadosReparacion.Interpretar(comando, ahora, ahora.AddSeconds(5), 0, "No component store corruption detected."));
            almacen.Registrar("Prueba", "Correcta"); almacen.GuardarCache(new CacheSesion { Filtro = "prueba" });
            var backup = Path.Combine(carpeta, "backup.json"); almacen.CrearBackup(backup);
            almacen.BorrarCache();
            verificar(almacen.LeerReparaciones().Count == 1, "borrar caché conserva resultados de reparación");
            var destino = new AlmacenLocal(Path.Combine(carpeta, "restaurado")); destino.ImportarBackup(backup);
            verificar(destino.LeerConfiguracion().Tema == "Oscuro" && destino.LeerCache().Filtro == "prueba" && destino.LeerHistorial().Count == 1 && destino.LeerReparaciones()[comando.Comando].Nivel == "verde", "backup único restaura ajustes, caché e historial");
            File.WriteAllText(backup, "{}");
            var rechazo = false; try { destino.ImportarBackup(backup); } catch (System.Text.Json.JsonException) { rechazo = true; }
            verificar(rechazo && destino.LeerConfiguracion().Tema == "Oscuro", "backup inválido no altera datos existentes");
            var archivo = Path.Combine(carpeta, "resultado.json");
            var inicio = ComandosReparacion.CrearInicio(ComandosReparacion.Todos.Single(c => c.Argumentos == "/all"), archivo);
            inicio.UseShellExecute = false; inicio.Verb = ""; inicio.CreateNoWindow = true; inicio.RedirectStandardInput = true; inicio.RedirectStandardOutput = true; inicio.RedirectStandardError = true;
            using var proceso = System.Diagnostics.Process.Start(inicio)!;
            var salida = proceso.StandardOutput.ReadToEndAsync(); var error = proceso.StandardError.ReadToEndAsync();
            await proceso.StandardInput.WriteLineAsync(); proceso.StandardInput.Close();
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await proceso.WaitForExitAsync(limite.Token);
            await Task.WhenAll(salida, error);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(archivo));
            verificar(json.RootElement.GetProperty("Codigo").GetInt32() == 0 && json.RootElement.GetProperty("Salida").GetString()!.Length > 20, "captura real de ipconfig con código y salida sin modificar Windows");
        }
        finally { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true); }
    }
}
