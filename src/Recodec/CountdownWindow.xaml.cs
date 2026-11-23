using System.Windows;
using System.Windows.Threading;
using Recodec.Infrastructure;

namespace Recodec;

/// <summary>Cuenta atrás de 60 s antes de suspender o apagar el equipo, por si el usuario sigue delante.</summary>
public partial class CountdownWindow : Window
{
    private const int Seconds = 60;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _verb;
    private int _left = Seconds;

    public CountdownWindow(WhenDoneAction action)
    {
        InitializeComponent();
        _verb = action == WhenDoneAction.Sleep ? "se suspenderá" : "se apagará";
        NowButton.Content = action == WhenDoneAction.Sleep ? "Suspender ahora" : "Apagar ahora";
        Update();
        _timer.Tick += (_, _) =>
        {
            _left--;
            Update();
            if (_left <= 0) { _timer.Stop(); DialogResult = true; }
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Update()
    {
        Message.Text = $"El equipo {_verb} dentro de {_left} segundos. Pulsa Cancelar si lo vas a seguir usando.";
        Bar.Value = Seconds - _left;
    }

    private void Now_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
