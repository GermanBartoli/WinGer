using System.Numerics;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace WinGer.Core;

public sealed record DependenciaPaquete(string Id, string Minima);
public sealed record RequisitosInstalacion(IReadOnlyList<DependenciaPaquete> Paquetes, IReadOnlyList<string> NoComprobables)
{
    public bool Vacio => Paquetes.Count == 0 && NoComprobables.Count == 0;
    public static RequisitosInstalacion Leer(YamlNode? nodo)
    {
        if (nodo is null || nodo is YamlScalarNode { Value: null or "" }) return new([], []);
        if (nodo is not YamlMappingNode mapa) throw new InvalidOperationException("La declaración de dependencias no tiene un formato reconocido.");
        var paquetes = new List<DependenciaPaquete>();
        var pendientes = new List<string>();
        foreach (var campo in mapa.Children)
        {
            var nombre = (campo.Key as YamlScalarNode)?.Value ?? "Dependencia desconocida";
            if (campo.Value is YamlScalarNode { Value: null or "" }) continue;
            if (campo.Value is not YamlSequenceNode lista) throw new InvalidOperationException("La dependencia " + nombre + " no contiene una lista válida.");
            if (nombre != "PackageDependencies")
            {
                foreach (var valor in lista.Children) pendientes.Add(nombre + ": " + valor);
                continue;
            }
            foreach (var valor in lista.Children)
            {
                if (valor is not YamlMappingNode paquete) throw new InvalidOperationException("Hay una dependencia de paquete no reconocida.");
                string Texto(string clave) => paquete.Children.TryGetValue(new YamlScalarNode(clave), out var texto) && texto is YamlScalarNode escalar ? escalar.Value ?? "" : "";
                var id = Texto("PackageIdentifier");
                if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z") || !id.Contains('.'))
                    throw new InvalidOperationException("La dependencia no contiene un identificador WinGet válido.");
                if (paquete.Children.Keys.Any(k => k.ToString() is not ("PackageIdentifier" or "MinimumVersion")))
                    pendientes.Add("Requisitos no reconocidos de " + id);
                if (paquete.Children.TryGetValue(new YamlScalarNode("MinimumVersion"), out var minimo) && minimo is not YamlScalarNode)
                    throw new InvalidOperationException("La versión mínima de " + id + " no es válida.");
                paquetes.Add(new(id, Texto("MinimumVersion")));
            }
        }
        return new(paquetes, pendientes);
    }
}

public sealed record ComprobacionDependencias(bool Cumplidas, string Detalle);
public static class DependenciasInstalacion
{
    public static bool CumpleVersion(string instalada, string minima)
    {
        if (string.IsNullOrWhiteSpace(instalada) || instalada is "Unknown" or "Desconocido") return false;
        if (minima == "" || instalada == minima) return true;
        // No adivinar el orden de versiones comerciales o sufijos que WinGet puede comparar de otra forma.
        if (!Regex.IsMatch(instalada, @"\A[0-9]+(?:\.[0-9]+)*\z") || !Regex.IsMatch(minima, @"\A[0-9]+(?:\.[0-9]+)*\z")) return false;
        var actual = instalada.Split('.').Select(BigInteger.Parse).ToArray();
        var requerida = minima.Split('.').Select(BigInteger.Parse).ToArray();
        for (var i = 0; i < Math.Max(actual.Length, requerida.Length); i++)
        {
            var comparacion = (i < actual.Length ? actual[i] : BigInteger.Zero).CompareTo(i < requerida.Length ? requerida[i] : BigInteger.Zero);
            if (comparacion != 0) return comparacion > 0;
        }
        return true;
    }
    public static async Task<ComprobacionDependencias> ComprobarAsync(RequisitosInstalacion requisitos,
        Func<string, CancellationToken, Task<ResultadoProceso>> consultar, CancellationToken token)
    {
        var cumplidas = requisitos.NoComprobables.Count == 0;
        var detalles = requisitos.NoComprobables.Select(p => "Requiere revisión manual: " + p).ToList();
        foreach (var grupo in requisitos.Paquetes.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var respuesta = await consultar(grupo.Key, token);
            var instalados = TablaWinGet.Leer(respuesta.Salida, false).Where(p => p.Id.Equals(grupo.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var dependencia in grupo)
            {
                var satisfecha = respuesta.Codigo == 0 && instalados.Any(p => CumpleVersion(p.Instalada, dependencia.Minima));
                cumplidas &= satisfecha;
                var minimo = dependencia.Minima == "" ? "" : " · mínimo " + dependencia.Minima;
                var versiones = string.Join(", ", instalados.Select(p => p.Instalada));
                detalles.Add(dependencia.Id + minimo + (satisfecha ? " · instalada: " + versiones : respuesta.Codigo != 0 ? " · no se pudo confirmar su instalación" : instalados.Length == 0 ? " · falta o su versión es desconocida" : " · versión insuficiente o no comparable: " + versiones));
            }
        }
        return new(cumplidas, detalles.Count == 0 ? "No declaradas." : string.Join("\n", detalles));
    }
}
