namespace WinGer.Core;

public sealed class CacheSesion
{
    public DateTimeOffset? UltimaBusqueda { get; set; }
    public List<Paquete> Paquetes { get; set; } = [];
    public bool OrdenarPorDetecciones { get; set; } = true;
    public bool SoloSeleccionados { get; set; }
    public string Filtro { get; set; } = "";
    public string Hash { get; set; } = "";
    public string SalidaVirus { get; set; } = "";
}

public sealed record InformeGuardado(DateTimeOffset Fecha, InformeVirusTotal? Informe);

public sealed partial class AlmacenLocal
{
    public Dictionary<string, InformeGuardado> LeerInformesVtai() => Leer<Dictionary<string, InformeGuardado>>("cache-vtai.json") ?? [];
    public void GuardarInformesVtai(Dictionary<string, InformeGuardado> informes) => Escribir("cache-vtai.json", informes);
    public CacheSesion LeerCache() => Leer<CacheSesion>("cache-sesion.json") ?? new();
    public void GuardarCache(CacheSesion sesion) => Escribir("cache-sesion.json", sesion);
    public Dictionary<string, InformeGuardado> LeerInformes() => Leer<Dictionary<string, InformeGuardado>>("cache-informes.json") ?? [];
    public void GuardarInformes(Dictionary<string, InformeGuardado> informes) => Escribir("cache-informes.json", informes);
    public void BorrarCache()
    {
        File.Delete(Path.Combine(carpeta, "cache-sesion.json"));
        File.Delete(Path.Combine(carpeta, "cache-informes.json"));
        File.Delete(Path.Combine(carpeta, "cache-vtai.json"));
    }
}
