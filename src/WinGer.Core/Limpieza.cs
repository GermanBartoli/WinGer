namespace WinGer.Core;

public sealed record CategoriaLimpieza(string Nombre, string Grupo, string Raiz, int DiasMinimos = 0, string? PatronEnRaiz = null);
public sealed record ArchivoLimpieza(string Ruta, long Bytes, DateTime Modificado, DateTime Creado)
{
    public string Nombre => Path.GetFileName(Ruta);
    public string Tamano => Limpieza.Formato(Bytes);
    public override string ToString() => $"{Nombre} · {Tamano} · {Ruta}";
}
public sealed record AnalisisLimpieza(CategoriaLimpieza Categoria, List<ArchivoLimpieza> Archivos, int Inaccesibles)
{
    public long Bytes { get; } = Archivos.Sum(a => a.Bytes);
}
public sealed record ResultadoLimpieza(int Borrados, long Bytes, int Omitidos, bool Cancelado);

public static class Limpieza
{
    private static readonly string CarpetaExplorador = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
    public static CategoriaLimpieza Miniaturas { get; } = new("Caché de miniaturas", "Windows", CarpetaExplorador, PatronEnRaiz: "thumbcache_*.db");
    public static CategoriaLimpieza Iconos { get; } = new("Caché de iconos", "Windows", CarpetaExplorador, PatronEnRaiz: "iconcache_*.db");

    public static IReadOnlyList<CategoriaLimpieza> Catalogo()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var movil = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var categorias = new List<CategoriaLimpieza>
        {
            new("Temporales de más de 7 días", "Windows", Path.Combine(local, "Temp"), 7),
            new("Caché de sombreadores DirectX", "Windows", Path.Combine(local, "D3DSCache")),
            Miniaturas,
            Iconos,
            new("Caché HTTP de NuGet", "Desarrollo", Path.Combine(local, @"NuGet\v3-cache")),
            new("Caché de VS Code", "Desarrollo", Path.Combine(movil, @"Code\Cache\Cache_Data")),
            new("Caché de código de VS Code", "Desarrollo", Path.Combine(movil, @"Code\Code Cache")),
            new("Caché de Discord", "Aplicaciones", Path.Combine(movil, @"discord\Cache\Cache_Data")),
            new("Caché de GitHub Desktop", "Aplicaciones", Path.Combine(movil, @"GitHub Desktop\Cache\Cache_Data"))
        };
        foreach (var (nombre, carpeta, cache) in new[]
        {
            ("Microsoft Edge", @"Microsoft\Edge\User Data", @"Cache\Cache_Data"),
            ("Google Chrome", @"Google\Chrome\User Data", @"Cache\Cache_Data"),
            ("Brave", @"BraveSoftware\Brave-Browser\User Data", @"Cache\Cache_Data"),
            ("Firefox", @"Mozilla\Firefox\Profiles", @"cache2\entries")
        })
        {
            var perfiles = new DirectoryInfo(Path.Combine(local, carpeta));
            try
            {
                if (!CarpetaNormal(perfiles)) continue;
                foreach (var perfil in perfiles.GetDirectories())
                {
                    if (nombre != "Firefox" && perfil.Name != "Default" && !perfil.Name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)) continue;
                    categorias.Add(new($"{nombre} · {perfil.Name}", "Navegadores", Path.Combine(perfil.FullName, cache)));
                }
            }
            catch (Exception e) when (ErrorDeAcceso(e)) { }
        }
        return categorias;
    }

    private static bool ErrorDeAcceso(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static bool CarpetaNormal(DirectoryInfo? carpeta)
    {
        for (; carpeta != null; carpeta = carpeta.Parent)
            if (!carpeta.Exists || carpeta.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        return true;
    }

    public static AnalisisLimpieza Analizar(CategoriaLimpieza categoria, CancellationToken token)
    {
        var archivos = new List<ArchivoLimpieza>();
        int inaccesibles = 0;
        var pendientes = new Stack<DirectoryInfo>();
        pendientes.Push(new(categoria.Raiz));
        var limite = DateTime.UtcNow.AddDays(-categoria.DiasMinimos);
        while (pendientes.TryPop(out var carpeta))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!CarpetaNormal(carpeta)) continue;
                foreach (var entrada in carpeta.EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if ((entrada.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.ReadOnly)) != 0) continue;
                    if (entrada is DirectoryInfo subcarpeta && categoria.PatronEnRaiz == null) pendientes.Push(subcarpeta);
                    else if (entrada is FileInfo archivo && (categoria.DiasMinimos == 0 || (archivo.LastWriteTimeUtc <= limite && archivo.CreationTimeUtc <= limite)) &&
                        (categoria.PatronEnRaiz == null || System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(categoria.PatronEnRaiz, archivo.Name)))
                        archivos.Add(new(archivo.FullName, archivo.Length, archivo.LastWriteTimeUtc, archivo.CreationTimeUtc));
                }
            }
            catch (Exception e) when (ErrorDeAcceso(e)) { inaccesibles++; }
        }
        archivos.Sort((a,b) => b.Bytes.CompareTo(a.Bytes));
        return new(categoria, archivos, inaccesibles);
    }

    public static ResultadoLimpieza Borrar(IEnumerable<AnalisisLimpieza> seleccion, CancellationToken token)
    {
        int borrados = 0, omitidos = 0;
        long bytes = 0;
        foreach (var grupo in seleccion)
        {
            var prefijo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(grupo.Categoria.Raiz)) + Path.DirectorySeparatorChar;
            foreach (var archivo in grupo.Archivos)
            {
                if (token.IsCancellationRequested) return new(borrados, bytes, omitidos, true);
                try
                {
                    if (Path.GetFullPath(archivo.Ruta).StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) &&
                        CarpetaNormal(new FileInfo(archivo.Ruta).Directory) && BorradoWindows.EliminarArchivoRevisado(archivo))
                    {
                        bytes += archivo.Bytes;
                        borrados++;
                    }
                    else omitidos++;
                }
                catch (Exception e) when (ErrorDeAcceso(e)) { omitidos++; }
            }
        }
        return new(borrados, bytes, omitidos, false);
    }

    public static string Formato(long bytes)
    {
        string[] unidades = ["B", "KB", "MB", "GB", "TB"];
        double cantidad = bytes;
        int unidad = 0;
        while (cantidad >= 1024 && unidad < unidades.Length - 1) { cantidad /= 1024; unidad++; }
        return $"{cantidad:0.##} {unidades[unidad]}";
    }
}
