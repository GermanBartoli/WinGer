using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private static void MarcarWebPendiente(Paquete paquete)
    {
        paquete.RegistrarErrorConsulta("Web", "Lectura dejada pendiente por el usuario.");
        paquete.Estado = "Web · lectura pendiente";
        paquete.Nivel = "amarillo";
        paquete.Detalle = "La lectura web quedó pendiente. Se conservan los resultados anteriores; podés volver a consultarla.";
    }

    private async Task<bool> ConsultarWeb(Paquete paquete, CancellationToken token)
    {
        var hash = await Gestor.ObtenerHashAsync(paquete, token);
        var url = $"https://www.virustotal.com/gui/file/{hash}/detection";
        var control = new ControlLecturaWeb(configuracion.PasarWebAutomaticamente);
        var automatico = new CheckBox { Content = "Pasar automáticamente", IsChecked = control.PasarAutomaticamente,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var web = new VentanaWeb("VirusTotal web · " + paquete.Nombre, () => automatico.IsChecked = false, url);
        var omitirTodas = false;
        using var lectura = CancellationTokenSource.CreateLinkedTokenSource(token);
        web.Ventana.Closed += (_, _) => lectura.Cancel();
        foreach (var ancho in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            web.Pie.ColumnDefinitions.Add(new ColumnDefinition { Width = ancho });
        var todas = new Button { Content = "Dejar pendientes todas" };
        todas.Click += (_, _) => { omitirTodas = true; web.Ventana.Close(); };
        var seguir = new Button { Content = "Dejar pendiente y seguir" };
        seguir.Click += (_, _) => web.Ventana.Close();
        web.Pie.Children.Add(todas);
        Grid.SetColumn(automatico, 1); web.Pie.Children.Add(automatico);
        Grid.SetColumn(seguir, 2); web.Pie.Children.Add(seguir);
        void CambiarAutomatico(object sender, RoutedEventArgs args)
        {
            control.PasarAutomaticamente = automatico.IsChecked == true;
            GuardarAjuste(() => configuracion.PasarWebAutomaticamente = control.PasarAutomaticamente);
        }
        automatico.Checked += CambiarAutomatico; automatico.Unchecked += CambiarAutomatico;
        web.Aviso.Text = "Desmarcá Pasar automáticamente para revisar, iniciar sesión o votar sin que se cierre la página. El resultado se guarda igualmente.";
        web.Ventana.Activate();
        try
        {
            await web.Iniciar(url, lectura.Token);
            var intentos = 0;
            while (true)
            {
                await Task.Delay(1000, lectura.Token);
                if (control.DebeAvanzar) return true;
                // La pausa y la navegación a la cuenta no consumen el tiempo de lectura del informe.
                if (control.ResultadoDisponible || web.Navegador.Source?.AbsoluteUri != url) continue;
                var json = await web.Navegador.ExecuteScriptAsync(LecturaInformeWeb.Script);
                lectura.Token.ThrowIfCancellationRequested();
                var informe = LecturaInformeWeb.Interpretar(json, hash);
                if (informe is null && !LecturaInformeWeb.EsInformeAusente(json, hash))
                {
                    if (control.PasarAutomaticamente && ++intentos >= 90)
                        throw new InvalidOperationException("No se pudo leer un informe completo. Queda pendiente; podés volver a intentar con el paso automático desmarcado.");
                    continue;
                }
                paquete.RegistrarConsulta("Web", hash, informe);
                paquete.Descarga = null;
                paquete.Nivel = paquete.ColorWeb == "#42C98B" ? "verde" : paquete.ColorWeb == "#F27373" ? "rojo" : "amarillo";
                paquete.Estado = informe is null ? "Web · sin informe" : $"Web · {informe.Maliciosos} detecciones · {informe.Sospechosos} sospechas";
                paquete.Detalle = $"Lectura web · {DateTimeOffset.Now:g}\nSHA-256: {hash}\n\n{informe?.Resumen ?? "VirusTotal muestra Item not found: sin informe existente."}\n\n{informe?.Json}\n\nLectura informativa. No habilita la instalación ni verifica el archivo real.";
                GuardarCache();
                control.RegistrarResultado();
                seguir.Content = "Continuar con la siguiente";
                web.Aviso.Text = paquete.Estado + " · Resultado guardado. Podés revisar o votar; para avanzar usá Continuar o activá Pasar automáticamente.";
                if (control.DebeAvanzar) return true;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (!control.ResultadoDisponible) MarcarWebPendiente(paquete);
            return !omitirTodas;
        }
        catch (Exception error)
        {
            paquete.RegistrarErrorConsulta("Web", error.Message);
            paquete.Estado = "Web · pendiente de lectura"; paquete.Nivel = "amarillo";
            paquete.Detalle = error.Message;
            throw;
        }
        finally { web.Cerrar(); }
    }
}
