namespace WinGer.Core;

public static class OrdenPaquetes
{
    private static ResultadoConsulta[] Resultados(Paquete paquete) => new[] { paquete.ResultadoApi, paquete.ResultadoVtai, paquete.ResultadoWeb }.OfType<ResultadoConsulta>().ToArray();
    public static int Detecciones(Paquete paquete) => Resultados(paquete).Select(r => (r.Informe?.Maliciosos ?? 0) + (r.Informe?.Sospechosos ?? 0)).DefaultIfEmpty().Max();
    public static int Grupo(Paquete paquete)
    {
        var resultados = Resultados(paquete);
        if (paquete.Nivel == "rojo" || resultados.Any(r => r.Color == "#F27373")) return 2;
        if (paquete.Nivel == "amarillo" || resultados.Length == 0 || resultados.Any(r => r.Color != "#42C98B")) return 1;
        return 0;
    }
}
