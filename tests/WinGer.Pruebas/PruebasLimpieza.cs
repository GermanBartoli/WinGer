using WinGer.Core;

internal static class PruebasLimpieza
{
    internal static void Ejecutar(Action<bool, string> verificar)
    {
        var prueba = Directory.CreateTempSubdirectory("WinGer-limpieza-");
        try
        {
            var raiz = prueba.CreateSubdirectory("cache");
            var sub = raiz.CreateSubdirectory("subcarpeta");
            var categoria = new CategoriaLimpieza("Temporales", "Prueba", raiz.FullName, 7);
            string Crear(DirectoryInfo carpeta, string nombre, bool antiguo = true)
            {
                string ruta = Path.Combine(carpeta.FullName, nombre);
                File.WriteAllText(ruta, "prueba");
                if (antiguo)
                {
                    var fecha = DateTime.UtcNow.AddDays(-8);
                    File.SetCreationTimeUtc(ruta, fecha);
                    File.SetLastWriteTimeUtc(ruta, fecha);
                }
                return ruta;
            }
            string elegido = Crear(sub, "elegido");
            string reciente = Crear(raiz, "reciente", false);
            string copiaReciente = Crear(raiz, "copia-reciente", false);
            File.SetLastWriteTimeUtc(copiaReciente, DateTime.UtcNow.AddDays(-15));
            string modificado = Crear(raiz, "modificado");
            string ocupado = Crear(raiz, "ocupado");
            string conservado = Crear(raiz, "conservado");
            string externo = Crear(prueba, "externo");
            var explorer = prueba.CreateSubdirectory("explorer");
            var anidada = explorer.CreateSubdirectory("otra");
            string miniatura = Crear(explorer, "thumbcache_256.db");
            Crear(explorer, "ajustes.db");
            Crear(anidada, "thumbcache_32.db");
            var miniaturas = Limpieza.Analizar(new("Miniaturas", "Windows", explorer.FullName, PatronEnRaiz: "thumbcache_*.db"), default);
            verificar(miniaturas.Archivos.Count == 1 && miniaturas.Archivos[0].Ruta == miniatura,
                "el filtro de miniaturas excluye bases ajenas y archivos en subcarpetas");
            var grupo = Limpieza.Analizar(categoria, default);
            verificar(grupo.Archivos.Count == 4 && grupo.Bytes == 24 && File.Exists(elegido), "análisis recursivo de temporales antiguos sin borrar archivos");
            File.AppendAllText(modificado, " cambio");
            string posterior = Crear(raiz, "posterior");
            var seleccion = grupo.Archivos.Where(a => a.Ruta != conservado).ToList();
            var fuera = new FileInfo(externo);
            seleccion.Add(new(externo, fuera.Length, fuera.LastWriteTimeUtc, fuera.CreationTimeUtc));
            var plan = new AnalisisLimpieza(categoria, seleccion, 0);
            var cancelado = Limpieza.Borrar([plan], new CancellationToken(true));
            verificar(cancelado.Cancelado && cancelado.Borrados == 0 && File.Exists(elegido), "cancelación antes del borrado");
            using var bloqueo = File.OpenRead(ocupado);
            var resultado = Limpieza.Borrar([plan], default);
            verificar(resultado.Borrados == 1 && resultado.Bytes == 6 && resultado.Omitidos == 3 && resultado.RutasBorradas.SetEquals([elegido]) &&
                !File.Exists(elegido) && new[] { reciente, copiaReciente, modificado, ocupado, conservado, externo, posterior }.All(File.Exists),
                "borra solo lo elegido y omite archivos externos, modificados, bloqueados o posteriores al análisis");
            verificar(Limpieza.Borrar([plan], default).Borrados == 0, "reintentar la limpieza conserva los archivos restantes");
        }
        finally { prueba.Delete(true); }
    }
}
