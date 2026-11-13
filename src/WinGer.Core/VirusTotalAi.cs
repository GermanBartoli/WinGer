using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinGer.Core;

public sealed class ServicioVirusTotalAi(HttpClient cliente, AlmacenLocal almacen)
{
    private DateTimeOffset siguiente;
    private readonly Dictionary<string, InformeGuardado> informes = almacen.LeerInformesVtai();
    public void BorrarCache() => informes.Clear();
    public async Task<string> RegistrarAsync(CancellationToken token)
    {
        using var respuesta = await cliente.PostAsJsonAsync("https://ai.virustotal.com/api/v3/agents/register",
            new { agent_family = "WinGer", agent_version = "0.2.0" }, token);
        if (!respuesta.IsSuccessStatusCode) throw new InvalidOperationException($"No se pudo conectar VTAI (HTTP {(int)respuesta.StatusCode}). No se reintenta el registro automáticamente.");
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(token));
        var valor = json.RootElement.GetProperty("agent_token").GetString() ?? "";
        if (!Regex.IsMatch(valor, @"\Avtai_[A-Za-z0-9_-]+\z")) throw new InvalidOperationException("VTAI devolvió una credencial no reconocida.");
        return valor;
    }
    public async Task<InformeVirusTotal?> ConsultarAsync(string hash, string credencial, CancellationToken token)
    {
        hash = Hashes.Validar(hash);
        if (informes.TryGetValue(hash, out var anterior) && anterior.Fecha <= DateTimeOffset.UtcNow && anterior.Fecha >= DateTimeOffset.UtcNow.AddHours(-1)) return anterior.Informe;
        var espera = siguiente - DateTimeOffset.UtcNow;
        if (espera > TimeSpan.Zero) await Task.Delay(espera, token);
        siguiente = DateTimeOffset.UtcNow.AddSeconds(1.1);
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"https://ai.virustotal.com/api/v3/files/{hash}");
        solicitud.Headers.Authorization = new("Bearer", credencial);
        using var respuesta = await cliente.SendAsync(solicitud, token);
        if ((int)respuesta.StatusCode is 429 or 503)
        {
            siguiente = respuesta.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(respuesta.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            throw new InvalidOperationException($"VTAI no disponible o cuota agotada. Reintentá después de {siguiente.LocalDateTime:t}.");
        }
        if (respuesta.StatusCode != HttpStatusCode.NotFound && !respuesta.IsSuccessStatusCode)
            throw new InvalidOperationException($"VTAI no pudo consultar el informe (HTTP {(int)respuesta.StatusCode}). Revisá la conexión.");
        var informe = respuesta.StatusCode == HttpStatusCode.NotFound ? null : Interpretar(await respuesta.Content.ReadAsStringAsync(token), hash);
        informes[hash] = new(DateTimeOffset.UtcNow, informe);
        almacen.GuardarInformesVtai(informes.Where(p => p.Value.Fecha >= DateTimeOffset.UtcNow.AddHours(-1)).ToDictionary());
        return informe;
    }
    public static InformeVirusTotal Interpretar(string json, string hash)
    {
        using var documento = JsonDocument.Parse(json);
        var datos = documento.RootElement.GetProperty("data");
        if (datos.GetProperty("id").GetString() != hash) throw new InvalidOperationException("El informe VTAI no corresponde al hash solicitado.");
        var numeros = datos.GetProperty("last_analysis_stats");
        int Numero(string clave) => numeros.TryGetProperty(clave, out var valor) && valor.TryGetInt32(out var numero) && numero >= 0
            ? numero : throw new InvalidOperationException("El informe VTAI no contiene estadísticas completas.");
        DateTimeOffset? fecha = datos.TryGetProperty("analysis_date", out var f) && f.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(f.GetString(), out var instante) ? instante : null;
        return new(hash, Numero("malicious"), Numero("suspicious"), Numero("undetected"), Numero("harmless"), fecha, json);
    }
}
