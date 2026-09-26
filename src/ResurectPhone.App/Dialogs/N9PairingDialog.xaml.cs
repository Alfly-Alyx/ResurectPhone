using System.Windows;
using System.Runtime.InteropServices;

namespace ResurectPhone.App.Dialogs;

public partial class N9PairingDialog : Window
{
    public N9PairingDialog(bool administrator = false)
    {
        InitializeComponent();
        if (administrator)
        {
            Title = "Préparer l’accès USB du N9";
            Heading.Text = "Mot de passe administrateur du N9";
            Instructions.Text = "Le mot de passe d’origine a été essayé automatiquement et refusé. Saisissez celui que vous avez configuré pour devel-su.";
            PasswordLabel.Text = "Mot de passe administrateur personnalisé";
            PasswordHint.Text = "Ce mot de passe sera utilisé pour cette préparation puis effacé de la mémoire.";
            FooterText.Text = "ResurectPhone préparera le N9 pour connecter automatiquement les prochains PC branchés en USB.";
        }
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string TemporaryPassword => PasswordInput.Password;

    public void ClearPassword() => PasswordInput.Clear();

    public char[] ReadPasswordCharacters()
    {
        using var secure = PasswordInput.SecurePassword;
        var buffer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            var characters = new char[secure.Length];
            Marshal.Copy(buffer, characters, 0, characters.Length);
            return characters;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(buffer);
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        using var secure = PasswordInput.SecurePassword;
        if (secure.Length == 0)
        {
            ValidationText.Text = "Saisissez le mot de passe pour continuer.";
            PasswordInput.Focus();
            return;
        }

        DialogResult = true;
    }
}
