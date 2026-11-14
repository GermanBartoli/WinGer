using System.Text.RegularExpressions;

namespace WinGer.Core;

public sealed record AplicacionCatalogo(string Nombre, string Id, string Categoria, string Descripcion, string Sitio, string Version = "");
public static class CatalogoAplicaciones
{
    public static IReadOnlyList<AplicacionCatalogo> Populares { get; } = new AplicacionCatalogo[]
    {
        new("Google Chrome", "Google.Chrome", "Navegadores", "Navegador de Google.", "https://www.google.com/chrome/"),
        new("Mozilla Firefox", "Mozilla.Firefox", "Navegadores", "Navegador de Mozilla.", "https://www.mozilla.org/"),
        new("Microsoft Edge", "Microsoft.Edge", "Navegadores", "Navegador de Microsoft.", "https://www.microsoft.com/edge"),
        new("Brave", "Brave.Brave", "Navegadores", "Navegador con controles de privacidad.", "https://brave.com/"),
        new("Vivaldi", "Vivaldi.Vivaldi", "Navegadores", "Navegador personalizable.", "https://vivaldi.com/"),
        new("7-Zip", "7zip.7zip", "Herramientas para PC", "Comprimir y extraer archivos.", "https://www.7-zip.org/"),
        new("PowerToys", "Microsoft.PowerToys", "Herramientas para PC", "Utilidades de productividad para Windows.", "https://learn.microsoft.com/windows/powertoys/"),
        new("Everything", "voidtools.Everything", "Herramientas para PC", "Buscar archivos rápidamente.", "https://www.voidtools.com/"),
        new("CPU-Z", "CPUID.CPU-Z", "Herramientas para PC", "Información del procesador y la memoria.", "https://www.cpuid.com/"),
        new("HWiNFO", "REALiX.HWiNFO", "Herramientas para PC", "Sensores e información del hardware.", "https://www.hwinfo.com/"),
        new("CrystalDiskInfo", "CrystalDewWorld.CrystalDiskInfo", "Herramientas para PC", "Información de salud de discos.", "https://crystalmark.info/"),
        new("TreeSize Free", "JAMSoftware.TreeSize.Free", "Herramientas para PC", "Ver qué ocupa espacio en el disco.", "https://www.jam-software.com/"),
        new("VLC", "VideoLAN.VLC", "Multimedia", "Reproductor de video y audio.", "https://www.videolan.org/"),
        new("OBS Studio", "OBSProject.OBSStudio", "Multimedia", "Grabación de pantalla y transmisiones.", "https://obsproject.com/"),
        new("Spotify", "Spotify.Spotify", "Multimedia", "Música y podcasts.", "https://www.spotify.com/"),
        new("Discord", "Discord.Discord", "Comunicación", "Comunidades, voz y videollamadas.", "https://discord.com/"),
        new("Telegram", "Telegram.TelegramDesktop", "Comunicación", "Mensajería de escritorio.", "https://desktop.telegram.org/"),
        new("LibreOffice", "TheDocumentFoundation.LibreOffice", "Productividad", "Documentos, hojas de cálculo y presentaciones.", "https://www.libreoffice.org/"),
        new("Notepad++", "Notepad++.Notepad++", "Desarrollo", "Editor de texto y código.", "https://notepad-plus-plus.org/"),
        new("Visual Studio Code", "Microsoft.VisualStudioCode", "Desarrollo", "Editor de código con extensiones.", "https://code.visualstudio.com/"),
        new("Git", "Git.Git", "Desarrollo", "Control de versiones.", "https://git-scm.com/"),
        new("Steam", "Valve.Steam", "Juegos", "Biblioteca y tienda de juegos.", "https://store.steampowered.com/"),
        new("Epic Games Launcher", "EpicGames.EpicGamesLauncher", "Juegos", "Biblioteca y tienda de Epic Games.", "https://store.epicgames.com/")
    };
    public static IReadOnlyList<AplicacionCatalogo> LeerBusqueda(string salida)
    {
        var lista = new List<AplicacionCatalogo>();
        int id = -1, version = -1, fin = -1;
        foreach (var original in salida.Replace("\r", "").Split('\n'))
        {
            var linea = Regex.Replace(original, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            if (Regex.IsMatch(linea, @"^(Name|Nombre)\s+Id\s+(Version|Versión)"))
            {
                id = Regex.Match(linea, @"\bId\b").Index;
                version = Regex.Match(linea, @"\b(Version|Versión)\b").Index;
                var siguiente = Regex.Match(linea, @"\b(Match|Coincidencia|Source|Origen)\b");
                fin = siguiente.Success ? siguiente.Index : -1;
                continue;
            }
            if (id < 0 || linea.StartsWith('-') || linea.Length <= version) continue;
            var identificador = linea[id..version].Trim();
            var disponible = linea[version..(fin < 0 ? linea.Length : Math.Min(fin, linea.Length))].Trim();
            if (!Regex.IsMatch(identificador, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z") || !identificador.Contains('.') || disponible.Contains('…') || disponible.Length == 0) continue;
            var conocida = Populares.FirstOrDefault(p => p.Id.Equals(identificador, StringComparison.OrdinalIgnoreCase));
            lista.Add(new(linea[..id].Trim(), identificador, conocida?.Categoria ?? "WinGet", conocida?.Descripcion ?? "Aplicación del catálogo de WinGet.", conocida?.Sitio ?? "", disponible));
        }
        return lista.DistinctBy(p => p.Id).ToArray();
    }
    public static string LeerVersion(string salida)
    {
        var version = Regex.Match(salida, @"(?m)^(?:Versión|Version):\s*([^\r\n]+)").Groups[1].Value.Trim();
        if (version.Length == 0 || version.Contains('…')) throw new InvalidOperationException("WinGet no informó una versión concreta para descargar.");
        return version;
    }
}
