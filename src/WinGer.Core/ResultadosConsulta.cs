namespace WinGer.Core;

public sealed record ResultadoConsulta(string Hash, string Metodo, InformeVirusTotal? Informe,
    DateTimeOffset Consultado, string? Error = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Resumen => (Informe is null ? Error is null ? "Sin informe" : "N/a" : $"{Informe.Maliciosos} detecciones · {Informe.Sospechosos} sospechas") +
        (Metodo == "Web" && Informe is { Maliciosos: 0, Sospechosos: 0 } ? " · lectura informativa" : "") +
        $" · {Consultado.ToLocalTime():dd/MM HH:mm}" + (Error is null ? "" : " · último intento falló");
    [System.Text.Json.Serialization.JsonIgnore]
    public string Color => Informe is { Maliciosos: > 0 } or { Sospechosos: > 0 } ? "#F27373" :
        Error is not null || Metodo == "Web" || !PoliticaInstalacion.InformeAprobado(Informe, DateTimeOffset.UtcNow) ? "#E8B44D" : "#42C98B";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Detalle => $"Método: {Metodo}\nSHA-256: {Hash}\nConsulta: {Consultado.ToLocalTime():g}\n{Informe?.Resumen ?? "Sin informe existente."}" +
        (Error is null ? "" : "\nÚltimo intento: " + Error) +
        (Metodo == "Web" ? "\nAmarillo en Web: lectura informativa; no verifica el instalador ni habilita su instalación. Cero detecciones no garantiza seguridad." : "");
}

public sealed partial class Paquete
{
    private ResultadoConsulta? resultadoApi;
    private ResultadoConsulta? resultadoVtai;
    private ResultadoConsulta? resultadoWeb;
    public ResultadoConsulta? ResultadoApi { get => resultadoApi; set { resultadoApi = value; NotificarResultados(); } }
    public ResultadoConsulta? ResultadoVtai { get => resultadoVtai; set { resultadoVtai = value; NotificarResultados(); } }
    public ResultadoConsulta? ResultadoWeb { get => resultadoWeb; set { resultadoWeb = value; NotificarResultados(); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string LineaApi => "API / vt-cli: " + (ResultadoApi?.Resumen ?? "N/a");
    [System.Text.Json.Serialization.JsonIgnore]
    public string LineaVtai => "VTAI: " + (ResultadoVtai?.Resumen ?? "N/a");
    [System.Text.Json.Serialization.JsonIgnore]
    public string LineaWeb => "Web: " + (ResultadoWeb?.Resumen ?? "N/a");
    [System.Text.Json.Serialization.JsonIgnore]
    public string ColorApi => ResultadoApi?.Color ?? "#9AA9BC";
    [System.Text.Json.Serialization.JsonIgnore]
    public string ColorVtai => ResultadoVtai?.Color ?? "#9AA9BC";
    [System.Text.Json.Serialization.JsonIgnore]
    public string ColorWeb => ResultadoWeb?.Color ?? "#9AA9BC";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DetallesConsultas => string.Join("\n\n", new[] {
        "API / vt-cli\n" + (ResultadoApi?.Detalle ?? "N/a · todavía no consultado"),
        "VTAI\n" + (ResultadoVtai?.Detalle ?? "N/a · todavía no consultado"),
        "Web\n" + (ResultadoWeb?.Detalle ?? "N/a · todavía no leído") });
    private void NotificarResultados()
    {
        foreach (var nombre in new[] { nameof(ResultadoApi), nameof(ResultadoVtai), nameof(ResultadoWeb),
            nameof(LineaApi), nameof(LineaVtai), nameof(LineaWeb), nameof(ColorApi), nameof(ColorVtai), nameof(ColorWeb), nameof(DetallesConsultas) }) Notificar(nombre);
    }
    public void RegistrarConsulta(string metodo, string hash, InformeVirusTotal? informe)
    {
        if (HashPublicado is not null && !string.Equals(HashPublicado, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El resultado no corresponde al hash de esta tarjeta.");
        var resultado = new ResultadoConsulta(hash, metodo, informe, DateTimeOffset.Now);
        switch (metodo)
        {
            case "API": case "vt-cli": ResultadoApi = resultado; break;
            case "VTAI": ResultadoVtai = resultado; break;
            case "Web": ResultadoWeb = resultado; break;
            default: throw new ArgumentException("Método de consulta desconocido.");
        }
    }
    public void RegistrarErrorConsulta(string metodo, string error)
    {
        ResultadoConsulta Actualizar(ResultadoConsulta? anterior) => anterior is null
            ? new(HashPublicado ?? "", metodo, null, DateTimeOffset.Now, error)
            : anterior with { Error = error };
        switch (metodo)
        {
            case "API": case "vt-cli": ResultadoApi = Actualizar(ResultadoApi); break;
            case "VTAI": ResultadoVtai = Actualizar(ResultadoVtai); break;
            case "Web": ResultadoWeb = Actualizar(ResultadoWeb); break;
            default: throw new ArgumentException("Método de consulta desconocido.");
        }
    }
    // Solo completa resultados faltantes: los informes existentes, incluso antiguos o con detecciones,
    // se conservan. La consulta normal permite renovarlos y la instalación valida su vigencia aparte.
    public bool NecesitaConsultaApi() => ResultadoApi is not { Error: null, Informe: not null } resultado ||
        resultado.Metodo is not ("API" or "vt-cli") || string.IsNullOrEmpty(HashPublicado) ||
        !string.Equals(resultado.Hash, HashPublicado, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(resultado.Informe.Hash, HashPublicado, StringComparison.OrdinalIgnoreCase);

    public void ConservarConsultas(Paquete anterior)
    {
        if (Id != anterior.Id || Disponible != anterior.Disponible || Instalada != anterior.Instalada ||
            (HashPublicado is not null && HashPublicado != anterior.HashPublicado)) return;
        ResultadoApi = anterior.ResultadoApi; ResultadoVtai = anterior.ResultadoVtai; ResultadoWeb = anterior.ResultadoWeb;
    }
}
