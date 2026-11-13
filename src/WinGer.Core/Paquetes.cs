using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace WinGer.Core;

public sealed partial class Paquete : INotifyPropertyChanged
{
    public required string Nombre { get; init; }
    public required string Id { get; init; }
    public required string Instalada { get; init; }
    public required string Disponible { get; init; }
    private bool seleccionado;
    private string estado = "Sin analizar";
    private string nivel = "gris";
    public bool Seleccionado { get => seleccionado; set { seleccionado = value; Notificar(); } }
    public string Estado { get => estado; set { estado = value; Notificar(); } }
    public string Nivel { get => nivel; set { nivel = value; Notificar(); Notificar(nameof(Color)); } }
    public string Color => Nivel switch { "verde" => "#42C98B", "rojo" => "#F27373", "amarillo" => "#E8B44D", _ => "#9AA9BC" };
    [System.Text.Json.Serialization.JsonIgnore]
    public DescargaVerificada? Descarga { get; set; }
    private string? hashPublicado;
    public string? HashPublicado
    {
        get => hashPublicado;
        set
        {
            if (hashPublicado is not null && !string.Equals(hashPublicado, value, StringComparison.OrdinalIgnoreCase))
                ResultadoApi = ResultadoVtai = ResultadoWeb = null;
            hashPublicado = value;
        }
    }
    public string Detalle { get; set; } = "Todavía no se descargó ni analizó el instalador.";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notificar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}

public static class TablaWinGet
{
    public static string LeerHashPublicado(string salida)
    {
        var coincidencias = Regex.Matches(salida, @"(?im)^\s*(?:Installer|Instalador) SHA256:\s*([a-f0-9]{64})\s*$");
        if (coincidencias.Count != 1) throw new InvalidOperationException("WinGet no publicó un hash SHA-256 único para este instalador.");
        return Hashes.Validar(coincidencias[0].Groups[1].Value);
    }
    public static List<Paquete> Leer(string salida, bool soloActualizaciones = true)
    {
        var paquetes = new List<Paquete>();
        int id = -1, version = -1, disponible = -1, finDisponible = -1;
        foreach (var original in salida.Replace("\r", "").Split('\n'))
        {
            var linea = Regex.Replace(original, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
            var cabecera = Regex.Match(linea, @"^(?:Name|Nombre)\s+(?:Id|ID)\s+(?:Version|Versión)\s*");
            if (cabecera.Success)
            {
                id = Regex.Match(linea, @"\b(?:Id|ID)\b").Index;
                version = Regex.Match(linea, @"\b(?:Version|Versión)\b").Index;
                var siguiente = Regex.Match(linea, @"\b(?:Available|Disponible)\b");
                disponible = siguiente.Success ? siguiente.Index : -1;
                var origen = Regex.Match(linea, @"\b(?:Source|Origen)\b");
                finDisponible = origen.Success ? origen.Index : -1;
                continue;
            }
            if (id < 0 || linea.Length <= version || linea.StartsWith('-')) continue;
            string Cortar(int desde, int hasta) => linea[desde..(hasta < 0 ? linea.Length : Math.Min(hasta, linea.Length))].Trim();
            var identificador = Cortar(id, version);
            if (!Regex.IsMatch(identificador, @"\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z") || !identificador.Contains('.')) continue;
            if (soloActualizaciones && (disponible < 0 || linea.Length <= disponible)) continue;
            var actual = Cortar(version, disponible >= 0 ? disponible : finDisponible);
            var nueva = disponible >= 0 && linea.Length > disponible ? Cortar(disponible, finDisponible) : "";
            if (actual is "Unknown" or "Desconocido" || string.IsNullOrWhiteSpace(actual) || nueva.Contains('…')) continue;
            if (soloActualizaciones && string.IsNullOrWhiteSpace(nueva)) continue;
            if (!paquetes.Any(p => p.Id == identificador)) paquetes.Add(new() { Nombre = linea[..id].Trim(), Id = identificador, Instalada = actual, Disponible = nueva });
        }
        return paquetes;
    }
}

public sealed record DescargaVerificada(string Ruta, string Hash, string Tipo, string Ambito,
    bool SinDependencias, string Firma, InformeVirusTotal? Informe, DateTimeOffset Consultado);

public static class PoliticaInstalacion
{
    public static bool InformeAprobado(InformeVirusTotal? informe, DateTimeOffset ahora) =>
        informe is { Maliciosos: 0, Sospechosos: 0, Fecha: not null } &&
        informe.SinDeteccion + informe.Inofensivos > 0 &&
        informe.Fecha <= ahora.AddMinutes(5) && informe.Fecha >= ahora.AddDays(-30);
    public static bool EsMsix(DescargaVerificada descarga) => descarga.Tipo is "msix" or "appx";
    public static bool FormatoCompatible(DescargaVerificada descarga) => descarga.Tipo switch
    {
        "msix" or "appx" => new[] { ".msix", ".msixbundle", ".appx", ".appxbundle" }.Contains(Path.GetExtension(descarga.Ruta).ToLowerInvariant()),
        "msi" or "wix" => descarga.Ruta.EndsWith(".msi", StringComparison.OrdinalIgnoreCase),
        "exe" or "inno" or "nullsoft" or "burn" => descarga.Ruta.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
    public static bool FirmaConAdvertencia(string firma) => firma is "NotSigned" or "UnknownError" or "NotSupportedFileFormat";
    public static bool PuedeInstalar(DescargaVerificada? descarga, DateTimeOffset ahora, bool aceptarAdvertenciaFirma = false) =>
        descarga is { SinDependencias: true } &&
        (descarga.Firma == "Valid" || (aceptarAdvertenciaFirma && FirmaConAdvertencia(descarga.Firma))) &&
        FormatoCompatible(descarga) && descarga.Consultado >= ahora.AddHours(-1) && InformeAprobado(descarga.Informe, ahora);
    public static string Motivo(DescargaVerificada? descarga, DateTimeOffset ahora)
    {
        if (descarga is null) return "No hay un instalador verificado. Volvé a pulsar Actualizar.";
        if (descarga.Informe is { Maliciosos: > 0 } or { Sospechosos: > 0 }) return "VirusTotal informa detecciones o sospechas. Instalación bloqueada.";
        if (!InformeAprobado(descarga.Informe, ahora)) return "Falta un informe reciente y completo de VirusTotal sin detecciones.";
        if (!FormatoCompatible(descarga)) return $"El formato {descarga.Tipo} todavía no está admitido.";
        if (!descarga.SinDependencias) return "El instalador requiere dependencias o una desinstalación previa que WinGer todavía no gestiona.";
        if (descarga.Consultado < ahora.AddHours(-1)) return "La comprobación venció. Volvé a analizar el instalador.";
        return $"Windows no pudo validar la firma digital ({descarga.Firma}). No se pudo confirmar el editor mediante esta comprobación.";
    }
}

public sealed record DatosManifiesto(string Hash, string Tipo, string Ambito, bool SinDependencias);

public static class Manifiestos
{
    private static YamlNode? Nodo(YamlMappingNode mapa, string clave) => mapa.Children.TryGetValue(new YamlScalarNode(clave), out var valor) ? valor : null;
    private static string Texto(YamlMappingNode mapa, string clave) => (Nodo(mapa, clave) as YamlScalarNode)?.Value ?? "";
    public static DatosManifiesto Leer(string texto, string id, string version)
    {
        var yaml = new YamlStream(); yaml.Load(new StringReader(texto));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode raiz)
            throw new InvalidOperationException("Manifiesto no reconocido.");
        if (Texto(raiz, "PackageIdentifier") != id || Texto(raiz, "PackageVersion") != version)
            throw new InvalidOperationException("El manifiesto no corresponde al programa y versión seleccionados.");
        if (Nodo(raiz, "Installers") is not YamlSequenceNode lista || lista.Children.Count != 1 || lista.Children[0] is not YamlMappingNode instalador)
            throw new InvalidOperationException("No se pudo identificar un único instalador. La actualización queda pendiente.");
        var hash = Hashes.Validar(Texto(instalador, "InstallerSha256"));
        if (hash.Length != 64) throw new InvalidOperationException("El manifiesto requiere SHA-256.");
        var tipo = Texto(instalador, "InstallerType"); if (tipo == "") tipo = Texto(raiz, "InstallerType");
        var ambito = Texto(instalador, "Scope"); if (ambito == "") ambito = Texto(raiz, "Scope");
        // Windows valida las dependencias ya instaladas de MSIX; no se descargan otras automáticamente.
        var sinDependencias = tipo is "msix" or "appx" || (Nodo(raiz, "Dependencies") is null && Nodo(instalador, "Dependencies") is null);
        if (Texto(instalador, "UpgradeBehavior") == "uninstallPrevious" || Texto(raiz, "UpgradeBehavior") == "uninstallPrevious") sinDependencias = false;
        return new(hash, tipo, ambito, sinDependencias);
    }
}

public sealed class ServicioPaquetes(EjecutorProcesos procesos, ServicioVirusTotal virus)
{
    private async Task<InformeVirusTotal?> ConsultarApiAsync(Paquete paquete, string hash, string clave, CancellationToken token, bool forzarConsulta = false)
    {
        try
        {
            var informe = await virus.ConsultarAsync(hash, clave, token, forzarConsulta);
            paquete.RegistrarConsulta("API", hash, informe);
            return informe;
        }
        catch (Exception error) { paquete.RegistrarErrorConsulta("API", error.Message); throw; }
    }
    public async Task<string> ObtenerHashAsync(Paquete paquete, CancellationToken token)
    {
        if (paquete.HashPublicado is not null) return paquete.HashPublicado;
        var respuesta = await procesos.EjecutarAsync("winget.exe", ["show", "--id", paquete.Id, "--exact", "--version", paquete.Disponible, "--source", "winget", "--disable-interactivity"], token);
        if (respuesta.Codigo != 0) throw new InvalidOperationException(respuesta.Salida);
        paquete.HashPublicado = TablaWinGet.LeerHashPublicado(respuesta.Salida);
        return paquete.HashPublicado;
    }
    public async Task ConsultarPublicadoAsync(Paquete paquete, string clave, CancellationToken token, bool forzarConsulta = false)
    {
        paquete.Descarga = null; paquete.Estado = "Consultando hash publicado…"; paquete.Nivel = "gris";
        try
        {
            var hash = await ObtenerHashAsync(paquete, token);
            var informe = await ConsultarApiAsync(paquete, hash, clave, token, forzarConsulta);
            paquete.Detalle = $"Hash publicado por WinGet: {hash}\n\n{informe?.Resumen ?? "No hay informe para este hash."}\n\nNo se descargó ni subió el instalador. Antes de instalar se comprobarán el archivo real y su firma.";
            if (informe is { Maliciosos: > 0 } or { Sospechosos: > 0 }) { paquete.Nivel = "rojo"; paquete.Estado = "Informe con detecciones · bloqueado"; }
            else if (PoliticaInstalacion.InformeAprobado(informe, DateTimeOffset.UtcNow)) { paquete.Nivel = "verde"; paquete.Estado = "Informe sin detecciones · archivo aún sin verificar"; }
            else { paquete.Nivel = "amarillo"; paquete.Estado = informe is null ? "Sin informe existente" : "Informe incompleto o antiguo"; }
        }
        catch (Exception error) { paquete.RegistrarErrorConsulta("API", error.Message); paquete.Nivel = "amarillo"; paquete.Estado = "Consulta pendiente"; paquete.Detalle = error.Message; throw; }
    }
    public async Task<List<Paquete>> BuscarAsync(CancellationToken token)
    {
        var resultado = await procesos.EjecutarAsync("winget.exe", ["list", "--upgrade-available", "--source", "winget", "--disable-interactivity"], token);
        if (resultado.Codigo != 0 && resultado.Codigo != unchecked((int)0x8A15002B)) throw new InvalidOperationException(resultado.Salida);
        return TablaWinGet.Leer(resultado.Salida);
    }
    public async Task<(string Ruta, string Hash, DatosManifiesto Datos)> DescargarVerificadoAsync(Paquete paquete, CancellationToken token)
    {
        var carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGer", "Descargas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        var resultado = await procesos.EjecutarAsync("winget.exe", ["download", "--id", paquete.Id, "--exact", "--version", paquete.Disponible,
            "--source", "winget", "--download-directory", carpeta, "--skip-dependencies", "--accept-package-agreements", "--disable-interactivity"], token);
        if (resultado.Codigo != 0) throw new InvalidOperationException(resultado.Salida);
        var manifiestos = Directory.GetFiles(carpeta, "*.yaml");
        if (manifiestos.Length != 1) throw new InvalidOperationException("La descarga no produjo un manifiesto único.");
        var datos = Manifiestos.Leer(await File.ReadAllTextAsync(manifiestos[0], token), paquete.Id, paquete.Disponible);
        var archivos = Directory.GetFiles(carpeta).Where(p => !p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (archivos.Length != 1) throw new InvalidOperationException("No se puede verificar un único archivo descargado.");
        var ruta = archivos[0];
        var hash = await Hashes.CalcularAsync(ruta, token);
        if (!hash.Equals(datos.Hash, StringComparison.OrdinalIgnoreCase))
        { paquete.Nivel = "rojo"; throw new InvalidOperationException("El hash no coincide con WinGet. Instalación bloqueada."); }
        if (paquete.HashPublicado is not null && !string.Equals(paquete.HashPublicado, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El archivo descargado no corresponde al hash consultado.");
        paquete.HashPublicado = hash;
        return (ruta, hash, datos);
    }
    public async Task AnalizarAsync(Paquete paquete, string clave, CancellationToken token)
    {
        paquete.Descarga = null; paquete.Nivel = "gris"; paquete.Estado = "Descargando…";
        try
        {
            var (ruta, hash, datos) = await DescargarVerificadoAsync(paquete, token);
            paquete.HashPublicado = hash;
            paquete.Estado = "Verificando firma y VirusTotal…";
            var firma = await FirmaAsync(ruta, token);
            var informe = await ConsultarApiAsync(paquete, hash, clave, token);
            var descarga = new DescargaVerificada(ruta, hash, datos.Tipo, datos.Ambito, datos.SinDependencias, firma, informe, DateTimeOffset.UtcNow);
            paquete.Descarga = descarga;
            paquete.Detalle = $"SHA-256: {hash}\nFirma: {firma}\nInstalador: {datos.Tipo}\nDependencias o desinstalación previa: {(datos.Tipo is "msix" or "appx" ? "Windows comprobará las dependencias instaladas" : datos.SinDependencias ? "no declaradas" : "requieren revisión")}\n\n{informe?.Resumen ?? "Sin informe en VirusTotal. No se subió el archivo."}\n\nArchivo: {ruta}";
            if (informe is { Maliciosos: > 0 } or { Sospechosos: > 0 } || firma is "HashMismatch" or "NotTrusted")
            { paquete.Nivel = "rojo"; paquete.Estado = "Bloqueado · detecciones o firma inválida"; }
            else if (PoliticaInstalacion.PuedeInstalar(descarga, DateTimeOffset.UtcNow))
            { paquete.Nivel = "verde"; paquete.Estado = "Sin detecciones · listo para actualizar"; }
            else
            { paquete.Nivel = "amarillo"; paquete.Estado = informe is null ? "Sin informe · pendiente" : firma != "Valid" ? "Firma no válida o ausente · revisar" : "Requiere revisión · no habilitado"; }
        }
        catch (Exception error)
        {
            paquete.Estado = error is OperationCanceledException ? "Análisis cancelado" : "No se pudo comprobar";
            if (paquete.Nivel != "rojo") paquete.Nivel = "amarillo";
            paquete.Detalle = error.Message;
            throw;
        }
    }
    public async Task<string> FirmaAsync(string ruta, CancellationToken token)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var resultado = await procesos.EjecutarAsync(powershell,
            ["-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; (Get-AuthenticodeSignature -LiteralPath $env:WINGER_ARCHIVO).Status.ToString()"],
            token, entorno: new Dictionary<string, string> { ["WINGER_ARCHIVO"] = ruta,
                ["PSModulePath"] = Path.Combine(Path.GetDirectoryName(powershell)!, "Modules") });
        if (resultado.Codigo != 0) throw new InvalidOperationException("No se pudo comprobar la firma digital: " + resultado.Salida);
        return resultado.Salida.Trim();
    }
    public async Task ActualizarAsync(Paquete paquete, string clave, CancellationToken token, bool aceptarAdvertenciaFirma = false)
    {
        var descarga = paquete.Descarga;
        if (!PoliticaInstalacion.PuedeInstalar(descarga, DateTimeOffset.UtcNow, aceptarAdvertenciaFirma)) throw new InvalidOperationException(PoliticaInstalacion.Motivo(descarga, DateTimeOffset.UtcNow));
        token.ThrowIfCancellationRequested();
        paquete.Nivel = "gris"; paquete.Estado = "Revalidando instalador…";
        try
        {
            await using var bloqueo = new FileStream(descarga!.Ruta, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(bloqueo, token)).ToLowerInvariant();
            if (hash != descarga.Hash) throw new InvalidOperationException("El archivo cambió después del análisis. No se ejecutó.");
            var firmaActual = await FirmaAsync(descarga.Ruta, token);
            if (firmaActual != "Valid" && !(aceptarAdvertenciaFirma && firmaActual == descarga.Firma && PoliticaInstalacion.FirmaConAdvertencia(firmaActual)))
                throw new InvalidOperationException("La firma cambió o no permite continuar: " + firmaActual);
            try
            {
                var consulta = await procesos.EjecutarAsync("vt.exe", ["file", hash, "--format", "json"], token, clave);
                if (consulta.Codigo != 0) throw new InvalidOperationException("vt-cli no pudo revalidar el informe. No se ejecutó el instalador.");
                var informe = ServicioVirusTotal.InterpretarCli(consulta.Salida, hash);
                paquete.RegistrarConsulta("vt-cli", hash, informe);
                if (!PoliticaInstalacion.InformeAprobado(informe, DateTimeOffset.UtcNow)) throw new InvalidOperationException("El informe actualizado no permite continuar.");
            }
            catch (Exception error) { paquete.RegistrarErrorConsulta("vt-cli", error.Message); throw; }
            token.ThrowIfCancellationRequested();
            var advertencia = aceptarAdvertenciaFirma ? PoliticaInstalacion.Motivo(descarga, DateTimeOffset.UtcNow) : "";
            if (PoliticaInstalacion.EsMsix(descarga))
            {
                paquete.Estado = "Instalando paquete de Windows…";
                var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                var resultado = await procesos.EjecutarAsync(powershell,
                    ["-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; try { Add-AppxPackage -Path $env:WINGER_ARCHIVO -ErrorAction Stop } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"],
                    CancellationToken.None, entorno: new Dictionary<string, string> { ["WINGER_ARCHIVO"] = descarga.Ruta,
                        ["PSModulePath"] = Path.Combine(Path.GetDirectoryName(powershell)!, "Modules") });
                if (resultado.Codigo != 0) throw new InvalidOperationException("Windows no pudo instalar el paquete. Si está abierto, cerralo y reintentá.\n" + resultado.Salida);
            }
            else
            {
                var msi = descarga.Tipo is "msi" or "wix";
                if (msi != descarga.Ruta.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || (!msi && !descarga.Ruta.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("El tipo de archivo no corresponde al instalador.");
                var inicio = new ProcessStartInfo(msi ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe") : descarga.Ruta)
                { UseShellExecute = true, Verb = descarga.Ambito == "machine" ? "runas" : "open" };
                if (msi) inicio.Arguments = $"/i \"{descarga.Ruta}\" /passive /norestart";
                paquete.Estado = "Instalando · seguí el asistente de Windows";
                using var instalador = Process.Start(inicio) ?? throw new InvalidOperationException("No se inició el instalador.");
                // No interrumpir un instalador iniciado: cancelar se aplica al próximo programa.
                await instalador.WaitForExitAsync(CancellationToken.None);
                if (instalador.ExitCode is not (0 or 3010)) throw new InvalidOperationException($"El instalador terminó con código {instalador.ExitCode}.");
            }
            var comprobacion = await procesos.EjecutarAsync("winget.exe", ["list", "--id", paquete.Id, "--exact", "--source", "winget", "--disable-interactivity"], CancellationToken.None);
            var instalado = TablaWinGet.Leer(comprobacion.Salida, false).FirstOrDefault(p => p.Id == paquete.Id);
            if (comprobacion.Codigo != 0 || instalado?.Instalada != paquete.Disponible)
            { paquete.Nivel = "amarillo"; paquete.Estado = "Instalador finalizado · versión pendiente de confirmar"; }
            else { paquete.Nivel = "verde"; paquete.Estado = "Actualizado · " + instalado.Instalada; paquete.Seleccionado = false; }
            if (aceptarAdvertenciaFirma)
            {
                paquete.Nivel = "amarillo";
                paquete.Estado += " · con advertencia de firma";
                paquete.Detalle = advertencia + "\n\n" + paquete.Detalle;
            }
            paquete.Descarga = null;
        }
        catch (Exception error)
        { paquete.Descarga = null; paquete.Nivel = "rojo"; paquete.Estado = "Actualización detenida"; paquete.Detalle = error.Message; throw; }
    }
}
