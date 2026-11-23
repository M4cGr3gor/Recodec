using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Recodec.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action run, Func<bool>? can = null) : this(_ => run(), can is null ? null : _ => can()) { }
    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
}

/// <summary>Opción de una lista desplegable: valor + texto en español.</summary>
public sealed record Option<T>(T Value, string Label, string? Hint = null)
{
    public override string ToString() => Label;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is bool b ? !b : value;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var v = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            null => false,
            _ => true,
        };
        if (Invert) v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Órdenes comunes a todas las ventanas (los botones "?" de ayuda funcionan en cualquiera).</summary>
public static class AppCommands
{
    public static RelayCommand Help { get; } = new(p =>
    {
        if (Application.Current?.MainWindow is Recodec.ViewModels.IDialogs d) d.ShowHelp(p as string ?? "inicio");
    });
}

/// <summary>Ancho de una barra de progreso: porcentaje (0-100) del ancho disponible.</summary>
public sealed class PercentWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) =>
        values is [double pct, double width] && width > 0 ? Math.Clamp(pct, 0, 100) / 100 * width : 0.0;

    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}
