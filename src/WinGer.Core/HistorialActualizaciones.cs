namespace WinGer.Core;

public sealed record ActualizacionRealizada(DateTimeOffset Fecha, string Id, string Nombre,
    string Anterior, string Version, string Resultado);

public sealed partial class Paquete
{
    // También reconoce éxitos guardados por versiones anteriores de WinGer.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ActualizacionConfirmada => Estado.StartsWith("Actualizado · ", StringComparison.Ordinal) ||
        Estado.StartsWith("Guardado · Actualizado · ", StringComparison.Ordinal);
}

public sealed partial class AlmacenLocal
{
    public List<ActualizacionRealizada> LeerActualizaciones() => Leer<List<ActualizacionRealizada>>("actualizaciones.json") ?? [];

    public void RegistrarActualizacion(Paquete paquete, DateTimeOffset? fecha = null)
    {
        if (!paquete.ActualizacionConfirmada) return;
        var historial = LeerActualizaciones();
        if (historial.Any(e => e.Id == paquete.Id && e.Anterior == paquete.Instalada && e.Version == paquete.Disponible)) return;
        historial.Insert(0, new(fecha ?? DateTimeOffset.Now, paquete.Id, paquete.Nombre,
            paquete.Instalada, paquete.Disponible, paquete.Estado.Replace("Guardado · ", "")));
        Escribir("actualizaciones.json", historial);
    }
}
