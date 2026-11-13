using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinGer.Core;

public sealed record ResultadoProceso(int Codigo, string Salida);

public sealed class EjecutorProcesos
{
    public async Task<ResultadoProceso> EjecutarAsync(string programa, IEnumerable<string> argumentos,
        CancellationToken cancelacion, string? clave = null, IReadOnlyDictionary<string, string>? entorno = null)
    {
        var inicio = new ProcessStartInfo(programa)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        if (clave is not null) inicio.Environment["VTCLI_APIKEY"] = clave;
        if (entorno is not null) foreach (var par in entorno) inicio.Environment[par.Key] = par.Value;
        cancelacion.ThrowIfCancellationRequested();
        using var proceso = new Process { StartInfo = inicio };
        try { proceso.Start(); }
        catch (System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException($"No se pudo iniciar {programa}. Comprobá que esté instalado y disponible en PATH."); }
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(TimeSpan.FromMinutes(5));
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var error = proceso.StandardError.ReadToEndAsync();
        try { await proceso.WaitForExitAsync(limite.Token); }
        catch (OperationCanceledException)
        {
            if (!proceso.HasExited) proceso.Kill(true);
            await proceso.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(salida, error);
            throw;
        }
        var texto = (await salida) + "\n" + (await error);
        if (!string.IsNullOrEmpty(clave)) texto = texto.Replace(clave, "[clave protegida]", StringComparison.Ordinal);
        return new(proceso.ExitCode, texto.Trim());
    }
}

public static partial class Hashes
{
    [GeneratedRegex("\\A(?:[a-fA-F0-9]{32}|[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\\z")]
    private static partial Regex Patron();
    public static string Validar(string valor)
    {
        valor = valor.Trim();
        if (!Patron().IsMatch(valor)) throw new ArgumentException("Ingresá un hash MD5, SHA-1 o SHA-256 válido.");
        return valor.ToLowerInvariant();
    }
    public static async Task<string> CalcularAsync(string ruta, CancellationToken cancelacion)
    {
        await using var archivo = File.OpenRead(ruta);
        return Convert.ToHexString(await SHA256.HashDataAsync(archivo, cancelacion)).ToLowerInvariant();
    }
}

public sealed record InformeVirusTotal(string Hash, int Maliciosos, int Sospechosos, int SinDeteccion,
    int Inofensivos, DateTimeOffset? Fecha, string Json)
{
    public string Resumen => $"Maliciosos: {Maliciosos} · Sospechosos: {Sospechosos} · Sin detección: {SinDeteccion} · Inofensivos: {Inofensivos}\nÚltimo análisis: {Fecha?.ToLocalTime().ToString("g") ?? "sin fecha"}.\nCero detecciones no garantiza que el archivo sea seguro.";
}

public sealed class ServicioVirusTotal(HttpClient cliente, AlmacenLocal? almacen = null)
{
    private readonly SemaphoreSlim turno = new(1, 1);
    private readonly Dictionary<string, InformeGuardado> informes = almacen?.LeerInformes() ?? [];
    public void BorrarCache() => informes.Clear();
    private DateTimeOffset proximaConsulta;
    public static InformeVirusTotal InterpretarCli(string json, string hash)
    {
        using var documento = JsonDocument.Parse(json);
        var objeto = documento.RootElement;
        if (objeto.ValueKind == JsonValueKind.Array)
        {
            if (objeto.GetArrayLength() != 1) throw new InvalidOperationException("vt-cli devolvió más de un resultado.");
            objeto = objeto[0];
        }
        if (objeto.TryGetProperty("data", out var datos)) objeto = datos;
        // vt-cli aplana los atributos y usa _id; la API conserva id y attributes.
        var plano = !objeto.TryGetProperty("attributes", out _);
        var tieneId = objeto.TryGetProperty(plano ? "_id" : "id", out var id);
        if (!tieneId || !string.Equals(id.GetString(), hash, StringComparison.OrdinalIgnoreCase) ||
            (objeto.TryGetProperty("sha256", out var sha256) && !string.Equals(sha256.GetString(), hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("El informe de vt-cli no corresponde al archivo descargado.");
        return Interpretar(plano ? "{\"data\":{\"attributes\":" + objeto.GetRawText() + "}}" : "{\"data\":" + objeto.GetRawText() + "}", hash);
    }
    public async Task<InformeVirusTotal?> ConsultarAsync(string hash, string clave, CancellationToken cancelacion)
    {
        hash = Hashes.Validar(hash);
        if (string.IsNullOrWhiteSpace(clave)) throw new InvalidOperationException("Guardá tu clave de VirusTotal en Configuración.");
        await turno.WaitAsync(cancelacion);
        try
        {
            if (informes.TryGetValue(hash, out var guardado) && guardado.Fecha >= DateTimeOffset.UtcNow.AddHours(-1)) return guardado.Informe;
            var espera = proximaConsulta - DateTimeOffset.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, cancelacion);
            proximaConsulta = DateTimeOffset.UtcNow.AddSeconds(16);
            var informe = await ConsultarRemotoAsync(hash, clave, cancelacion);
            informes[hash] = new(DateTimeOffset.UtcNow, informe);
            almacen?.GuardarInformes(informes.Where(p => p.Value.Fecha >= DateTimeOffset.UtcNow.AddHours(-1)).ToDictionary());
            return informe;
        }
        finally { turno.Release(); }
    }
    private async Task<InformeVirusTotal?> ConsultarRemotoAsync(string hash, string clave, CancellationToken cancelacion)
    {
        hash = Hashes.Validar(hash);
        if (string.IsNullOrWhiteSpace(clave)) throw new InvalidOperationException("Guardá tu clave de VirusTotal en Configuración.");
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{hash}");
        solicitud.Headers.Add("x-apikey", clave);
        using var respuesta = await cliente.SendAsync(solicitud, cancelacion);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return null;
        if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("VirusTotal rechazó la clave o sus permisos.");
        if ((int)respuesta.StatusCode == 429) throw new InvalidOperationException("Se alcanzó el límite de VirusTotal. Esperá antes de reintentar.");
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadAsStringAsync(cancelacion);
        return Interpretar(json, hash);
    }
    public static InformeVirusTotal Interpretar(string json, string hash)
    {
        using var documento = JsonDocument.Parse(json);
        var atributos = documento.RootElement.GetProperty("data").GetProperty("attributes");
        var estadisticas = atributos.GetProperty("last_analysis_stats");
        int Numero(string nombre) => estadisticas.TryGetProperty(nombre, out var valor) ? valor.GetInt32() : 0;
        DateTimeOffset? fecha = atributos.TryGetProperty("last_analysis_date", out var fechaJson)
            ? DateTimeOffset.FromUnixTimeSeconds(fechaJson.GetInt64()) : null;
        return new(hash, Numero("malicious"), Numero("suspicious"), Numero("undetected"), Numero("harmless"), fecha,
            JsonSerializer.Serialize(documento.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class Configuracion
{
    public bool RegistroVtaiIntentado { get; set; }
    public bool MinimizarAlCerrar { get; set; }
    public string ConsultaAutomatica { get; set; } = "VTAI";
    public string Tema { get; set; } = "Predeterminado";
}

public sealed record EntradaHistorial(DateTimeOffset Fecha, string Operacion, string Resultado);

public sealed partial class AlmacenLocal
{
    private readonly string carpeta;
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };
    public AlmacenLocal(string? ruta = null)
    {
        carpeta = ruta ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGer");
        Directory.CreateDirectory(carpeta);
    }
    public Configuracion LeerConfiguracion() => Leer<Configuracion>("configuracion.json") ?? new();
    public List<EntradaHistorial> LeerHistorial() => Leer<List<EntradaHistorial>>("historial.json") ?? [];
    private T? Leer<T>(string nombre)
    {
        var ruta = Path.Combine(carpeta, nombre);
        if (!File.Exists(ruta)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(ruta)); }
        catch (JsonException) { return default; }
    }
    private void Escribir<T>(string nombre, T valor)
    {
        var ruta = Path.Combine(carpeta, nombre);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(valor, Opciones));
        File.Move(temporal, ruta, true);
    }
    public void Guardar(Configuracion configuracion) => Escribir("configuracion.json", configuracion);
    public void Registrar(string operacion, string resultado)
    {
        var historial = LeerHistorial();
        historial.Insert(0, new(DateTimeOffset.Now, operacion, resultado));
        Escribir("historial.json", historial.Take(200).ToList());
    }
}
