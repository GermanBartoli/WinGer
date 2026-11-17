using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WinGer.Core;

public enum AccionDefender { Rapido, Completo, Carpeta, ActualizarFirmas }

public sealed record AmenazaDefender(string Nombre, bool Activa);

public sealed class EstadoDefender
{
    public bool? Antivirus { get; set; }
    public bool? TiempoReal { get; set; }
    public bool? Comportamiento { get; set; }
    public bool? Antimanipulacion { get; set; }
    public bool? FirmasAntiguas { get; set; }
    public string? Modo { get; set; }
    public string? VersionFirmas { get; set; }
    public DateTimeOffset? FechaFirmas { get; set; }
    public DateTimeOffset? InicioRapido { get; set; }
    public DateTimeOffset? FinRapido { get; set; }
    public DateTimeOffset? InicioCompleto { get; set; }
    public DateTimeOffset? FinCompleto { get; set; }
    public AmenazaDefender[] Amenazas { get; set; } = [];
    public string? ErrorAmenazas { get; set; }
    public DateTimeOffset Consultado { get; set; }
    public bool PuedeAnalizar => Antivirus == true && string.Equals(Modo, "Normal", StringComparison.OrdinalIgnoreCase);
    public DateTimeOffset? UltimoFin => new[] { FechaValida(FinRapido), FechaValida(FinCompleto) }.Max();
    public static DateTimeOffset? FechaValida(DateTimeOffset? fecha) => fecha?.Year > 1970 ? fecha : null;
    public static string Fecha(DateTimeOffset? fecha) => FechaValida(fecha)?.ToLocalTime().ToString("g") ?? "Sin registro disponible";
}

public static class ProteccionDefender
{
    public static string PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
    // Fechas ISO explícitas: Windows PowerShell 5.1 serializa DateTime de otra manera.
    public const string Consulta = """
        $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        try {
            $e=Get-MpComputerStatus
            function Fecha($f) { if ($null -ne $f -and $f.Year -gt 1970) { $f.ToUniversalTime().ToString('o') } else { $null } }
            $amenazas=@(); $errorAmenazas=$null
            try { $amenazas=@(Get-MpThreat | ForEach-Object { @{Nombre=$_.ThreatName; Activa=[bool]$_.IsActive} }) }
            catch { $errorAmenazas='No se pudo consultar el registro de amenazas. Revisá el historial en Seguridad de Windows.' }
            @{
                Antivirus=$e.AntivirusEnabled; TiempoReal=$e.RealTimeProtectionEnabled
                Comportamiento=$e.BehaviorMonitorEnabled; Antimanipulacion=$e.IsTamperProtected
                FirmasAntiguas=$e.DefenderSignaturesOutOfDate; Modo=$e.AMRunningMode
                VersionFirmas=$e.AntivirusSignatureVersion; FechaFirmas=(Fecha $e.AntivirusSignatureLastUpdated)
                InicioRapido=(Fecha $e.QuickScanStartTime); FinRapido=(Fecha $e.QuickScanEndTime)
                InicioCompleto=(Fecha $e.FullScanStartTime); FinCompleto=(Fecha $e.FullScanEndTime)
                Amenazas=$amenazas; ErrorAmenazas=$errorAmenazas; Consultado=[DateTimeOffset]::UtcNow.ToString('o')
            } | ConvertTo-Json -Depth 5 -Compress
        } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;

    public static EstadoDefender Interpretar(string json) => JsonSerializer.Deserialize<EstadoDefender>(json)
        ?? throw new InvalidOperationException("Defender no devolvió un estado válido.");

    public static async Task<EstadoDefender> ConsultarAsync(CancellationToken cancelacion)
    {
        var resultado = await new EjecutorProcesos().EjecutarAsync(PowerShell,
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Codificar(Consulta)], cancelacion, tiempoEspera: TimeSpan.FromSeconds(45));
        if (resultado.Codigo != 0) throw new InvalidOperationException("No se pudo consultar Microsoft Defender. Puede estar deshabilitado, administrado por tu organización o no disponible. " + resultado.Salida.Trim());
        return Interpretar(resultado.Salida);
    }

    public static ProcessStartInfo CrearInicio(AccionDefender accion, string? carpeta = null)
    {
        var comando = accion switch
        {
            AccionDefender.Rapido => "Start-MpScan -ScanType QuickScan",
            AccionDefender.Completo => "Start-MpScan -ScanType FullScan",
            AccionDefender.Carpeta when !string.IsNullOrWhiteSpace(carpeta) && Path.IsPathFullyQualified(carpeta) =>
                "Start-MpScan -ScanType CustomScan -ScanPath '" + carpeta.Replace("'", "''") + "'",
            AccionDefender.ActualizarFirmas => "Update-MpSignature",
            _ => throw new ArgumentException("Acción o carpeta de Defender no válida.")
        };
        var inicio = new ProcessStartInfo(PowerShell) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        inicio.ArgumentList.Add("-NoProfile"); inicio.ArgumentList.Add("-NonInteractive"); inicio.ArgumentList.Add("-EncodedCommand");
        inicio.ArgumentList.Add(Codificar("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { " + comando + "; exit 0 } catch { exit 1 }"));
        return inicio;
    }
    private static string Codificar(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
