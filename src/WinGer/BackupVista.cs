using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace WinGer;

public sealed partial class MainWindow
{
    private void MostrarBackup()
    {
        Contenido.Children.Add(new TextBlock { Text = "Datos y backup", FontSize = 24 });
        Contenido.Children.Add(Texto("Carpeta de WinGer: " + almacen.RutaDatos));
        Contenido.Children.Add(Texto("Los ajustes se guardan automáticamente. Crear backup concentra configuración, caché e historial en un solo archivo. No incluye claves, sesiones web ni instaladores descargados."));
        var tarjetas = new PanelAjustable { AnchoElemento = 340 };
        var crear = new StackPanel { Spacing = 10 };
        crear.Children.Add(Texto("Guardá todos tus ajustes y resultados en un único archivo de backup."));
        crear.Children.Add(Boton("Crear backup", async () =>
        {
            if (Modelo.Ocupado || reparacionEnCurso) { Modelo.Estado = "Esperá a que termine la operación actual."; return; }
            var selector = new FileSavePicker { SuggestedFileName = "WinGer-backup-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") };
            selector.FileTypeChoices.Add("Backup de WinGer", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var archivo = await selector.PickSaveFileAsync(); if (archivo is null) return;
            almacen.Guardar(configuracion); GuardarCache(); almacen.CrearBackup(archivo.Path);
            Modelo.Estado = "Backup creado: " + archivo.Path;
        }));
        tarjetas.Children.Add(MarcoHerramienta(crear));
        var importar = new StackPanel { Spacing = 10 };
        importar.Children.Add(Texto("Restaurá configuración, caché e historial desde un backup de WinGer."));
        importar.Children.Add(Boton("Importar", async () =>
        {
            if (Modelo.Ocupado || reparacionEnCurso) { Modelo.Estado = "Esperá a que termine la operación actual."; return; }
            var selector = new FileOpenPicker(); selector.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var archivo = await selector.PickSingleFileAsync(); if (archivo is null) return;
            if (await new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = "Importar backup", Content = "Se reemplazarán los ajustes, la caché y el historial actuales. Las credenciales se conservan. ¿Continuar?", PrimaryButtonText = "Importar", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close }.ShowAsync() != ContentDialogResult.Primary) return;
            configuracion = almacen.ImportarBackup(archivo.Path);
            paquetes.Clear(); salidaVirus = ""; RestaurarCache(); virus.BorrarCache(); vtai?.BorrarCache(); AplicarTema(); MostrarConfiguracion();
            Modelo.Estado = "Backup importado correctamente.";
        }));
        tarjetas.Children.Add(MarcoHerramienta(importar)); Contenido.Children.Add(tarjetas);
        Contenido.Children.Add(Boton("Abrir carpeta de datos", () => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(almacen.RutaDatos) { UseShellExecute = true }); return Task.CompletedTask; }));
    }
}
