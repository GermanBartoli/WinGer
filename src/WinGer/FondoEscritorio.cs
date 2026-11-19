using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WinGer;

internal static class FondoEscritorio
{
    public static string? RutaActual => Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;

    private static string Carpeta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGer", "Fondo");

    public static bool UsaCopiaLocal()
    {
        var ruta = RutaActual;
        return File.Exists(ruta) && string.Equals(Path.GetDirectoryName(ruta), Carpeta, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AplicarConfiguracion(uint accion, uint parametro, string ruta, uint opciones);

    public static void Restaurar(string imagen)
    {
        using var origen = File.OpenRead(imagen);
        using var foto = System.Drawing.Image.FromStream(origen, false, true);
        string extension = new System.Drawing.ImageFormatConverter().ConvertToInvariantString(foto.RawFormat) switch
        {
            "Jpeg" => ".jpg",
            "Png" => ".png",
            "Bmp" => ".bmp",
            _ => throw new InvalidDataException("Elegí una imagen JPG, PNG o BMP.")
        };
        origen.Position = 0;
        var carpeta = Carpeta;
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, Convert.ToHexString(SHA256.HashData(origen)) + extension);
        if (!File.Exists(destino))
        {
            var temporal = Path.Combine(carpeta, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                origen.Position = 0;
                using (var copia = File.Create(temporal)) origen.CopyTo(copia);
                File.Move(temporal, destino);
            }
            finally { File.Delete(temporal); }
        }
        if (!AplicarConfiguracion(20, 0, destino, 3))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
