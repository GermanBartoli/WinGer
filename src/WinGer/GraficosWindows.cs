using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace WinGer;

internal sealed record AplicacionGrafica(string Nombre, string Ruta);

internal sealed record FilaGrafica(string Nombre, string Ruta, string Estado, bool Favorita, bool Ausente,
    bool Disponible, bool Pendiente, bool Recordada)
{
    public override string ToString() => $"{Titulo}, {Detalle}, {Ruta}";
    public string Titulo => (Favorita ? "★  " : "") + Nombre;
    public string Detalle => Estado + (Recordada ? " · Recordar alto rendimiento" : "");
}

internal static class GraficosWindows
{
    private const string ClaveGpu = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ClaveApp = @"Software\WinGer\Graficos\";
    private static readonly Regex CarpetaVersion = new(@"\\(?:app-)?(?<version>\d+(?:\.\d+){1,3})(?=\\)", RegexOptions.IgnoreCase);
    private static readonly Regex IdStore = new(@"^[\w.-]+_[a-z0-9]+![\w.-]+$", RegexOptions.IgnoreCase);

    private static string Identidad(string ruta) => CarpetaVersion.Replace(ruta, tramo => tramo.Value.Replace(tramo.Groups["version"].Value, "{versión}"), 1);
    private static bool EsArchivo(string ruta) => Path.IsPathFullyQualified(ruta) && ruta.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    private static bool EsEntrada(string ruta) => EsArchivo(ruta) || IdStore.IsMatch(ruta);

    private static bool AusenciaConfirmada(string ruta)
    {
        if (!EsArchivo(ruta) || ruta.StartsWith(@"\\")) return false;
        try
        {
            var disco = new DriveInfo(Path.GetPathRoot(ruta)!);
            if (disco.DriveType != DriveType.Fixed || !disco.IsReady) return false;
            _ = File.GetAttributes(ruta);
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    private static Dictionary<string,string> Marcas(string grupo)
    {
        using var clave = Registry.CurrentUser.OpenSubKey(ClaveApp + grupo);
        var marcas = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (clave != null)
            foreach (var nombre in clave.GetValueNames())
                if (clave.GetValue(nombre) is string ruta && EsEntrada(ruta)) marcas[nombre] = ruta;
        return marcas;
    }

    internal static void Marcar(string grupo, string ruta, bool activar)
    {
        using var clave = Registry.CurrentUser.CreateSubKey(ClaveApp + grupo);
        if (activar) clave.SetValue(Identidad(ruta), ruta);
        else clave.DeleteValue(Identidad(ruta), false);
    }

    internal static List<FilaGrafica> Leer(IEnumerable<AplicacionGrafica> detectadas)
    {
        using var clave = Registry.CurrentUser.OpenSubKey(ClaveGpu);
        var favoritas = Marcas("Favoritos");
        var reglas = Marcas("Recordadas");
        var catalogo = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in detectadas) catalogo[app.Ruta] = app.Nombre;
        foreach (var ruta in (clave?.GetValueNames() ?? []).Where(EsEntrada))
            catalogo.TryAdd(ruta, EsArchivo(ruta) ? Path.GetFileNameWithoutExtension(ruta) : ruta.Split('!')[0]);

        var identidades = catalogo.Keys.Select(Identidad).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ruta in favoritas.Values.Concat(reglas.Values))
            if (identidades.Add(Identidad(ruta)))
                catalogo[ruta] = EsArchivo(ruta) ? Path.GetFileNameWithoutExtension(ruta) : ruta.Split('!')[0];

        return catalogo.Select(par =>
        {
            string ruta = par.Key;
            var valor = clave?.GetValue(ruta) as string;
            var gpu = Regex.Match(valor ?? "", @"(?:^|;)GpuPreference=([^;]+)", RegexOptions.IgnoreCase);
            bool disponible = EsArchivo(ruta) ? File.Exists(ruta) : detectadas.Any(a => a.Ruta.Equals(ruta, StringComparison.OrdinalIgnoreCase));
            bool ausente = !disponible && AusenciaConfirmada(ruta);
            string estado = !disponible ? (ausente ? "Archivo ausente" : "Sin verificar") : gpu.Groups[1].Value switch
            {
                "2" => "Alto rendimiento", "1" => "Ahorro de energía", "0" => "Windows decide",
                "" => "Sin preferencia", _ => "GPU personalizada"
            };
            return new FilaGrafica(par.Value, ruta, estado, favoritas.ContainsKey(Identidad(ruta)), ausente && valor != null,
                disponible, disponible && !gpu.Success, reglas.ContainsKey(Identidad(ruta)));
        }).ToList();
    }

    internal static void Aplicar(string ruta, bool soloFaltantes)
    {
        if (!EsEntrada(ruta) || (EsArchivo(ruta) && !File.Exists(ruta))) return;
        using var clave = Registry.CurrentUser.CreateSubKey(ClaveGpu);
        var partes = (clave.GetValue(ruta) as string ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        bool EsGpu(string parte) => parte.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase);
        if (soloFaltantes && partes.Any(EsGpu)) return;
        var conservar = partes.Where(p => !EsGpu(p) && !p.StartsWith("SpecificAdapter=", StringComparison.OrdinalIgnoreCase));
        clave.SetValue(ruta, string.Join(';', conservar.Append("GpuPreference=2")) + ";");
    }

    internal static void Limpiar(string ruta)
    {
        if (!AusenciaConfirmada(ruta)) return;
        using var clave = Registry.CurrentUser.OpenSubKey(ClaveGpu, true);
        clave?.DeleteValue(ruta, false);
    }

    internal static void Sincronizar()
    {
        foreach (var ruta in Marcas("Recordadas").Values)
        {
            var tramo = CarpetaVersion.Match(ruta);
            if (!tramo.Success) { Aplicar(ruta, true); continue; }
            var padre = ruta[..tramo.Index];
            var sufijo = ruta[(tramo.Index + tramo.Length + 1)..];
            if (!Directory.Exists(padre)) continue;
            try
            {
                var prefijo = tramo.Value[1..].StartsWith("app-", StringComparison.OrdinalIgnoreCase) ? "app-" : "";
                var reciente = Directory.EnumerateDirectories(padre)
                    .Where(d => Path.GetFileName(d).StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                    .Select(d => (Ruta: Path.Combine(d, sufijo), Version: Version.TryParse(Path.GetFileName(d)[prefijo.Length..], out var v) ? v : null))
                    .Where(d => d.Version != null && File.Exists(d.Ruta))
                    .OrderByDescending(d => d.Version).FirstOrDefault();
                if (reciente.Ruta != null) Aplicar(reciente.Ruta, true);
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
