using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Recodec.ViewModels;

namespace Recodec;

public partial class SyncWindow : Window
{
    private readonly SyncViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _playing;
    private string? _currentClip;

    public SyncWindow(SyncViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.ClipReady += path => Dispatcher.Invoke(() => Play(path));
        vm.ReleaseClip += () => Dispatcher.Invoke(Release);
        vm.CloseRequested += () => { DialogResult = true; };
        _timer.Tick += (_, _) => UpdateTime();
        DelayBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            DelayBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            _vm.GenerateCommand.Execute(null);
        };
        Loaded += (_, _) => _ = _vm.GenerateAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Release();
            _vm.Cleanup();
        };
    }

    private void Play(string path)
    {
        _currentClip = path;
        Player.Source = new Uri(path);
        Player.Play();
        SetPlaying(true);
    }

    private void Release()
    {
        Player.Stop();
        Player.Close();
        Player.Source = null;
        SetPlaying(false);
        TimeText.Text = "";
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        PlayIcon.Text = playing ? "" : ""; // pausa / reproducir
        if (playing) _timer.Start(); else _timer.Stop();
        UpdateTime();
    }

    private void UpdateTime()
    {
        if (Player.Source is null) return;
        var total = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : SyncViewModel.ClipLength;
        TimeText.Text = $"{Player.Position:mm\\:ss} / {total:mm\\:ss}";
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (Player.Source is null) return;
        if (_playing) { Player.Pause(); SetPlaying(false); }
        else { Player.Play(); SetPlaying(true); }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (Player.Source is null) return;
        Player.Position = TimeSpan.Zero;
        Player.Play();
        SetPlaying(true);
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e) => UpdateTime();

    // Al terminar vuelve a empezar: así se puede mirar varias veces sin tocar nada.
    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Player.Position = TimeSpan.Zero;
        Player.Play();
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        SetPlaying(false);
        var clip = _currentClip;
        if (clip is null) return;
        if (MessageBox.Show(this,
                "Windows no ha podido reproducir el clip dentro de Recodec (puede faltar el componente \"Windows Media Player\").\n\n¿Quieres abrirlo en tu reproductor de vídeo?",
                "Comprobar la sincronización", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(clip) { UseShellExecute = true }); } catch { }
        }
    }
}
