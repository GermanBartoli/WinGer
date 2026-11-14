using System.Text.Json;

namespace WinGer.Core;

public sealed record ResultadoReparacion(DateTimeOffset Inicio, DateTimeOffset Fin, int? Codigo, string Nivel, string Mensaje, string Salida);

public static class ResultadosReparacion
{
    public static string Seccion(ComandoReparacion comando) => comando.Argumentos is "/scannow" or "/flushdns" || comando.Argumentos.Contains("/RestoreHealth")
        ? "Reparación y mantenimiento" : comando.Programa is "systeminfo.exe" || comando.Argumentos == "/all" ? "Información del equipo" : "Análisis y comprobaciones";

    public static ResultadoReparacion Interpretar(ComandoReparacion comando, DateTimeOffset inicio, DateTimeOffset fin, int? codigo, string salida)
    {
        var texto = salida.Replace("\0", "").ToLowerInvariant();
        bool Contiene(params string[] frases) => frases.Any(texto.Contains);
        var nivel = "amarillo";
        var mensaje = "Resultado pendiente de revisión. Abrí el detalle.";
        if (codigo is null) mensaje = "Ejecución interrumpida o sin resultado confirmado.";
        else if (codigo == 3010 || Contiene("restart", "reiniciar", "reboot", "reinicio")) mensaje = "Revisá el resultado: puede requerir reiniciar Windows.";
        else if (codigo != 0) { nivel = "rojo"; mensaje = "El comando informó un error. Revisá el detalle."; }
        else if (Contiene("unable to fix", "could not", "no pudo", "no se pudo", "unrepairable", "no se puede reparar")) { nivel = "rojo"; mensaje = "Se detectaron problemas que no pudieron resolverse."; }
        else if (Contiene("no component store corruption detected", "no se detectó ningún daño", "no se detectaron daños", "did not find any integrity violations", "no encontró ninguna infracción de integridad", "found no problems", "no encontró problemas")) { nivel = "verde"; mensaje = "Comprobación completada: no se detectaron problemas."; }
        else if (Contiene("successfully repaired", "reparó correctamente", "restore operation completed successfully", "operación de restauración se completó correctamente")) { nivel = "verde"; mensaje = "Reparación completada correctamente."; }
        else if (comando.Programa is "ipconfig.exe" or "systeminfo.exe") { nivel = "verde"; mensaje = "Comando completado correctamente."; }
        return new(inicio, fin, codigo, nivel, mensaje, salida.Replace("\0", ""));
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
