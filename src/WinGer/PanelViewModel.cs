using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinGer;

public sealed class PanelViewModel : INotifyPropertyChanged
{
    private string estado = "Listo para comenzar";
    private bool ocupado;
    private string pendientes = "";
    public string Pendientes { get => pendientes; set { pendientes = value; Notificar(); } }
    public string Estado { get => estado; set { estado = value; Notificar(); } }
    public bool Ocupado { get => ocupado; set { ocupado = value; Notificar(); Notificar(nameof(Disponible)); } }
    public bool Disponible => !Ocupado;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notificar([CallerMemberName] string? nombre = null) => PropertyChanged?.Invoke(this, new(nombre));
}
