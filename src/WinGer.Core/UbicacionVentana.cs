namespace WinGer.Core;

public sealed record UbicacionVentana(int X, int Y, int Ancho, int Alto, bool Maximizada = false)
{
    public UbicacionVentana Ajustar(int xArea, int yArea, int anchoArea, int altoArea)
    {
        var ancho = Math.Clamp(Ancho, 1, Math.Max(1, anchoArea));
        var alto = Math.Clamp(Alto, 1, Math.Max(1, altoArea));
        var x = (int)Math.Clamp((long)X, xArea, (long)xArea + Math.Max(1, anchoArea) - ancho);
        var y = (int)Math.Clamp((long)Y, yArea, (long)yArea + Math.Max(1, altoArea) - alto);
        return this with { X = x, Y = y, Ancho = ancho, Alto = alto };
    }

    public UbicacionVentana Registrar(int x, int y, int ancho, int alto, bool maximizada, bool minimizada)
    {
        if (minimizada) return this;
        if (maximizada) return this with { Maximizada = true };
        return ancho > 0 && alto > 0 ? new(x, y, ancho, alto) : this;
    }
}
