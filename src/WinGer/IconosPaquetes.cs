using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Task<byte[]?>> iconos = [];
    private static readonly Lazy<List<(string Nombre, string Ruta)>> iconosInstalados = new(() =>
    {
        var lista = new List<(string, string)>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var vista in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var raiz = RegistryKey.OpenBaseKey(hive, vista);
            using var programas = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (programas is null) continue;
            foreach (var nombre in programas.GetSubKeyNames())
            {
                try
                {
                    using var programa = programas.OpenSubKey(nombre);
                    if (programa?.GetValue("DisplayName") is not string etiqueta) continue;
                    if (programa.GetValue("DisplayIcon") is string ruta && !string.IsNullOrWhiteSpace(ruta))
                        lista.Add((etiqueta, ruta));
                    else if (programa.GetValue("InstallLocation") is string carpeta && Path.IsPathFullyQualified(carpeta) &&
                        !carpeta.StartsWith(@"\\") && Directory.Exists(carpeta))
                    {
                        var ejecutables = Directory.GetFiles(carpeta, "*.exe").Where(p => NormalizarNombre(Path.GetFileNameWithoutExtension(p)).Replace("desktop", "") == NormalizarNombre(etiqueta).Replace("desktop", "")).ToArray();
                        if (ejecutables.Length == 1) lista.Add((etiqueta, ejecutables[0]));
                    }
                }
                catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
            }
        }
        return lista.Distinct().ToList();
    });

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string archivo, int indice, out IntPtr grande, out IntPtr pequeño, uint cantidad);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icono);

    private static string NormalizarNombre(string nombre) => Regex.Replace(nombre.ToLowerInvariant(), @"[\s\d.()_-]+", "");

    private static byte[]? LeerIcono(Paquete paquete)
    {
        try
        {
            var nombre = NormalizarNombre(paquete.Nombre);
            var coincidencias = iconosInstalados.Value.Where(e => e.Nombre.Equals(paquete.Nombre, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (coincidencias.Length == 0)
                coincidencias = iconosInstalados.Value.Where(e => NormalizarNombre(e.Nombre) == nombre).ToArray();
            if (coincidencias.Length == 0)
                coincidencias = iconosInstalados.Value.Where(e => NormalizarNombre(e.Nombre).StartsWith(nombre, StringComparison.Ordinal)).ToArray();
            if (coincidencias.Length != 1) return null;
            var ubicacion = Environment.ExpandEnvironmentVariables(coincidencias[0].Ruta.Trim());
            var indice = 0;
            var sufijo = Regex.Match(ubicacion, @",\s*(-?\d+)\s*$");
            if (sufijo.Success) { indice = int.Parse(sufijo.Groups[1].Value); ubicacion = ubicacion[..sufijo.Index]; }
            ubicacion = ubicacion.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(ubicacion) || ubicacion.StartsWith(@"\\") || !File.Exists(ubicacion)) return null;
            ExtractIconEx(ubicacion, indice, out var grande, out var pequeño, 1);
            try
            {
                var handle = grande != IntPtr.Zero ? grande : pequeño;
                if (handle == IntPtr.Zero) return null;
                using var icono = System.Drawing.Icon.FromHandle(handle);
                using var bitmap = icono.ToBitmap();
                using var memoria = new MemoryStream();
                bitmap.Save(memoria, System.Drawing.Imaging.ImageFormat.Png);
                return memoria.ToArray();
            }
            finally { if (grande != IntPtr.Zero) DestroyIcon(grande); if (pequeño != IntPtr.Zero) DestroyIcon(pequeño); }
        }
        catch { return null; }
    }

    private static async Task<byte[]?> LeerIconoWindows(Paquete paquete)
    {
        var local = await Task.Run(() => LeerIcono(paquete));
        if (local is not null) return local;
        try
        {
            var gestor = new Windows.Management.Deployment.PackageManager();
            var instalado = gestor.FindPackagesForUser("").FirstOrDefault(p => p.Id.Name.Equals(paquete.Id, StringComparison.OrdinalIgnoreCase));
            if (instalado is null) return null;
            var entradas = await instalado.GetAppListEntriesAsync();
            var entrada = entradas.FirstOrDefault();
            if (entrada is null) return null;
            using var flujo = await entrada.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64)).OpenReadAsync();
            using var lector = new Windows.Storage.Streams.DataReader(flujo);
            if (flujo.Size > 4 * 1024 * 1024) return null;
            await lector.LoadAsync((uint)flujo.Size);
            var datos = new byte[(int)flujo.Size];
            lector.ReadBytes(datos);
            return datos;
        }
        catch { return null; }
    }

    private async void CargarIconoPaquete(object sender, RoutedEventArgs args)
    {
        if (sender is not Image imagen || imagen.DataContext is not Paquete paquete) return;
        try
        {
            if (!iconos.TryGetValue(paquete.Id, out var tarea)) iconos[paquete.Id] = tarea = LeerIconoWindows(paquete);
            var datos = await tarea;
            if (datos is null || imagen.DataContext != paquete) return;
            using var flujo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var escritor = new Windows.Storage.Streams.DataWriter(flujo))
            { escritor.WriteBytes(datos); await escritor.StoreAsync(); escritor.DetachStream(); }
            flujo.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(flujo);
            if (imagen.DataContext == paquete)
            {
                imagen.Source = bitmap;
                if (imagen.Parent is Grid contenedor) contenedor.Children.OfType<FontIcon>().First().Visibility = Visibility.Collapsed;
            }
        }
        catch { /* Conservar el icono genérico cuando Windows no proporciona uno. */ }
    }
}
