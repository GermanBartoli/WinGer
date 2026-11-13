using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using WinGer.Core;

namespace WinGer;

public sealed partial class MainWindow
{
    private void RestaurarCache()
    {
        var cache = almacen.LeerCache();
        ultimaBusqueda = cache.UltimaBusqueda; filtroPaquetes = cache.Filtro; ordenarPorDetecciones = cache.OrdenarPorDetecciones; soloSeleccionados = cache.SoloSeleccionados;
        hashActual = cache.Hash;
        if (!string.IsNullOrEmpty(cache.SalidaVirus)) salidaVirus = cache.SalidaVirus;
        foreach (var completado in cache.Paquetes.Where(p => p.ActualizacionConfirmada))
        {
            var registro = almacen.LeerHistorial().FirstOrDefault(e => e.Operacion == "Actualización · " + completado.Id && e.Resultado.StartsWith("Actualizado · "));
            if (registro is not null) almacen.RegistrarActualizacion(completado, registro.Fecha);
        }
        paquetes.AddRange(cache.Paquetes.Where(p => !p.ActualizacionConfirmada));
        var informesApi = almacen.LeerInformes();
        var informesVtai = almacen.LeerInformesVtai();
        foreach (var p in paquetes)
        {
            // Recuperar evidencia anterior por su hash sin inventar fechas ni consumir consultas.
            if (p.HashPublicado is string hash)
            {
                if (p.ResultadoApi is null && informesApi.TryGetValue(hash, out var api))
                    p.ResultadoApi = new(hash, "API", api.Informe, api.Fecha);
                if (p.ResultadoVtai is null && informesVtai.TryGetValue(hash, out var ai))
                    p.ResultadoVtai = new(hash, "VTAI", ai.Informe, ai.Fecha);
            }
            p.Descarga = null;
            if (p.Estado != "Sin analizar" && !p.Estado.StartsWith("Guardado · "))
            {
                p.Estado = "Guardado · " + p.Estado;
            }
        }
        VincularPaquetes();
    }
    private void VincularPaquetes()
    {
        foreach (var p in paquetes) p.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Paquete.Seleccionado)) return;
            DispatcherQueue.TryEnqueue(() => { FiltrarPaquetes(); GuardarCache(); });
        };
    }
    private void GuardarCache()
    {
        try { almacen.GuardarCache(new CacheSesion { UltimaBusqueda = ultimaBusqueda, Paquetes = paquetes,
            OrdenarPorDetecciones = ordenarPorDetecciones, SoloSeleccionados = soloSeleccionados, Filtro = filtroPaquetes, Hash = hashActual, SalidaVirus = salidaVirus }); }
        catch (Exception error) { Modelo.Estado = "No se pudo guardar la caché: " + error.Message; }
    }
    private void MostrarOpcionesCache()
    {
        var modo = new ComboBox { Header = "Consultar VirusTotal al buscar actualizaciones", Width = 300,
            ItemsSource = new[] { "VTAI", "Web experimental", "Desactivado" }, SelectedItem = configuracion.ConsultaAutomatica };
        modo.SelectionChanged += (_, _) => GuardarAjuste(() => configuracion.ConsultaAutomatica = modo.SelectedItem?.ToString() ?? "Desactivado");
        Contenido.Children.Add(modo);
        Contenido.Children.Add(Texto("VTAI usa una conexión propia de WinGer, sin clave clásica. La web experimental lee el informe visible y puede requerir intervención. Ninguna opción sube instaladores."));
        Contenido.Children.Add(Boton("Iniciar sesión en VirusTotal / Mi cuenta web", () => Ejecutar("Cuenta web de VirusTotal", AbrirCuentaVirusTotal)));
        Contenido.Children.Add(Texto("Ingresá tu usuario y contraseña en la página oficial. La sesión del navegador se comparte con Leer Web y se conserva entre aperturas mientras VirusTotal la mantenga vigente. Desde allí podés cerrar sesión o cambiar de cuenta."));
        var tokenVtai = new PasswordBox { Header = "Token de VTAI", PlaceholderText = "Ingresá un token para guardarlo o reemplazarlo", MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        Contenido.Children.Add(tokenVtai);
        var estadoVtai = Texto(EstadoTokenVtai());
        Contenido.Children.Add(estadoVtai);
        Contenido.Children.Add(Boton("Conectar VTAI", () => Ejecutar("Conexión VTAI", async token =>
        {
            await ObtenerTokenVtai(token);
            estadoVtai.Text = EstadoTokenVtai();
        })));
        Contenido.Children.Add(Boton("Guardar token VTAI", () =>
        {
            if (Modelo.Ocupado) return Task.CompletedTask;
            var valor = tokenVtai.Password.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(valor, @"\Avtai_[A-Za-z0-9_-]+\z")) throw new ArgumentException("Token VTAI no reconocido.");
            credenciales.Add(new Windows.Security.Credentials.PasswordCredential("WinGer.VTAI", "usuario", valor));
            tokenVtai.Password = ""; estadoVtai.Text = EstadoTokenVtai(); Modelo.Estado = "Token VTAI protegido y guardado.";
            return Task.CompletedTask;
        }));
        Contenido.Children.Add(Texto("La caché conserva búsquedas, selección, filtros, hashes e informes. Borrarla conserva ajustes, credenciales, historial e instaladores descargados."));
        Contenido.Children.Add(Boton("Borrar caché", () =>
        {
            if (Modelo.Ocupado) { Modelo.Estado = "Esperá a que termine la operación antes de borrar la caché."; return Task.CompletedTask; }
            almacen.BorrarCache(); virus.BorrarCache(); vtai?.BorrarCache();
            paquetes.Clear(); ordenarPorDetecciones = true; soloSeleccionados = false; filtroPaquetes = ""; hashActual = ""; salidaVirus = ""; ultimaBusqueda = null;
            ActualizarContador();
            Modelo.Estado = "Caché borrada.";
            return Task.CompletedTask;
        }));
    }
    private string EstadoTokenVtai()
    {
        try
        {
            credenciales.Retrieve("WinGer.VTAI", "usuario");
            return "✓ Token VTAI guardado y protegido en Windows. No se muestra por seguridad.";
        }
        catch (System.Runtime.InteropServices.COMException) { return "Sin token VTAI guardado. Conectá WinGer o importá uno existente."; }
    }
}
