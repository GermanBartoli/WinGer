namespace WinGer.Core;

public sealed record ComandoReparacion(string Nombre, string Programa, string Argumentos, string Descripcion, bool Administrador, string Icono)
{
    public string Comando => Programa + " " + Argumentos;
}
public static class ComandosReparacion
{
    public static IReadOnlyList<ComandoReparacion> Todos { get; } = new ComandoReparacion[]
    {
        new("DISM · Comprobación rápida", "dism.exe", "/Online /Cleanup-Image /CheckHealth", "Consulta si Windows ya detectó corrupción de su imagen. No realiza un análisis completo ni repara.", true, "\uE9D9"),
        new("DISM · Analizar imagen", "dism.exe", "/Online /Cleanup-Image /ScanHealth", "Busca corrupción en la imagen de Windows. Puede tardar varios minutos; no repara.", true, "\uE721"),
        new("DISM · Reparar imagen", "dism.exe", "/Online /Cleanup-Image /RestoreHealth", "Repara la imagen de Windows. Puede descargar componentes desde Windows Update. Después conviene ejecutar SFC.", true, "\uE90F"),
        new("SFC · Reparar archivos de sistema", "sfc.exe", "/scannow", "Analiza los archivos protegidos de Windows y reemplaza los dañados. Puede tardar y solicitar reiniciar al terminar.", true, "\uE73E"),
        new("SFC · Solo verificar", "sfc.exe", "/verifyonly", "Comprueba los archivos protegidos sin repararlos.", true, "\uE9D9"),
        new("DISM · Analizar almacén de componentes", "dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore", "Informa tamaño y estado del almacén de componentes y si recomienda limpieza. No elimina componentes.", true, "\uE7B8"),
        new("CHKDSK · Examinar unidad de Windows", "chkdsk.exe", (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\') + " /scan", "Escaneo en línea del sistema de archivos NTFS. Windows puede indicar que necesita una reparación adicional o un reinicio; no se programa aquí.", true, "\uEDA2"),
        new("Red · Ver configuración", "ipconfig.exe", "/all", "Muestra adaptadores, direcciones, DNS y configuración de red.", false, "\uE701"),
        new("Red · Vaciar caché DNS", "ipconfig.exe", "/flushdns", "Borra respuestas DNS almacenadas para que vuelvan a consultarse. Puede ayudar con nombres de sitios que dejaron de resolver.", true, "\uE72C"),
        new("Windows · Información del sistema", "systeminfo.exe", "", "Muestra versión de Windows, memoria, equipo y revisiones instaladas.", false, "\uE946")
    };
    public static bool EsConocido(ComandoReparacion comando) => Todos.Contains(comando);
    public static System.Diagnostics.ProcessStartInfo CrearInicio(ComandoReparacion comando, string? archivoResultado = null)
    {
        if (!EsConocido(comando)) throw new InvalidOperationException("Comando no reconocido.");
        var ruta = Path.Combine(Environment.SystemDirectory, comando.Programa);
        // Solo parámetros del catálogo fijo, sin evaluar texto introducido en la interfaz.
        var destino = archivoResultado?.Replace("'", "''");
        var guardar = destino is null ? "" : "$resultado=@{ Codigo=$codigo; Salida=($salida -join [Environment]::NewLine); Fin=[DateTimeOffset]::Now.ToString('o') }; $json=$resultado | ConvertTo-Json; [IO.File]::WriteAllText('" + destino + ".tmp', $json, [Text.UTF8Encoding]::new($false)); Move-Item -LiteralPath '" + destino + ".tmp' -Destination '" + destino + "' -Force; ";
        var script = "$ErrorActionPreference='Continue'; $salida=@(); $codigo=1; try { & '" + ruta.Replace("'", "''") + "' " + comando.Argumentos + " 2>&1 | ForEach-Object { $linea=$_.ToString(); $salida += $linea; Write-Host $linea }; $codigo=$LASTEXITCODE } catch { $salida += $_.ToString(); Write-Host $_ }; " + guardar + "Write-Host ('Código de salida: ' + $codigo); Read-Host 'Revisá el resultado y pulsá Entrar para cerrar'; exit $codigo";
        var inicio = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = true, Verb = comando.Administrador ? "runas" : "open" };
        inicio.ArgumentList.Add("-NoProfile"); inicio.ArgumentList.Add("-EncodedCommand");
        inicio.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        return inicio;
    }
}
