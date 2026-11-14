using WinGer.Core;

static class PruebasReparacion
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        var comando = ComandosReparacion.Todos[0]; var ahora = DateTimeOffset.Now;
        var sfc = ComandosReparacion.Todos.Single(c => c.Argumentos == "/verifyonly");
        var chkdsk = ComandosReparacion.Todos.Single(c => c.Programa == "chkdsk.exe");
        verificar(ResultadosReparacion.Interpretar(chkdsk, ahora, ahora, 9, "Ya se estß ejecutando un examen en esta unidad.").Nivel == "amarillo", "CHKDSK diferencia un examen simultáneo de un fallo del disco");
        verificar(ResultadosReparacion.Interpretar(chkdsk, ahora, ahora, 0, "Se examinó el sistema de archivos sin encontrar problemas.\r\nNo se requieren más acciones.").Nivel == "verde", "CHKDSK reconoce el resultado correcto de Windows en español");
        var antiguo = "Iniciando examen. Este proceso tardarß alg·n tiempo.\r\n\r\nSe complet¾ la comprobaci¾n de 1%.\r\nSe complet¾ la comprobaci¾n de 1%.\r\nSe complet¾ la comprobaci¾n de 100%.\r\nProtecci¾n de recursos de Windows no encontr¾ ninguna infracci¾n\r\n\r\nde integridad.";
        var corregido = ResultadosReparacion.Interpretar(sfc, ahora, ahora, 0, antiguo);
        verificar(corregido.Nivel == "verde" && corregido.Salida.Contains("tardará algún") && corregido.Salida.Contains("100%") && !corregido.Salida.Contains("1%") && !corregido.Salida.Contains('¾'), "SFC histórico recupera acentos, compacta progreso y reconoce integridad entre líneas");
        var dism = ComandosReparacion.Todos.Single(c => c.Argumentos.Contains("AnalyzeComponentStore"));
        verificar(ResultadosReparacion.Interpretar(dism, ahora, ahora, 0, "Component Store Cleanup Recommended : Yes\nThe operation completed successfully.").Mensaje.Contains("liberar espacio"), "DISM explica limpieza recomendada sin confundirla con corrupción");
        verificar(ResultadosReparacion.Interpretar(dism, ahora, ahora, 0, "Component Store Cleanup Recommended : No").Nivel == "verde", "DISM sin limpieza recomendada queda verde");
        verificar(ResultadosReparacion.LimpiarSalida("[===97.9%=== ]\r[===97.9%=== ]\r[===100.0%===]\nResultado correcto").Split(Environment.NewLine).Length == 2, "DISM conserva último progreso y resultado final");
        verificar(ResultadosReparacion.Interpretar(sfc, ahora, ahora, 5, "Error. Reiniciar no es necesario.").Nivel == "rojo", "mencionar reiniciar no oculta un código de error");
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
            // Simula salida UTF-16 de SFC sin ejecutar reparaciones en el equipo.
            var automatico = ComandosReparacion.CrearInicio(sfc, archivo, true);
            var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(automatico.ArgumentList.Last()));
            var hijo = "[Console]::OutputEncoding=[Text.Encoding]::Unicode; [Console]::WriteLine('Comprobación: áéíóú ñ'); [Console]::Error.WriteLine('Error de prueba'); exit 5";
            script = script.Replace(Path.Combine(Environment.SystemDirectory, "sfc.exe"), automatico.FileName).Replace("'/verifyonly'", "'-NoProfile -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(hijo)) + "'");
            automatico.ArgumentList[^1] = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            automatico.UseShellExecute = false; automatico.Verb = ""; automatico.CreateNoWindow = true;
            automatico.RedirectStandardInput = true; automatico.RedirectStandardOutput = true; automatico.RedirectStandardError = true;
            using var simulacion = System.Diagnostics.Process.Start(automatico)!;
            var captura = simulacion.StandardOutput.ReadToEndAsync(); var errores = simulacion.StandardError.ReadToEndAsync();
            using var tiempo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await simulacion.WaitForExitAsync(tiempo.Token); await Task.WhenAll(captura, errores);
            using var unicode = System.Text.Json.JsonDocument.Parse(File.ReadAllText(archivo));
            verificar(simulacion.ExitCode == 5 && unicode.RootElement.GetProperty("Codigo").GetInt32() == 5 && unicode.RootElement.GetProperty("Salida").GetString()!.Contains("Comprobación: áéíóú ñ") && unicode.RootElement.GetProperty("Salida").GetString()!.Contains("Error de prueba"), "captura UTF-16, stderr y código real; cierre automático sin entrada ni foco");
        }
        finally { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true); }
    }
}
