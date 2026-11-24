using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private LimpiezaVista? vistaLimpieza;

    private LimpiezaVista Limpiador => vistaLimpieza ??= new LimpiezaVista(Ejecutar, CrearSelectorTema(), () => configuracion.Tema);

    private Task RevisarCacheExplorador(CategoriaLimpieza categoria)
    {
        if (Modelo.Ocupado || reparacionEnCurso)
        {
            Modelo.Estado = "Esperá a que termine la operación actual.";
            return Task.CompletedTask;
        }
        Navegacion.SelectedItem = Navegacion.MenuItems.OfType<NavigationViewItem>().First(i => Equals(i.Tag, "limpieza"));
        return Limpiador.AnalizarAsync(categoria);
    }

    private void MostrarLimpieza()
    {
        Encabezado("Limpieza", "Abrí una categoría para revisar sus archivos o marcala para borrar el grupo completo.");
        Contenido.Children.Add(Limpiador);
    }
}

public sealed record FamiliaLimpieza(string Nombre, string Icono)
{
    public override string ToString() => Nombre;
}

public sealed partial class LimpiezaVista : UserControl
{
    private readonly Func<string, Func<CancellationToken, Task>, Task> ejecutar;
    private readonly HashSet<AnalisisLimpieza> marcados = [];
    private List<AnalisisLimpieza> grupos = [];
    private bool ocupado;

    public LimpiezaVista(Func<string, Func<CancellationToken, Task>, Task> ejecutar, ComboBox tema, Func<string> leerTema)
    {
        this.ejecutar = ejecutar;
        InitializeComponent();
        Opciones.Children.Add(tema);
        Opciones.Loaded += (_, _) => tema.SelectedItem = leerTema();
        FamiliaLimpieza[] familias = [new("Todas", "\uE8A9"), new("Windows", "\uE7F4"), new("Navegadores", "\uE774"),
            new("Desarrollo", "\uE943"), new("Aplicaciones", "\uECAA")];
        Familias.ItemsSource = familias;
        FamiliasCompactas.ItemsSource = familias.Select(f => f.Nombre).ToArray();
        Familias.SelectedIndex = 0;
        SizeChanged += (_, e) => AjustarAncho(e.NewSize.Width);
    }

    private void AjustarAncho(double ancho)
    {
        bool compacto = ancho < 650;
        Menu.Visibility = compacto ? Visibility.Collapsed : Visibility.Visible;
        FamiliasCompactas.Visibility = compacto ? Visibility.Visible : Visibility.Collapsed;
        AnchoMenu.Width = new GridLength(compacto ? 0 : 152);
        Grid.SetRow(Acciones, compacto ? 1 : 2);
        Grid.SetRow(Explorador, compacto ? 2 : 1);
        ListaResultados.Height = compacto ? 195 : 280;
        Grid.SetColumn(Resultados, compacto ? 0 : 1);
        Grid.SetColumnSpan(Resultados, compacto ? 2 : 1);
        Grid.SetRow(Resultados, compacto ? 1 : 0);
        Grid.SetRowSpan(Resultados, compacto ? 1 : 2);
    }

    private void CambiarFamilia(object sender, SelectionChangedEventArgs e)
    {
        int indice = ReferenceEquals(sender, Familias) ? Familias.SelectedIndex : FamiliasCompactas.SelectedIndex;
        if (indice < 0) return;
        Familias.SelectedIndex = FamiliasCompactas.SelectedIndex = indice;
        ActualizarFilas();
    }

    private async void IniciarAnalisis(object sender, RoutedEventArgs e) => await AnalizarAsync();

    internal async Task AnalizarAsync(CategoriaLimpieza? categoria = null)
    {
        await ejecutar("Análisis de limpieza", async token =>
        {
            Ocupado(true);
            var tituloAnterior = TituloEstado.Text;
            var estadoAnterior = Estado.Text;
            var avanceAnterior = Avance.Value;
            try
            {
                TituloEstado.Text = "Analizando archivos";
                Avance.Value = 0;
                var progreso = new Progress<(string Nombre, double Porcentaje)>(p =>
                {
                    if (!ocupado) return;
                    Estado.Text = p.Nombre;
                    Avance.Value = p.Porcentaje;
                });
                grupos = await Task.Run(() => AnalizarCategorias(categoria == null ? Limpieza.Catalogo() : [categoria], progreso, token), token);
                if (categoria != null) Familias.SelectedIndex = 0;
                RestablecerDetalle();
                Espacio.Text = Limpieza.Formato(grupos.Sum(g => g.Bytes));
                TituloEstado.Text = categoria?.Nombre ?? "Análisis terminado";
                int omitidas = grupos.Sum(g => g.Inaccesibles);
                Estado.Text = $"{grupos.Sum(g => g.Archivos.Count):N0} archivos · {grupos.Count(g => g.Archivos.Count > 0)} categorías con resultados" +
                    (omitidas > 0 ? $" · Ubicaciones inaccesibles: {omitidas}" : "");
                Avance.Value = 100;
                ActualizarFilas();
                if (categoria != null && grupos[0].Archivos.Count > 0) AbrirDetalle(grupos[0]);
            }
            catch
            {
                TituloEstado.Text = tituloAnterior;
                Estado.Text = estadoAnterior;
                Avance.Value = avanceAnterior;
                throw;
            }
            finally { Ocupado(false); }
        });
    }

    private static List<AnalisisLimpieza> AnalizarCategorias(IReadOnlyList<CategoriaLimpieza> catalogo, IProgress<(string, double)> progreso, CancellationToken token)
    {
        var analisis = new List<AnalisisLimpieza>();
        foreach (var categoria in catalogo)
        {
            token.ThrowIfCancellationRequested();
            progreso.Report((categoria.Nombre, 100d * analisis.Count / catalogo.Count));
            analisis.Add(Limpieza.Analizar(categoria, token));
        }
        return analisis.OrderByDescending(g => g.Bytes).ToList();
    }

    private void Ocupado(bool valor)
    {
        ocupado = valor;
        Analizar.IsEnabled = Explorador.IsHitTestVisible = !valor;
        Avance.Visibility = valor || grupos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActualizarSeleccion();
    }

    private void ActualizarSeleccion()
    {
        Borrar.IsEnabled = !ocupado && marcados.Count > 0;
        Seleccion.Text = marcados.Count > 0
            ? $"{marcados.Sum(g => g.Archivos.Count):N0} archivos seleccionados · {Limpieza.Formato(marcados.Sum(g => g.Bytes))}"
            : grupos.Count == 0 ? "" : "Sin selección";
    }

    private void RestablecerDetalle()
    {
        Pestanas.SelectedIndex = 0;
        while (Pestanas.TabItems.Count > 2) Pestanas.TabItems.RemoveAt(2);
        marcados.Clear();
        ActualizarSeleccion();
    }

    private void ActualizarFilas()
    {
        Filas.Children.Clear();
        var familia = (Familias.SelectedItem as FamiliaLimpieza)?.Nombre;
        foreach (var grupo in grupos.Where(g => g.Archivos.Count > 0 && (familia == "Todas" || g.Categoria.Grupo == familia)))
            Filas.Children.Add(CrearFila(grupo));
        if (Filas.Children.Count > 0) return;
        var mensaje = grupos.Count > 0 ? "Sin archivos para limpiar en esta categoría." : familia switch
        {
            "Windows" => "Temporales del usuario de más de siete días y cachés de DirectX, miniaturas e iconos.",
            "Navegadores" => "Caché de los perfiles de Edge, Chrome, Brave y Firefox.",
            "Desarrollo" => "Caché HTTP de NuGet y cachés de Visual Studio Code.",
            "Aplicaciones" => "Cachés de Discord y GitHub Desktop.",
            _ => "Los resultados del análisis aparecerán acá, ordenados por tamaño."
        };
        Filas.Children.Add(new TextBlock { Text = mensaje, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 20, 12, 12), Opacity = 0.75 });
    }

    private Grid CrearFila(AnalisisLimpieza grupo)
    {
        var fila = new Grid { ColumnSpacing = 6, Padding = new Thickness(2, 2, 4, 2) };
        fila.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new() { Width = new GridLength(65) });
        fila.ColumnDefinitions.Add(new() { Width = new GridLength(78) });
        var marca = new CheckBox { MinWidth = 32, IsChecked = marcados.Contains(grupo) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(marca, "Seleccionar " + grupo.Categoria.Nombre);
        marca.Click += (_, _) =>
        {
            if (marca.IsChecked == true) marcados.Add(grupo); else marcados.Remove(grupo);
            ActualizarSeleccion();
        };
        var nombre = new TextBlock { Text = grupo.Categoria.Nombre, TextTrimming = TextTrimming.CharacterEllipsis };
        var detalle = new Button { Content = nombre, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(detalle, grupo.Categoria.Nombre);
        ToolTipService.SetToolTip(detalle, "Abrir detalle · " + grupo.Categoria.Nombre);
        detalle.Click += (_, _) => AbrirDetalle(grupo);
        fila.Children.Add(marca);
        Grid.SetColumn(detalle, 1);
        fila.Children.Add(detalle);
        AgregarDato(grupo.Archivos.Count.ToString("N0"), 2);
        AgregarDato(Limpieza.Formato(grupo.Bytes), 3);
        return fila;

        void AgregarDato(string valor, int columna)
        {
            var dato = new TextBlock { Text = valor, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(dato, columna);
            fila.Children.Add(dato);
        }
    }

    private void AbrirDetalle(AnalisisLimpieza grupo)
    {
        var existente = Pestanas.TabItems.OfType<TabViewItem>().FirstOrDefault(t => ReferenceEquals(t.Tag, grupo));
        if (existente != null) { Pestanas.SelectedItem = existente; return; }
        var detalle = new StackPanel { Spacing = 12, Margin = new Thickness(12) };
        detalle.Children.Add(new TextBlock { Text = grupo.Categoria.Nombre, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        detalle.Children.Add(new TextBlock { Text = $"{grupo.Archivos.Count:N0} archivos · {Limpieza.Formato(grupo.Bytes)}" });
        var lista = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 300, ItemsSource = grupo.Archivos,
            ItemTemplate = (DataTemplate)Resources["ArchivoLimpieza"] };
        var buscar = new TextBox { PlaceholderText = "Buscar por nombre o ruta" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(buscar, buscar.PlaceholderText);
        var elegidos = new HashSet<ArchivoLimpieza>();
        bool filtrando = false;
        var accion = new Button { Content = "Borrar archivos seleccionados…", IsEnabled = false };
        accion.Click += async (_, _) => await Confirmar([new(grupo.Categoria, elegidos.ToList(), 0)], individuales: true);
        lista.SelectionChanged += (_, cambio) =>
        {
            if (filtrando) return;
            elegidos.ExceptWith(cambio.RemovedItems.Cast<ArchivoLimpieza>());
            elegidos.UnionWith(cambio.AddedItems.Cast<ArchivoLimpieza>());
            accion.IsEnabled = elegidos.Count > 0;
            accion.Content = elegidos.Count == 0 ? "Borrar archivos seleccionados…" : $"Borrar selección ({elegidos.Count:N0})…";
        };
        buscar.TextChanged += (_, _) =>
        {
            string filtro = buscar.Text.Trim();
            filtrando = true;
            try
            {
                var visibles = filtro.Length == 0 ? grupo.Archivos : grupo.Archivos.Where(a => a.Ruta.Contains(filtro, StringComparison.OrdinalIgnoreCase)).ToList();
                lista.ItemsSource = visibles;
                foreach (var archivo in visibles.Where(elegidos.Contains))
                    if (!lista.SelectedItems.Contains(archivo)) lista.SelectedItems.Add(archivo);
            }
            finally { filtrando = false; }
        };
        detalle.Children.Add(buscar);
        detalle.Children.Add(lista);
        detalle.Children.Add(accion);
        var tab = new TabViewItem { Header = grupo.Categoria.Nombre, Tag = grupo, Content = detalle };
        Pestanas.TabItems.Add(tab);
        Pestanas.SelectedItem = tab;
    }

    private void CerrarPestana(TabView sender, TabViewTabCloseRequestedEventArgs e) => sender.TabItems.Remove(e.Tab);
    private async void BorrarSeleccion(object sender, RoutedEventArgs e) => await Confirmar(marcados.ToArray());

    private async Task Confirmar(AnalisisLimpieza[] elegidos, bool individuales = false)
    {
        var cantidad = elegidos.Sum(g => g.Archivos.Count);
        if (ocupado || cantidad == 0) return;
        string alcance = individuales && cantidad <= 4
            ? string.Join("\n", elegidos.SelectMany(g => g.Archivos).Select(a => a.Ruta))
            : string.Join("\n", elegidos.Select(g => g.Categoria.Nombre));
        string conteo = cantidad == 1 ? "1 archivo" : $"{cantidad:N0} archivos";
        var dialogo = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
            Title = "¿Borrar los archivos seleccionados?", Content = $"{conteo} · {Limpieza.Formato(elegidos.Sum(g => g.Bytes))}\n\n{alcance}\n\nSe eliminarán de forma permanente.",
            PrimaryButtonText = "Borrar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        await ejecutar("Limpieza de archivos", async token =>
        {
            Ocupado(true);
            try
            {
                var resultado = await Task.Run(() => Limpieza.Borrar(elegidos, token));
                grupos.Clear();
                RestablecerDetalle();
                ActualizarFilas();
                TituloEstado.Text = resultado.Cancelado ? "Limpieza cancelada" : "Limpieza terminada";
                Espacio.Text = Limpieza.Formato(resultado.Bytes);
                Estado.Text = $"Archivos borrados: {resultado.Borrados:N0} · Omitidos: {resultado.Omitidos:N0}. Volvé a analizar para actualizar la lista.";
                if (resultado.Cancelado) token.ThrowIfCancellationRequested();
            }
            finally { Ocupado(false); }
        });
    }
}
