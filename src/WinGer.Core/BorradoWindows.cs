using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinGer.Core;

internal static class BorradoWindows
{
    // Se valida y elimina con el mismo identificador exclusivo, incluso si cambia la ruta durante la limpieza.
    internal static bool EliminarArchivoRevisado(ArchivoLimpieza archivo)
    {
        const uint eliminar = 0x10000, leerAtributos = 0x80, abrirExistente = 3, abrirEnlace = 0x00200000;
        using var handle = CreateFileW(archivo.Ruta, eliminar | leerAtributos, 0, IntPtr.Zero, abrirExistente, abrirEnlace, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) return false;
        const FileAttributes excluidos = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.ReadOnly;
        if ((info.Atributos & excluidos) != 0 || info.Enlaces != 1 ||
            (((long)info.TamanoAlto << 32) | info.TamanoBajo) != archivo.Bytes ||
            info.Modificado.Valor != archivo.Modificado.ToFileTimeUtc() || info.Creado.Valor != archivo.Creado.ToFileTimeUtc()) return false;

        var ruta = new StringBuilder(archivo.Ruta.Length + 16);
        uint longitud = GetFinalPathNameByHandleW(handle, ruta, (uint)ruta.Capacity, 0);
        if (longitud == 0 || longitud >= ruta.Capacity ||
            !string.Equals(ruta.ToString(), @"\\?\" + archivo.Ruta, StringComparison.OrdinalIgnoreCase)) return false;
        byte borrar = 1;
        const int informacionDeEliminacion = 4;
        return SetFileInformationByHandle(handle, informacionDeEliminacion, ref borrar, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Fecha
    {
        public uint Baja, Alta;
        public readonly long Valor => ((long)Alta << 32) | Baja;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Informacion
    {
        public FileAttributes Atributos;
        public Fecha Creado, Acceso, Modificado;
        public uint Volumen, TamanoAlto, TamanoBajo, Enlaces, IndiceAlto, IndiceBajo;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string ruta, uint acceso, uint compartir, IntPtr seguridad, uint modo, uint opciones, IntPtr plantilla);
    [DllImport("kernel32.dll")]
    private static extern bool GetFileInformationByHandle(SafeFileHandle archivo, out Informacion informacion);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle archivo, StringBuilder ruta, uint capacidad, uint opciones);
    [DllImport("kernel32.dll")]
    private static extern bool SetFileInformationByHandle(SafeFileHandle archivo, int clase, ref byte informacion, uint longitud);
}
