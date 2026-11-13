using WinGer.Core;

static class PruebasDependencias
{
    public static async Task Ejecutar(Action<bool, string> verificar, bool integracion)
    {
        var hash = new string('a', 64);
        var baseYaml = $"PackageIdentifier: Editor.Prueba\nPackageVersion: 2\nInstallerType: wix\nInstallers:\n- Architecture: x64\n  InstallerSha256: {hash}\n";
        var oleDb = baseYaml + "  Dependencies:\n    PackageDependencies:\n    - PackageIdentifier: Microsoft.VCRedist.2015+.x86\n    - PackageIdentifier: Microsoft.VCRedist.2015+.x64\n";
        var datos = Manifiestos.Leer(oleDb, "Editor.Prueba", "2");
        verificar(!datos.SinDependencias && datos.Requisitos.Paquetes.Count == 2, "OLE DB conserva los dos requisitos x86 y x64 del manifiesto");
        string Tabla(string id, string version) => $"Nombre                  Id                                  Versión\n------------------------------------------------------------------------------------\n{ "Prueba",-24}{id,-36}{version}\n";
        var llamadas = new List<string>();
        Task<ResultadoProceso> Instalada(string id, CancellationToken token)
        { llamadas.Add(id); return Task.FromResult(new ResultadoProceso(0, Tabla(id, "14.52.37029.0"))); }
        var estado = await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, Instalada, default);
        verificar(estado.Cumplidas && llamadas.SequenceEqual(new[] { "Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64" }), "comprueba ambas arquitecturas por ID exacto sin instalar nada");
        verificar(estado.Detalle.Contains("x86 · instalada: 14.52.37029.0") && estado.Detalle.Contains("x64 · instalada"), "detalle informa las versiones comprobadas");
        var incompleto = await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, (id, t) => Task.FromResult(new ResultadoProceso(0, id.EndsWith("x86") ? Tabla(id, "14.52") : "Sin resultados")), default);
        verificar(!incompleto.Cumplidas && incompleto.Detalle.Contains("x64 · falta"), "no alcanza tener solo una de las arquitecturas");
        var otroId = await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, (id, t) => Task.FromResult(new ResultadoProceso(0, Tabla("Otro.Editor", "99"))), default);
        verificar(!otroId.Cumplidas, "un paquete distinto nunca satisface una dependencia");
        var error = await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, (id, t) => Task.FromResult(new ResultadoProceso(1, Tabla(id, "99"))), default);
        verificar(!error.Cumplidas && error.Detalle.Contains("no se pudo confirmar"), "error de WinGet bloquea aunque su salida contenga una versión");
        foreach (var (actual, minima, valido) in new[] { ("14.52.37029.0", "14.34", true), ("14.9", "14.10", false), ("1", "1.0.0", true), ("1.0", "1.0.1", false), ("14.52.0.1", "14.52", true), ("Unknown", "", false), ("Desconocido", "", false), ("", "", false), ("v2", "1", false), ("2-beta", "2", false), ("2-beta", "2-beta", true), ("99999999999999999999.1", "99999999999999999999", true) })
            verificar(DependenciasInstalacion.CumpleVersion(actual, minima) == valido, $"compara requisito mínimo {actual} / {minima}");
        var minimoYaml = baseYaml + "Dependencies:\n  PackageDependencies:\n  - PackageIdentifier: Editor.Runtime\n    MinimumVersion: 14.34\n";
        var minimo = Manifiestos.Leer(minimoYaml, "Editor.Prueba", "2");
        verificar(minimo.Requisitos.Paquetes.Single().Minima == "14.34", "hereda versión mínima de dependencias en raíz");
        var faltaVersion = await DependenciasInstalacion.ComprobarAsync(minimo.Requisitos, (id, t) => Task.FromResult(new ResultadoProceso(0, Tabla(id, "14.30"))), default);
        verificar(!faltaVersion.Cumplidas && faltaVersion.Detalle.Contains("mínimo 14.34") && faltaVersion.Detalle.Contains("insuficiente"), "informa qué dependencia requiere una versión superior");
        var actualizada = await DependenciasInstalacion.ComprobarAsync(minimo.Requisitos, Instalada, default);
        verificar(actualizada.Cumplidas, "paquete actualizado satisface versión mínima");
        var reemplazoYaml = minimoYaml.Replace("  InstallerSha256:", "  Dependencies: {}\n  InstallerSha256:");
        verificar(Manifiestos.Leer(reemplazoYaml, "Editor.Prueba", "2").SinDependencias, "dependencias del instalador reemplazan las de raíz");
        foreach (var vacio in new[] { "Dependencies: {}\n", "Dependencies:\n", "Dependencies:\n  PackageDependencies: []\n" })
            verificar(Manifiestos.Leer(baseYaml + vacio, "Editor.Prueba", "2").SinDependencias, "declaración vacía no causa un bloqueo falso");
        var manual = Manifiestos.Leer(minimoYaml + "  WindowsFeatures:\n  - NetFx3\n  ExternalDependencies:\n  - Runtime externo\n", "Editor.Prueba", "2");
        var sinGestion = await DependenciasInstalacion.ComprobarAsync(manual.Requisitos, Instalada, default);
        verificar(!sinGestion.Cumplidas && sinGestion.Detalle.Contains("NetFx3") && sinGestion.Detalle.Contains("Runtime externo"), "características y dependencias externas siguen pendientes con nombre concreto");
        foreach (var malformado in new[] { "Dependencies: texto\n", "Dependencies:\n  PackageDependencies: texto\n", "Dependencies:\n  PackageDependencies:\n  - PackageIdentifier: --id peligroso\n", "Dependencies:\n  PackageDependencies:\n  - PackageIdentifier: Editor.Runtime\n    MinimumVersion: []\n" })
        {
            var rechazo = false;
            try { Manifiestos.Leer(baseYaml + malformado, "Editor.Prueba", "2"); } catch (InvalidOperationException) { rechazo = true; }
            verificar(rechazo, "dependencia malformada se rechaza antes de consultar o instalar");
        }
        using var cancelacion = new CancellationTokenSource(); cancelacion.Cancel();
        var cancelada = false;
        try { await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, (id, t) => throw new Exception("No debe consultar"), cancelacion.Token); }
        catch (OperationCanceledException) { cancelada = true; }
        verificar(cancelada, "cancelación impide comenzar consultas de dependencias");
        var desaparecida = await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, (id, t) => Task.FromResult(new ResultadoProceso(0, "Sin resultados")), default);
        verificar(estado.Cumplidas && !desaparecida.Cumplidas, "revalidación no reutiliza aprobación de una dependencia quitada");
        if (integracion)
        {
            var ejecutor = new EjecutorProcesos();
            var real = await DependenciasInstalacion.ComprobarAsync(datos.Requisitos, (id, t) => ejecutor.EjecutarAsync("winget.exe", ["list", "--id", id, "--exact", "--source", "winget", "--disable-interactivity"], t), default);
            verificar(real.Cumplidas, "integración local: requisitos OLE DB x86 y x64 confirmados sin instalar");
            Console.WriteLine(real.Detalle);
        }
    }
}
