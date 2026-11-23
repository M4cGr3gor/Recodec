using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace Recodec;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Interfaz en español: fechas, números y textos de los controles.
        var es = new CultureInfo("es-ES");
        CultureInfo.DefaultThreadCurrentCulture = es;
        CultureInfo.DefaultThreadCurrentUICulture = es;
        Thread.CurrentThread.CurrentCulture = es;
        Thread.CurrentThread.CurrentUICulture = es;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(es.IetfLanguageTag)));

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show("Ha ocurrido un error inesperado:\n\n" + ex.Exception.Message, "Recodec", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        base.OnStartup(e);
        var window = new MainWindow(e.Args.FirstOrDefault());
        MainWindow = window;
        window.Show();
    }
}
