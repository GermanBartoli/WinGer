using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WinGer;

// Distribuye filas con altura propia, sin recortar tarjetas ni botones.
public sealed class PanelAjustable : Panel
{
    public double AnchoElemento { get; set; } = double.NaN;
    private const double Separacion = 12;

    protected override Size MeasureOverride(Size disponible)
    {
        var ancho = double.IsFinite(disponible.Width) ? disponible.Width : 1200;
        foreach (var hijo in Children)
            hijo.Measure(new Size(double.IsNaN(AnchoElemento) ? ancho : Math.Min(AnchoElemento, ancho), double.PositiveInfinity));
        return Distribuir(ancho, false);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Distribuir(final.Width, true);
        return final;
    }

    private Size Distribuir(double ancho, bool colocar)
    {
        double x = 0, y = 0, altoFila = 0, anchoUsado = 0;
        foreach (var hijo in Children)
        {
            var anchoHijo = Math.Min(ancho, double.IsNaN(AnchoElemento) ? hijo.DesiredSize.Width : AnchoElemento);
            if (x > 0 && x + anchoHijo > ancho)
            {
                x = 0;
                y += altoFila + Separacion;
                altoFila = 0;
            }
            if (colocar) hijo.Arrange(new Rect(x, y, anchoHijo, hijo.DesiredSize.Height));
            anchoUsado = Math.Max(anchoUsado, x + anchoHijo);
            x += anchoHijo + Separacion;
            altoFila = Math.Max(altoFila, hijo.DesiredSize.Height);
        }
        return new Size(anchoUsado, y + altoFila);
    }
}
