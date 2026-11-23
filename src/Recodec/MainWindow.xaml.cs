using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Recodec.Core.Conversion;
using Recodec.Infrastructure;
using Recodec.ViewModels;

namespace Recodec;

public partial class MainWindow : Window, IDialogs
{
    private readonly MainViewModel _vm;
    private readonly string? _startupFile;
    private HelpWindow? _help;
    private bool _closing;

    public MainWindow(string? startupFile)
    {
        InitializeComponent();
        _startupFile = startupFile;
        _vm = new MainViewModel(this);
        DataContext = _vm;

        PreviewDragOver += OnDragOver;
        PreviewDrop += OnDrop; // "Preview" para que los cuadros de texto no se queden con el archivo
        Loaded += async (_, _) => await _vm.InitializeAsync(_startupFile);
        // La ventana de progreso aparece al empezar a convertir y se cierra sola al terminar.
        _vm.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName != nameof(MainViewModel.IsRunning)) return;
            if (_vm.IsRunning) ShowProgress();
            else { _progress?.CloseFinished(); _progress = null; }
        };
        Closing += (_, e) =>
        {
            if (!_vm.IsRunning) return;
            e.Cancel = true;
            if (_closing || !Confirm("Hay una conversión en marcha. Si cierras, se cancelará y se borrará el archivo a medias.\n\n¿Cerrar de todos modos?", "Recodec"))
                return;
            // Se espera a que FFmpeg se detenga y se borre el archivo a medias; después se cierra.
            _closing = true;
            _vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(MainViewModel.IsRunning) && !_vm.IsRunning) Dispatcher.BeginInvoke(Close); };
            _vm.CancelCommand.Execute(null);
        };
    }

    // ---------------------------------------------------------------- arrastrar y soltar
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_vm.IsRunning ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm.IsRunning)
        {
            e.Handled = true;
            Info("Espera a que termine la conversión (o cancélala) para abrir otros vídeos.", "Recodec");
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items) return;
        e.Handled = true;
        var files = new List<string>();
        foreach (var item in items)
        {
            // Las subcarpetas sin permiso de lectura se saltan en lugar de interrumpir todo.
            if (Directory.Exists(item))
                files.AddRange(Directory.EnumerateFiles(item, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(IsVideo).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase));
            else if (File.Exists(item)) files.Add(item);
        }
        await _vm.OpenFilesAsync(files);
    }

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".m2ts", ".mts", ".ts", ".webm", ".wmv", ".mpg", ".mpeg", ".vob", ".flv", ".3gp", ".ogv", ".divx",
    };

    private static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    // ---------------------------------------------------------------- diálogos
    public string? OpenVideo()
    {
        var d = new OpenFileDialog
        {
            Title = "Abrir vídeo",
            Filter = "Vídeos|" + string.Join(";", VideoExtensions.Select(x => "*" + x)) + "|Todos los archivos|*.*",
        };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public string? SaveVideo(string suggestedPath, ContainerKind container)
    {
        var ext = ContainerRules.Extension(container);
        var d = new SaveFileDialog
        {
            Title = "Guardar el vídeo convertido",
            Filter = $"{ContainerRules.DisplayName(container)} (*{ext})|*{ext}",
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = false, // se pregunta al añadir a la cola
            FileName = Path.GetFileName(suggestedPath),
            InitialDirectory = Path.GetDirectoryName(suggestedPath),
        };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string message, string title) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowHelp(string topic)
    {
        // Si hay una ventana modal abierta (Configuración, Sincronización…), la ayuda depende de ella:
        // si dependiera de la principal, Windows la dejaría bloqueada mientras la modal siga abierta.
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w is not HelpWindow) ?? this;
        if (_help is not null && _help.IsLoaded && _help.Owner != owner) { _help.Close(); _help = null; }
        if (_help is null || !_help.IsLoaded)
        {
            _help = new HelpWindow { Owner = owner };
            _help.Show();
        }
        _help.ShowTopic(topic);
        _help.Activate();
    }

    private QueueWindow? _queue;

    public void ShowQueue()
    {
        if (_queue is null || !_queue.IsLoaded)
        {
            _queue = new QueueWindow { Owner = this, DataContext = _vm };
            _queue.Show();
        }
        if (_queue.WindowState == WindowState.Minimized) _queue.WindowState = WindowState.Normal;
        _queue.Activate();
    }

    private ProgressWindow? _progress;

    public void ShowProgress()
    {
        if (_progress is null || !_progress.IsLoaded)
        {
            _progress = new ProgressWindow(_vm) { Owner = this };
            _progress.Show();
        }
        if (_progress.WindowState == WindowState.Minimized) _progress.WindowState = WindowState.Normal;
        _progress.Activate();
    }

    public void ShowSettings() => new SettingsWindow { Owner = this, DataContext = _vm }.ShowDialog();

    public string? AskText(string title, string prompt, string initial)
    {
        var w = new PromptWindow(title, prompt, initial) { Owner = this };
        return w.ShowDialog() == true ? w.Value : null;
    }

    public bool ConfirmPowerAction(WhenDoneAction action)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        return new CountdownWindow(action) { Owner = this }.ShowDialog() == true;
    }

    public bool ShowSync(SyncViewModel vm) => new SyncWindow(vm) { Owner = this }.ShowDialog() == true;

    public void ShowDevices(DevicesViewModel vm) => new DevicesWindow(vm) { Owner = OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsVisible) as Window ?? this }.ShowDialog();
}
