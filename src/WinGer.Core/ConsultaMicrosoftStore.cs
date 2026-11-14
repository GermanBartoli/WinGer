namespace WinGer.Core;

public static class ConsultaMicrosoftStore
{
    public static string Presentar(ResultadoProceso resultado)
    {
        // Solo aclarar la confirmación omitida cuando Store ya devolvió una lista y terminó correctamente.
        if (resultado.Codigo != 0 || !resultado.Salida.Contains("Updates available (", StringComparison.OrdinalIgnoreCase) ||
            !resultado.Salida.Contains("Failed to read input in non-interactive mode.", StringComparison.Ordinal))
            return resultado.Salida;
        var lineas = resultado.Salida.Split('\n')
            .Where(linea => !linea.TrimStart().StartsWith("Would you like to install ", StringComparison.Ordinal))
            .Select(linea => linea.Replace("Failed to read input in non-interactive mode.",
                "No se instalaron actualizaciones. Para instalarlas, usá «Abrir Store para actualizar».", StringComparison.Ordinal));
        return string.Join('\n', lineas);
    }
}
