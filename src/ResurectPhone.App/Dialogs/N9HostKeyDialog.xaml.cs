using System.Windows;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.App.Dialogs;

public partial class N9HostKeyDialog : Window
{
    public N9HostKeyDialog(N9HostKeyIdentity identity)
    {
        InitializeComponent();
        DataContext = new HostKeyViewModel(
            identity.DisplayFingerprint,
            $"Algorithme : {identity.Algorithm} · {identity.KeyLength} bits");
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record HostKeyViewModel(string DisplayFingerprint, string KeyDescription);
}
