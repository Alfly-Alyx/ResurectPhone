using System.Windows;
using ResurectPhone.App.Presentation;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.App.Dialogs;

public sealed class N9PairingInteraction(Func<Window?> ownerProvider) : IN9PairingInteraction
{
    public string? RequestTemporaryPassword()
    {
        var dialog = new N9PairingDialog { Owner = ownerProvider() };
        try
        {
            return dialog.ShowDialog() == true ? dialog.TemporaryPassword : null;
        }
        finally
        {
            dialog.ClearPassword();
        }
    }

    public bool ConfirmHostKey(N9HostKeyIdentity identity)
    {
        bool ShowDialog()
        {
            var dialog = new N9HostKeyDialog(identity) { Owner = ownerProvider() };
            return dialog.ShowDialog() == true;
        }

        return Application.Current.Dispatcher.CheckAccess()
            ? ShowDialog()
            : Application.Current.Dispatcher.Invoke(ShowDialog);
    }

    public bool ConfirmForgetPairing() => MessageBox.Show(
        ownerProvider(),
        "ResurectPhone supprimera de ce PC la clé et l’empreinte enregistrées pour le N9. Vous devrez utiliser de nouveau le mot de passe de SDK Connectivity pour l’appairer.",
        "Oublier la liaison N9",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No) == MessageBoxResult.Yes;
}
