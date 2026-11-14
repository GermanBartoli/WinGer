using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InformacionIconoShell
    {
        public IntPtr Icono;
        public int Indice;
        public uint Atributos;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Nombre;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Tipo;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string ruta, uint atributos, out InformacionIconoShell informacion, uint tamaño, uint opciones);

    private static byte[]? LeerIconoArchivo(string ruta)
    {
        SHGetFileInfo(ruta, 0, out var informacion, (uint)Marshal.SizeOf<InformacionIconoShell>(), 0x100);
        if (informacion.Icono == IntPtr.Zero) return null;
        try
        {
            using var icono = System.Drawing.Icon.FromHandle(informacion.Icono);
            using var bitmap = icono.ToBitmap(); using var flujo = new MemoryStream();
            bitmap.Save(flujo, System.Drawing.Imaging.ImageFormat.Png); return flujo.ToArray();
        }
        finally { DestroyIcon(informacion.Icono); }
    }
    private static Grid IconoConCarga(Func<Task<byte[]?>> cargar)
    {
        var contenedor = new Grid { Width = 40, Height = 40 };
        var respaldo = new FontIcon { Glyph = "\uE71D", FontSize = 30 };
        var imagen = new Image { Width = 40, Height = 40 };
        contenedor.Children.Add(respaldo); contenedor.Children.Add(imagen);
        var cargado = false;
        imagen.Loaded += async (_, _) =>
        {
            if (cargado) return; cargado = true;
            try
            {
                var datos = await cargar(); if (datos is null) { cargado = false; return; }
                using var flujo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var escritor = new Windows.Storage.Streams.DataWriter(flujo)) { escritor.WriteBytes(datos); await escritor.StoreAsync(); escritor.DetachStream(); }
                flujo.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(flujo);
                imagen.Source = bitmap; respaldo.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine(error);
                cargado = false;
            }
        };
        return contenedor;
    }
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reservado, uint opciones);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private static readonly object bloqueoIconosShell = new();

    private static Task<byte[]?> CargarIconoArchivo(string ruta)
    {
        var resultado = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // El Shell necesita COM inicializado para resolver los iconos de accesos directos.
        var hilo = new Thread(() =>
        {
            // Las consultas simultáneas del Shell pueden devolver un HICON nulo.
            // Serializar la extracción, manteniendo libre el hilo de la interfaz.
            lock (bloqueoIconosShell)
            {
                var inicializado = CoInitializeEx(IntPtr.Zero, 2) >= 0;
                try { resultado.SetResult(LeerIconoArchivo(ruta)); }
                catch (Exception error) { resultado.SetException(error); }
                finally { if (inicializado) CoUninitialize(); }
            }
        }) { IsBackground = true };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return resultado.Task;
    }
    private Grid IconoArchivo(string ruta) => IconoConCarga(() => CargarIconoArchivo(ruta));
    private Grid IconoAplicacion(AplicacionCatalogo app) => IconoConCarga(async () =>
    {
        var clave = "catalogo:" + app.Id;
        if (!iconos.TryGetValue(clave, out var tarea))
            iconos[clave] = tarea = CargarIconoCatalogo(app);
        return await tarea;
    });
    private static async Task<byte[]?> CargarIconoCatalogo(AplicacionCatalogo app)
    {
        var local = await LeerIconoWindows(new Paquete { Nombre = app.Nombre, Id = app.Id, Instalada = "", Disponible = "" });
        if (local is not null) return local;
        if (!Uri.TryCreate(app.Sitio, UriKind.Absolute, out var sitio) || sitio.Scheme != "https") return null;
        var url = new Uri(sitio, "/favicon.ico");
        var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGer", "IconosCatalogo");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri))) + ".ico");
        byte[] datos;
        if (File.Exists(ruta)) datos = await File.ReadAllBytesAsync(ruta);
        else
        {
            using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var respuesta = await cliente.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            respuesta.EnsureSuccessStatusCode();
            if (respuesta.Content.Headers.ContentLength > 2 * 1024 * 1024) return null;
            using var entrada = await respuesta.Content.ReadAsStreamAsync(); using var memoria = new MemoryStream();
            var bloque = new byte[8192]; int leidos;
            while ((leidos = await entrada.ReadAsync(bloque)) > 0) { if (memoria.Length + leidos > 2 * 1024 * 1024) return null; memoria.Write(bloque, 0, leidos); }
            datos = memoria.ToArray();
        }
        using var original = new MemoryStream(datos); using var salida = new MemoryStream();
        // Conservar el original en disco; la conversión en memoria es PNG sin pérdida para WinUI.
        if (datos.Length > 4 && datos[0] == 0 && datos[1] == 0 && datos[2] == 1 && datos[3] == 0)
        {
            using var icono = new System.Drawing.Icon(original); using var bitmap = icono.ToBitmap();
            bitmap.Save(salida, System.Drawing.Imaging.ImageFormat.Png);
        }
        else { using var bitmap = System.Drawing.Image.FromStream(original); bitmap.Save(salida, System.Drawing.Imaging.ImageFormat.Png); }
        if (!File.Exists(ruta)) await File.WriteAllBytesAsync(ruta, datos);
        return salida.ToArray();
    }
}
