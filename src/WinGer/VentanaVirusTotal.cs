using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinGer;

public sealed partial class MainWindow
{
    private sealed class VentanaWeb
    {
        public Window Ventana { get; } = new();
        public WebView2 Navegador { get; } = new();
        public TextBlock Aviso { get; } = Texto("Cargando VirusTotal…");
        public Grid Pie { get; } = new() { ColumnSpacing = 16 };
        private readonly StackPanel botones = Fila();
        public bool Cerrada { get; private set; }

        public VentanaWeb(string titulo, Action? alIniciarSesion = null, string? informe = null)
        {
            Ventana.Title = titulo;
            Ventana.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "WinGer.ico"));
            var area = DisplayArea.GetFromWindowId(Ventana.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            Ventana.AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min(1280, area.Width - 32), Math.Min(900, area.Height - 32)));
            var raiz = new Grid { Padding = new Thickness(16), RowSpacing = 10, Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] };
            foreach (var alto in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                raiz.RowDefinitions.Add(new RowDefinition { Height = alto });
            var barra = new StackPanel { Spacing = 6 };
            void Agregar(string texto, Action accion)
            {
                var boton = new Button { Content = texto, IsEnabled = false };
                boton.Click += (_, _) => { if (!Cerrada) accion(); };
                botones.Children.Add(boton);
            }
            Agregar("Atrás", () => { if (Navegador.CoreWebView2?.CanGoBack == true) Navegador.CoreWebView2.GoBack(); });
            Agregar("Recargar", () => Navegador.CoreWebView2?.Reload());
            Agregar("Mi cuenta / Iniciar sesión", () => { alIniciarSesion?.Invoke(); Navegador.Source = new Uri("https://www.virustotal.com/gui/sign-in"); });
            if (informe is not null) Agregar("Volver al informe", () => Navegador.Source = new Uri(informe));
            barra.Children.Add(botones);
            var direccion = new TextBox { IsReadOnly = true, Text = "https://www.virustotal.com", PlaceholderText = "Dirección de la página" };
            Navegador.NavigationCompleted += (_, _) => direccion.Text = Navegador.Source?.AbsoluteUri ?? "";
            barra.Children.Add(direccion);
            raiz.Children.Add(barra);
            Grid.SetRow(Aviso, 1); raiz.Children.Add(Aviso);
            Grid.SetRow(Navegador, 2); raiz.Children.Add(Navegador);
            Grid.SetRow(Pie, 3); raiz.Children.Add(Pie);
            Ventana.Content = raiz;
            Ventana.Closed += (_, _) => Cerrada = true;
        }

        public async Task Iniciar(string url, CancellationToken token)
        {
            var entorno = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGer", "WebView2"), null);
            token.ThrowIfCancellationRequested();
            await Navegador.EnsureCoreWebView2Async(entorno);
            token.ThrowIfCancellationRequested();
            // La cuenta y los informes usan el mismo perfil persistente; no se captura la contraseña.
            Navegador.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var destino) && destino.Scheme == "https") Navegador.Source = destino;
            };
            Navegador.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            Navegador.Source = new Uri(url);
            foreach (var boton in botones.Children.OfType<Button>()) boton.IsEnabled = true;
        }

        public void Cerrar()
        {
            if (!Cerrada) Ventana.Close();
            Navegador.Close();
        }
    }

    private async Task AbrirCuentaVirusTotal(CancellationToken token)
    {
        var web = new VentanaWeb("VirusTotal · Mi cuenta");
        var cerrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        web.Ventana.Closed += (_, _) => cerrada.TrySetResult();
        using var carga = CancellationTokenSource.CreateLinkedTokenSource(token);
        web.Ventana.Closed += (_, _) => carga.Cancel();
        var cerrar = new Button { Content = "Cerrar y conservar sesión", HorizontalAlignment = HorizontalAlignment.Right };
        cerrar.Click += (_, _) => web.Ventana.Close();
        web.Pie.Children.Add(cerrar);
        web.Aviso.Text = "Ingresá tu usuario y contraseña en VirusTotal. La sesión se comparte con Leer Web. Podés navegar, votar o cerrar sesión desde tu cuenta.";
        web.Ventana.Activate();
        try
        {
            await web.Iniciar("https://www.virustotal.com/gui/sign-in", carga.Token);
            await cerrada.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (web.Cerrada && !token.IsCancellationRequested) { }
        finally { web.Cerrar(); }
    }
}
