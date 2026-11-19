using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace WinGer;

internal static class VistaPreviaCamara
{
    internal static async Task Mostrar(string instancia)
    {
        const string propiedad = "System.Devices.DeviceInstanceId";
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var dispositivos = await DeviceInformation.FindAllAsync(
            DeviceInformation.GetAqsFilterFromDeviceClass(DeviceClass.VideoCapture), new[] { propiedad }).AsTask(limite.Token);
        var dispositivo = dispositivos.FirstOrDefault(d => d.Properties.TryGetValue(propiedad, out var id)
            && string.Equals(id as string, instancia, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("La cámara reiniciada no está disponible para video.");

        using var captura = new MediaCapture();
        await captura.InitializeAsync(new MediaCaptureInitializationSettings {
            VideoDeviceId = dispositivo.Id, StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu, SharingMode = MediaCaptureSharingMode.SharedReadOnly
        }).AsTask(limite.Token);
        var fuente = captura.FrameSources.Values.Where(f => f.Info.SourceKind == MediaFrameSourceKind.Color)
            .OrderByDescending(f => f.Info.MediaStreamType == MediaStreamType.VideoPreview).FirstOrDefault()
            ?? throw new InvalidOperationException("La cámara no ofrece una fuente de video en color.");
        using var lector = await captura.CreateFrameReaderAsync(fuente, MediaEncodingSubtypes.Bgra8).AsTask(limite.Token);
        if (await lector.StartAsync().AsTask(limite.Token) != MediaFrameReaderStartStatus.Success)
            throw new InvalidOperationException("Windows no pudo iniciar el video de la cámara.");

        var imagen = new SoftwareBitmapSource();
        var ventana = new Window { Title = dispositivo.Name, Content = new Image { Source = imagen } };
        var cerrada = false;
        ventana.Closed += (_, _) => cerrada = true;
        try
        {
            ventana.AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 300));
            if (ventana.AppWindow.Presenter is OverlappedPresenter presentador) presentador.IsAlwaysOnTop = true;
            System.Diagnostics.Stopwatch? visible = null;
            while (visible == null || visible.Elapsed < TimeSpan.FromSeconds(3))
            {
                limite.Token.ThrowIfCancellationRequested();
                if (cerrada) throw new InvalidOperationException("La vista previa se cerró antes de completar la prueba.");
                using var cuadro = lector.TryAcquireLatestFrame();
                using var mapa = cuadro?.VideoMediaFrame?.SoftwareBitmap;
                if (mapa != null)
                {
                    using var convertido = SoftwareBitmap.Convert(mapa, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    await imagen.SetBitmapAsync(convertido);
                    if (visible == null)
                    {
                        ventana.Activate();
                        visible = System.Diagnostics.Stopwatch.StartNew();
                    }
                }
                await Task.Delay(60, limite.Token);
            }
        }
        catch (OperationCanceledException) { throw new TimeoutException("Se agotó el tiempo de espera del video."); }
        finally
        {
            if (!cerrada) ventana.Close();
            await lector.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}
