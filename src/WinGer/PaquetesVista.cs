using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private IEnumerable<Paquete> Visibles() => paquetes.Where(p => p.Nombre.Contains(filtroPaquetes, StringComparison.OrdinalIgnoreCase) || p.Id.Contains(filtroPaquetes, StringComparison.OrdinalIgnoreCase));
    private void ActualizarContador()
    {
        Modelo.Pendientes = $"{paquetes.Count} apps pendientes";
        if (contadorPaquetes is not null) contadorPaquetes.Text = $"{paquetes.Count} disponibles · {paquetes.Count(p => p.Seleccionado)} seleccionados · Última búsqueda: {ultimaBusqueda?.ToLocalTime().ToString("g") ?? "sin realizar"}";
    }
    private void FiltrarPaquetes() { if (listaPaquetes is not null) listaPaquetes.ItemsSource = Visibles().ToArray(); ActualizarContador(); }
    private Task BuscarPaquetes() => Ejecutar("Consulta de WinGet", async token =>
    {
        var encontrados = await Gestor.BuscarAsync(token);
        foreach (var nuevo in encontrados)
        {
            var anterior = paquetes.FirstOrDefault(p => p.Id == nuevo.Id && p.Disponible == nuevo.Disponible && p.Instalada == nuevo.Instalada);
            if (anterior is null) continue;
            nuevo.Seleccionado = anterior.Seleccionado; nuevo.HashPublicado = anterior.HashPublicado;
            nuevo.Detalle = anterior.Detalle;
            nuevo.ConservarConsultas(anterior);
            nuevo.Estado = anterior.Estado; nuevo.Nivel = anterior.Nivel;
        }
        paquetes.Clear(); paquetes.AddRange(encontrados);
        ultimaBusqueda = DateTimeOffset.Now;
        VincularPaquetes();
        FiltrarPaquetes();
        GuardarCache();
        await ConsultarAutomaticamente(token);
    });
    private Task AnalizarGrupo(Paquete[] seleccion) => Ejecutar("Análisis de instaladores", async token =>
    {
        if (seleccion.Length == 0) throw new InvalidOperationException("Seleccioná al menos una aplicación.");
        var clave = LeerClave(); var fallos = 0;
        for (var i = 0; i < seleccion.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            Modelo.Estado = $"Analizando {i + 1}/{seleccion.Length}: {seleccion[i].Nombre}";
            try { await Gestor.ConsultarPublicadoAsync(seleccion[i], clave, token, forzarConsulta: true); }
            catch (OperationCanceledException) { throw; }
            catch { fallos++; }
            almacen.Registrar("Análisis · " + seleccion[i].Id, seleccion[i].Estado);
            // Respetar la cuota pública habitual de cuatro consultas por minuto.
            if (i < seleccion.Length - 1) await Task.Delay(TimeSpan.FromSeconds(16), token);
        }
        if (fallos > 0) throw new InvalidOperationException($"Análisis finalizado con {fallos} errores. Consultá los detalles de cada tarjeta.");
    });
    private async Task ActualizarGrupo(Paquete[] seleccion)
    {
        if (Modelo.Ocupado) { Modelo.Estado = "Esperá a que termine la operación actual."; return; }
        if (seleccion.Length == 0) { Modelo.Estado = "Seleccioná al menos una aplicación."; return; }
        var confirmacion = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = "Actualizar programas verificados",
            Content = "Se descargarán y comprobarán los instaladores. Si no se puede validar una firma, verás el motivo y podrás decidir si continuar. Los fallos de hash y las detecciones bloquean la instalación. Esto utiliza tu cuota de API y vt-cli. Windows puede pedir permisos y mostrar asistentes.\n\n" + string.Join("\n", seleccion.Select(p => $"{p.Nombre} → {p.Disponible}")) + "\n\nCancelar detiene la cola, no un instalador que ya empezó.",
            PrimaryButtonText = "Actualizar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close };
        if (await confirmacion.ShowAsync() != ContentDialogResult.Primary) return;
        await Ejecutar("Actualización verificada", async token =>
        {
            var clave = LeerClave(); var fallos = 0;
            for (var i = 0; i < seleccion.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                Modelo.Estado = "Actualizando " + seleccion[i].Nombre;
                try
                {
                    if (!PoliticaInstalacion.PuedeInstalar(seleccion[i].Descarga, DateTimeOffset.UtcNow))
                        await Gestor.AnalizarAsync(seleccion[i], clave, token);
                    var paquete = seleccion[i];
                    var aceptarFirma = false;
                    if (!PoliticaInstalacion.PuedeInstalar(paquete.Descarga, DateTimeOffset.UtcNow))
                    {
                        var puedeContinuar = PoliticaInstalacion.PuedeInstalar(paquete.Descarga, DateTimeOffset.UtcNow, true);
                        aceptarFirma = await MostrarMensajePaquete(paquete, PoliticaInstalacion.Motivo(paquete.Descarga, DateTimeOffset.UtcNow), puedeContinuar);
                        if (!aceptarFirma)
                        {
                            fallos++;
                            almacen.Registrar("Actualización · " + paquete.Id, "No se continuó · " + paquete.Estado);
                            continue;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    await Gestor.ActualizarAsync(paquete, clave, token, aceptarFirma);
                    if (paquete.ActualizacionConfirmada)
                    {
                        almacen.RegistrarActualizacion(paquete);
                        paquetes.Remove(paquete);
                        FiltrarPaquetes();
                        GuardarCache();
                    }
                    else fallos++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    fallos++;
                    seleccion[i].Detalle = error.Message;
                    await MostrarMensajePaquete(seleccion[i], error.Message, false);
                }
                almacen.Registrar("Actualización · " + seleccion[i].Id, seleccion[i].Estado);
                if (i < seleccion.Length - 1) await Task.Delay(TimeSpan.FromSeconds(16), token);
            }
            if (fallos > 0) throw new InvalidOperationException($"{fallos} actualizaciones detenidas. Revisá sus detalles.");
        });
    }
    private async Task<bool> MostrarMensajePaquete(Paquete paquete, string motivo, bool puedeContinuar)
    {
        var contenido = new StackPanel { Spacing = 12 };
        contenido.Children.Add(new InfoBar { IsOpen = true, IsClosable = false,
            Severity = puedeContinuar ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
            Title = puedeContinuar ? "Advertencia de firma" : "No se pudo actualizar", Message = motivo });
        if (puedeContinuar) contenido.Children.Add(new TextBlock { Text = "Podés continuar con esta advertencia. El hash coincide y el informe no tiene detecciones, pero eso no garantiza seguridad. Windows mantiene sus comprobaciones al instalar paquetes MSIX.", TextWrapping = TextWrapping.Wrap });
        contenido.Children.Add(new Expander { Header = "Ver detalles técnicos", Content = new TextBlock { Text = paquete.Detalle, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } });
        return await new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = paquete.Nombre,
            Content = new ScrollViewer { MaxHeight = 420, Content = contenido },
            PrimaryButtonText = puedeContinuar ? "Actualizar igualmente" : "",
            CloseButtonText = puedeContinuar ? "Cancelar" : "Cerrar", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;
    }
    private async void AnalizarPaquete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Paquete p) await AnalizarGrupo([p]);
    }
    private async void AbrirInformeWeb(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Paquete p) return;
        await Ejecutar("Abrir informe en VirusTotal", async token =>
        {
            var hash = await Gestor.ObtenerHashAsync(p, token);
            await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://www.virustotal.com/gui/file/{hash}/detection"));
            p.Detalle = $"Hash publicado por WinGet: {hash}\nInforme abierto en el navegador. No se usó la API ni se descargó el instalador. El resultado de la web no se importa automáticamente a WinGer.";
        });
    }
    private async void ActualizarPaquete(object sender, RoutedEventArgs e)
    {
        try { if ((sender as FrameworkElement)?.Tag is Paquete p) await ActualizarGrupo([p]); }
        catch (Exception error) { Modelo.Estado = error.Message; }
    }
    private async void DetallesPaquete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Paquete p) return;
        try
        {
            await new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = p.Nombre,
                Content = new ScrollViewer { MaxHeight = 400, Content = new TextBox { Text = p.DetallesConsultas + "\n\nÚltima operación\n" + p.Detalle, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true } },
                CloseButtonText = "Cerrar" }.ShowAsync();
        }
        catch (Exception error) { Modelo.Estado = error.Message; }
    }
}
