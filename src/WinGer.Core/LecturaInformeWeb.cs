using System.Text.Json;

namespace WinGer.Core;

public static class LecturaInformeWeb
{
    // Solo DOM renderizado: no se consultan API internas ni se sortean verificaciones del sitio.
    public const string Script = """
        (() => {
          const roots = [];
          function recorrer(root) {
            roots.push(root);
            for (const e of root.querySelectorAll('*')) if (e.shadowRoot) recorrer(e.shadowRoot);
          }
          recorrer(document);
          const filas = roots.flatMap(r => [...r.querySelectorAll('span.result.file[id^="engine-text-"]')]);
          const categorias = filas.map(e => e.querySelector('[role="img"][aria-label]')?.getAttribute('aria-label'));
          const fecha = roots.flatMap(r => [...r.querySelectorAll('.last-analysis-ago')])[0]?.getAttribute('data-tooltip-text');
          const texto = roots.map(r => r.textContent).join(' ');
          const hash = location.pathname.match(/^\/gui\/file\/([a-f0-9]{64})\/detection$/)?.[1];
          return {url: location.href, hash: hash && texto.includes(hash) ? hash : null, categorias, fecha};
        })()
        """;

    public static InformeVirusTotal? Interpretar(string json, string hash)
    {
        if (json == "null") return null;
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;
        if (!raiz.TryGetProperty("url", out var url) || url.GetString() != $"https://www.virustotal.com/gui/file/{hash}/detection" ||
            !raiz.TryGetProperty("hash", out var id) || id.GetString() != hash) return null;
        var categorias = raiz.GetProperty("categorias").EnumerateArray().Select(x => x.GetString()).ToArray();
        string[] conocidas = ["malicious", "suspicious", "undetected", "harmless", "type-unsupported", "failure", "timeout", "confirmed-timeout"];
        if (categorias.Length == 0 || categorias.Any(c => c is null || !conocidas.Contains(c))) return null;
        int Contar(string c) => categorias.Count(x => x == c);
        if (Contar("malicious") + Contar("suspicious") + Contar("undetected") + Contar("harmless") == 0) return null;
        DateTimeOffset? fecha = raiz.TryGetProperty("fecha", out var f) && f.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(f.GetString()?.Replace(" UTC", "+00:00"), out var instante) ? instante : null;
        return new(hash, Contar("malicious"), Contar("suspicious"), Contar("undetected"), Contar("harmless"), fecha, json);
    }
}
