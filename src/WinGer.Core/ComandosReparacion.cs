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
    public static System.Diagnostics.ProcessStartInfo CrearInicio(ComandoReparacion comando, string? archivoResultado = null, bool cerrarAutomaticamente = false)
    {
        if (!EsConocido(comando)) throw new InvalidOperationException("Comando no reconocido.");
        var ruta = Path.Combine(Environment.SystemDirectory, comando.Programa);
        // Solo parámetros del catálogo fijo, sin evaluar texto introducido en la interfaz.
        var destino = archivoResultado?.Replace("'", "''");
        var guardar = destino is null ? "" : "$resultado=@{ Codigo=$codigo; Salida=($salida -join [Environment]::NewLine); Fin=[DateTimeOffset]::Now.ToString('o') }; $json=$resultado | ConvertTo-Json; [IO.File]::WriteAllText('" + destino + ".tmp', $json, [Text.UTF8Encoding]::new($false)); Move-Item -LiteralPath '" + destino + ".tmp' -Destination '" + destino + "' -Force; ";
        var script = "$ErrorActionPreference='Stop'; Write-Host 'Preparando " + comando.Nombre + "...'; $salida=@(); $codigo=1; try { Add-Type -TypeDefinition @'\n" + CapturaConsola + "\n'@; $codigo=[ConsolaReparacion]::Ejecutar('" + ruta.Replace("'", "''") + "', '" + comando.Argumentos + "', " + (comando.Programa == "sfc.exe" ? "1200" : comando.Programa == "chkdsk.exe" ? "0" : "-1") + "); $salida=@([ConsolaReparacion]::Salida) } catch { $salida += $_.ToString(); Write-Host $_ }; " + guardar + "Write-Host ('Código de salida: ' + $codigo); " + (cerrarAutomaticamente ? "" : "Read-Host 'Revisá el resultado y pulsá Entrar para cerrar'; ") + "exit $codigo";
        var inicio = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = true, Verb = comando.Administrador ? "runas" : "open" };
        inicio.ArgumentList.Add("-NoProfile"); inicio.ArgumentList.Add("-EncodedCommand");
        inicio.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        return inicio;
    }

    // El proceso conserva la entrada de la consola y se drenan ambas salidas en paralelo.
    private const string CapturaConsola = """
        using System;
        using System.Diagnostics;
        using System.IO;
        using System.Text;
        using System.Threading.Tasks;
        using System.Runtime.InteropServices;
        public static class ConsolaReparacion {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFile(string nombre, uint acceso, uint compartir, IntPtr seguridad, uint apertura, uint atributos, IntPtr plantilla);
            [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool WriteConsole(IntPtr handle, string texto, uint longitud, out uint escritos, IntPtr reservado);
            [DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr handle, out uint modo);
            [DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr handle, uint modo);
            [DllImport("kernel32.dll")] static extern uint GetOEMCP();
            static readonly StringBuilder texto = new StringBuilder();
            static IntPtr pantalla;
            public static string Salida { get { return texto.ToString(); } }
            static async Task Copiar(StreamReader lector) {
                var buffer = new char[256]; int cantidad;
                while ((cantidad = await lector.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    lock (texto) {
                        texto.Append(buffer, 0, cantidad); uint escritos;
                        if (!WriteConsole(pantalla, new string(buffer, 0, cantidad), (uint)cantidad, out escritos, IntPtr.Zero))
                            Console.Write(buffer, 0, cantidad);
                    }
                }
            }
            public static int Ejecutar(string ruta, string argumentos, int pagina) {
                // PowerShell puede redirigir sus handles estándar aun teniendo una consola visible.
                uint modo; var entrada = CreateFile("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                pantalla = CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (GetConsoleMode(entrada, out modo)) SetConsoleMode(entrada, (modo | 0x80) & ~0x40u);
                var codificacion = pagina == 0 ? Encoding.Default : Encoding.GetEncoding(pagina == -1 ? (int)GetOEMCP() : pagina);
                var inicio = new ProcessStartInfo(ruta, argumentos) {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = codificacion, StandardErrorEncoding = codificacion
                };
                try {
                    using (var proceso = Process.Start(inicio)) {
                        var salida = Copiar(proceso.StandardOutput); var error = Copiar(proceso.StandardError);
                        proceso.WaitForExit(); Task.WaitAll(salida, error); return proceso.ExitCode;
                    }
                } finally { CloseHandle(entrada); CloseHandle(pantalla); }
            }
        }
        """;
}
