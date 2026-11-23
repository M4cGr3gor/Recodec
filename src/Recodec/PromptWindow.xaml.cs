using System.Windows;

namespace Recodec;

/// <summary>Pide un texto corto (el nombre de un perfil).</summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        OkButton.IsEnabled = initial.Trim().Length > 0;
        Input.TextChanged += (_, _) => OkButton.IsEnabled = Input.Text.Trim().Length > 0;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public string Value => Input.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
