using System.Windows;

namespace ResurectPhone.App.Dialogs;

public partial class N9PairingDialog : Window
{
    public N9PairingDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string TemporaryPassword => PasswordInput.Password;

    public void ClearPassword() => PasswordInput.Clear();

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (TemporaryPassword.Length == 0)
        {
            ValidationText.Text = "Saisissez le mot de passe affiché actuellement par SDK Connectivity.";
            PasswordInput.Focus();
            return;
        }

        DialogResult = true;
    }
}
