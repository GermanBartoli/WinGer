using System.Text;
using WinGer.Core;

internal static class PruebasProteccion
{
    public static async Task Ejecutar(Action<bool, string> verificar, bool integracion)
    {
        var vacio = ProteccionDefender.Interpretar("{}");
        verificar(!vacio.PuedeAnalizar && vacio.UltimoFin is null, "Defender ausente no se presenta como protegido ni analizado");
        verificar(EstadoDefender.Fecha(DateTimeOffset.MinValue) == "Sin registro disponible", "Defender descarta fechas centinela");
        var estado = ProteccionDefender.Interpretar("""
            {"Antivirus":true,"TiempoReal":false,"Modo":"Normal","InicioRapido":"2026-11-16T12:00:00Z",
             "FinRapido":"2026-11-15T12:00:00Z","FinCompleto":"2026-11-16T10:00:00Z",
             "Amenazas":[{"Nombre":"Amenaza de prueba","Activa":true}],"ErrorAmenazas":null}
            """);
        verificar(estado.PuedeAnalizar && estado.TiempoReal == false, "Permite analizar con antivirus activo aunque tiempo real esté desactivado");
        verificar(estado.UltimoFin == estado.FinCompleto, "Último análisis usa fin real y no inicio más reciente");
        verificar(estado.Amenazas.Single().Activa, "Conserva amenazas activas sin confundirlas con resultado del análisis");
        estado.Modo = "Passive";
        verificar(!estado.PuedeAnalizar, "Defender pasivo no habilita análisis");
        var parcial = ProteccionDefender.Interpretar("{\"Antivirus\":true,\"ErrorAmenazas\":\"Acceso denegado\"}");
        verificar(parcial.ErrorAmenazas is not null && parcial.TiempoReal is null, "Consulta parcial conserva errores y estados desconocidos");
        string Script(AccionDefender accion, string? carpeta = null) => Encoding.Unicode.GetString(Convert.FromBase64String(ProteccionDefender.CrearInicio(accion, carpeta).ArgumentList.Last()));
        verificar(Script(AccionDefender.Rapido).Contains("QuickScan") && Script(AccionDefender.Completo).Contains("FullScan"), "Análisis rápido y completo usan comandos diferentes");
        verificar(Script(AccionDefender.ActualizarFirmas).Contains("Update-MpSignature"), "Actualización usa las definiciones de Defender");
        var sinConexion = Script(AccionDefender.SinConexion);
        verificar(sinConexion.Contains("Start-MpWDOScan") && sinConexion.IndexOf("Get-BitLockerVolume", StringComparison.Ordinal) < sinConexion.IndexOf("Start-MpWDOScan", StringComparison.Ordinal)
            && sinConexion.Contains("exit 2") && !sinConexion.Contains("Suspend-BitLocker"), "Análisis sin conexión comprueba BitLocker sin suspender su protección");
        var ahora = new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
        foreach (var (accion, amarillo, rojo) in new[] { (AccionDefender.Rapido, 5, 7), (AccionDefender.Completo, 15, 30) })
        {
            verificar(ProteccionDefender.Vigencia(accion, ahora, ahora).Nivel == "verde", "Análisis reciente verde: " + accion);
            verificar(ProteccionDefender.Vigencia(accion, ahora.AddDays(-amarillo).AddSeconds(1), ahora).Nivel == "verde", "Verde hasta el umbral exacto: " + accion);
            verificar(ProteccionDefender.Vigencia(accion, ahora.AddDays(-amarillo), ahora).Nivel == "amarillo", "Amarillo desde el umbral exacto: " + accion);
            verificar(ProteccionDefender.Vigencia(accion, ahora.AddDays(-rojo).AddSeconds(1), ahora).Nivel == "amarillo", "Amarillo hasta el umbral rojo: " + accion);
            verificar(ProteccionDefender.Vigencia(accion, ahora.AddDays(-rojo), ahora).Nivel == "rojo", "Rojo desde el umbral exacto: " + accion);
            verificar(ProteccionDefender.Vigencia(accion, null, ahora).Nivel == "gris" && ProteccionDefender.Vigencia(accion, ahora.AddDays(1), ahora).Nivel == "gris", "Sin fecha o fecha futura no aparece verde: " + accion);
        }
        var ruta = @"C:\prueba'; Write-Output 'no ejecutable";
        verificar(Script(AccionDefender.Carpeta, ruta).Contains("-ScanPath 'C:\\prueba''; Write-Output ''no ejecutable'"), "Carpeta con comillas permanece como argumento literal");
        foreach (var accion in Enum.GetValues<AccionDefender>())
        {
            var inicio = ProteccionDefender.CrearInicio(accion, @"C:\Prueba");
            verificar(inicio.Verb == "runas" && inicio.WindowStyle == System.Diagnostics.ProcessWindowStyle.Hidden, "Acción de Defender elevada y sin consola: " + accion);
        }
        var rechazado = false;
        try { ProteccionDefender.CrearInicio((AccionDefender)123); } catch (ArgumentException) { rechazado = true; }
        verificar(rechazado, "Rechaza acciones Defender desconocidas");
        rechazado = false;
        try { ProteccionDefender.CrearInicio(AccionDefender.Carpeta, "relativa"); } catch (ArgumentException) { rechazado = true; }
        verificar(rechazado, "Rechaza carpetas relativas");
        if (integracion)
        {
            var real = await ProteccionDefender.ConsultarAsync(default);
            verificar(real.Consultado > DateTimeOffset.UtcNow.AddMinutes(-2) && real.Modo is not null, "Consulta real de Defender sin iniciar análisis ni cambiar ajustes");
            Console.WriteLine($"Defender: {real.Modo}; rápido: {EstadoDefender.Fecha(real.FinRapido)}; completo: {EstadoDefender.Fecha(real.FinCompleto)}");
        }
    }
}
