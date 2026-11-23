using System.ComponentModel;
using System.Windows;
using Recodec.ViewModels;

namespace Recodec;

/// <summary>Ventana de progreso: aparece al convertir y se cierra sola al terminar la cola.</summary>
public partial class ProgressWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _allowClose;

    public ProgressWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Cierra la ventana al terminar (sin preguntar).</summary>
    public void CloseFinished()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose || !_vm.IsRunning) return;
        // Cerrar esta ventana a mitad de una conversión equivale a cancelarla: se pregunta antes.
        e.Cancel = true;
        if (MessageBox.Show(this, "¿Cancelar la conversión? Se borrará el archivo a medias y la cola se detendrá.", "Recodec",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _vm.CancelCommand.Execute(null);
    }
}
