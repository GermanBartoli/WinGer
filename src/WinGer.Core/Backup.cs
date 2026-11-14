using System.Text.Json;

namespace WinGer.Core;

public sealed class BackupWinGer
{
    public string Formato { get; set; } = "WinGer-backup-1";
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.Now;
    public required Configuracion Configuracion { get; set; }
    public required CacheSesion Cache { get; set; }
    public required List<EntradaHistorial> Historial { get; set; }
    public required List<ActualizacionRealizada> Actualizaciones { get; set; }
    public required Dictionary<string, ResultadoReparacion> Reparaciones { get; set; }
    public required Dictionary<string, InformeGuardado> Informes { get; set; }
    public required Dictionary<string, InformeGuardado> InformesVtai { get; set; }
}

public sealed partial class AlmacenLocal
{
    public void CrearBackup(string ruta) => File.WriteAllText(ruta, JsonSerializer.Serialize(new BackupWinGer
    {
        Configuracion = LeerConfiguracion(), Cache = LeerCache(), Historial = LeerHistorial(),
        Actualizaciones = LeerActualizaciones(), Reparaciones = LeerReparaciones(),
        Informes = LeerInformes(), InformesVtai = LeerInformesVtai()
    }, Opciones));

    public Configuracion ImportarBackup(string ruta)
    {
        var copia = JsonSerializer.Deserialize<BackupWinGer>(File.ReadAllText(ruta)) ?? throw new InvalidDataException("Backup vacío.");
        if (copia.Formato != "WinGer-backup-1" || copia.Configuracion is null || copia.Cache?.Paquetes is null || copia.Historial is null || copia.Actualizaciones is null || copia.Reparaciones is null || copia.Informes is null || copia.InformesVtai is null)
            throw new InvalidDataException("El archivo no es un backup compatible de WinGer.");
        if (copia.Configuracion.Tema is not ("Predeterminado" or "Oscuro" or "Claro") || copia.Configuracion.ConsultaAutomatica is not ("VTAI" or "Web experimental" or "Desactivado"))
            throw new InvalidDataException("El backup contiene opciones no válidas.");
        // Una descarga importada nunca autoriza instalar un archivo local sin volver a verificarlo.
        foreach (var paquete in copia.Cache.Paquetes) paquete.Descarga = null;
        var valores = new Dictionary<string, object> { ["configuracion.json"] = copia.Configuracion, ["cache-sesion.json"] = copia.Cache,
            ["historial.json"] = copia.Historial, ["actualizaciones.json"] = copia.Actualizaciones, ["reparaciones.json"] = copia.Reparaciones,
            ["cache-informes.json"] = copia.Informes, ["cache-vtai.json"] = copia.InformesVtai };
        var anteriores = valores.Keys.ToDictionary(n => n, n => File.Exists(Path.Combine(carpeta, n)) ? File.ReadAllText(Path.Combine(carpeta, n)) : null);
        try { foreach (var par in valores) Escribir(par.Key, par.Value); }
        catch
        {
            foreach (var par in anteriores)
                if (par.Value is null) File.Delete(Path.Combine(carpeta, par.Key)); else File.WriteAllText(Path.Combine(carpeta, par.Key), par.Value);
            throw;
        }
        return copia.Configuracion;
    }
}
