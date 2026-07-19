using System.Windows;

namespace MicFX;

public partial class InputDialog : Window
{
    public string Value => txtValue.Text.Trim();

    public InputDialog(Window owner, string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        txtPrompt.Text = prompt;
        txtValue.Text = initial;
        Loaded += (_, _) =>
        {
            txtValue.SelectAll();
            txtValue.Focus();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
