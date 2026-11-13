namespace WinGer.Core;

public sealed class ControlLecturaWeb(bool pasarAutomaticamente = true)
{
    public bool PasarAutomaticamente { get; set; } = pasarAutomaticamente;
    public bool ResultadoDisponible { get; private set; }
    public bool DebeAvanzar => PasarAutomaticamente && ResultadoDisponible;
    public void RegistrarResultado() => ResultadoDisponible = true;
}
