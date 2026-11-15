using System.Text.Json;

namespace WinGer.Core;

public sealed record ResultadoReparacion(DateTimeOffset Inicio, DateTimeOffset Fin, int? Codigo, string Nivel, string Mensaje, string Salida);

public static class ResultadosReparacion
{
    public static string Seccion(ComandoReparacion comando) => comando.Argumentos is "/scannow" or "/flushdns" || comando.Argumentos.Contains("/RestoreHealth")
        ? "Reparación y mantenimiento" : comando.Programa is "systeminfo.exe" || comando.Argumentos == "/all" ? "Información del equipo" : "Análisis y comprobaciones";

    public static ResultadoReparacion Interpretar(ComandoReparacion comando, DateTimeOffset inicio, DateTimeOffset fin, int? codigo, string salida)
    {
        salida = LimpiarSalida(salida);
        var texto = System.Text.RegularExpressions.Regex.Replace(salida, @"\s+", " ").ToLowerInvariant();
        bool Contiene(params string[] frases) => frases.Any(texto.Contains);
        var nivel = "amarillo";
        var mensaje = "Resultado pendiente de revisión. Abrí el detalle.";
        if (codigo is null) mensaje = "Ejecución interrumpida o sin resultado confirmado.";
        else if (codigo == 3010) mensaje = "Operación completada: es necesario reiniciar Windows.";
        else if (comando.Programa == "chkdsk.exe" && Contiene("examen en esta unidad", "scan is already running"))
            mensaje = "Ya hay un examen de esta unidad en curso. Esperá a que termine antes de volver a ejecutar CHKDSK.";
        else if (codigo != 0) { nivel = "rojo"; mensaje = "El comando informó un error. Revisá el detalle."; }
        else if (Contiene("restart required", "reboot required", "debe reiniciar", "es necesario reiniciar")) mensaje = "Revisá el resultado: requiere reiniciar Windows.";
        else if (Contiene("unable to fix", "could not", "no pudo", "no se pudo", "unrepairable", "no se puede reparar")) { nivel = "rojo"; mensaje = "Se detectaron problemas que no pudieron resolverse."; }
        else if (comando.Argumentos.Contains("/AnalyzeComponentStore"))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(texto, @"(?:component store cleanup recommended|limpieza del almacén de componentes recomendada|se recomienda.*limpieza.*)\s*:\s*(?:yes|sí|si)"))
            { nivel = "verde"; mensaje = "Análisis correcto. Windows recomienda limpiar componentes antiguos para liberar espacio; no indica archivos dañados."; }
            else if (System.Text.RegularExpressions.Regex.IsMatch(texto, @"(?:component store cleanup recommended|limpieza del almacén de componentes recomendada|se recomienda.*limpieza.*)\s*:\s*no"))
            { nivel = "verde"; mensaje = "Análisis correcto. Windows no recomienda limpiar el almacén de componentes."; }
            else mensaje = "Análisis completado. Revisá en el detalle si Windows recomienda una limpieza.";
        }
        else if (Contiene("no component store corruption detected", "no se detectó ningún daño", "no se detectaron daños", "did not find any integrity violations", "no encontró ninguna infracción de integridad", "found no problems", "no encontró problemas", "se examinó el sistema de archivos sin encontrar problemas")) { nivel = "verde"; mensaje = "Comprobación completada: no se detectaron problemas."; }
        else if (Contiene("successfully repaired", "reparó correctamente", "restore operation completed successfully", "operación de restauración se completó correctamente")) { nivel = "verde"; mensaje = "Reparación completada correctamente."; }
        else if (comando.Programa is "ipconfig.exe" or "systeminfo.exe") { nivel = "verde"; mensaje = "Comando completado correctamente."; }
        if (Contiene("acceso denegado", "access is denied")) { nivel = "rojo"; mensaje = "Windows denegó el acceso. Volvé a ejecutar y aceptá los permisos de administrador."; }
        return new(inicio, fin, codigo, nivel, mensaje, salida);
    }

    public static string LimpiarSalida(string salida)
    {
        salida = salida.Replace("\0", "");
        // Compatibilidad con los resultados de SFC guardados por la captura anterior.
        if (salida.Contains("comprobaci¾n") || salida.Contains("Protecci¾n") || salida.Contains("tardarß") || salida.Contains("estß ejecutando"))
            salida = salida.Replace('¾', 'ó').Replace('ß', 'á').Replace('·', 'ú').Replace('Ú', 'é').Replace('±', 'ñ').Replace('Ý', 'í');
        var lineas = new List<string>();
        var progresoAnterior = false;
        foreach (var entrada in salida.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var linea = entrada.TrimEnd().TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(linea)) continue;
            var progreso = System.Text.RegularExpressions.Regex.IsMatch(linea, @"^\s*(\[=+.*\]|(?:Se completó la comprobación|Verification).*\d+(?:[.,]\d+)?%.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (progreso && progresoAnterior) lineas[^1] = linea;
            else lineas.Add(linea);
            progresoAnterior = progreso;
        }
        return string.Join(Environment.NewLine, lineas);
    }
}

public sealed partial class AlmacenLocal
{
    public string RutaDatos => carpeta;
    public Dictionary<string, ResultadoReparacion> LeerReparaciones() => Leer<Dictionary<string, ResultadoReparacion>>("reparaciones.json") ?? [];
    public void GuardarReparacion(ComandoReparacion comando, ResultadoReparacion resultado)
    {
        var registros = LeerReparaciones(); registros[comando.Comando] = resultado;
        Escribir("reparaciones.json", registros);
    }

}
