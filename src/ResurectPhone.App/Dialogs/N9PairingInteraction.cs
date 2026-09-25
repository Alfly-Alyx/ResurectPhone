using System.IO;
using System.Windows;
using Microsoft.Win32;
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

    public void ExportUsbSetupScript()
    {
        var dialog = new SaveFileDialog
        {
            FileName = "enable-usb-passwordless.sh",
            Filter = "Script N9 (*.sh)|*.sh",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(ownerProvider()) != true)
            return;

        using var source = typeof(N9PairingInteraction).Assembly.GetManifestResourceStream(
            "ResurectPhone.N9UsbSetup") ?? throw new InvalidOperationException(
            "Le script de préparation USB est absent de l’application.");
        using (var destination = File.Create(dialog.FileName))
            source.CopyTo(destination);

        MessageBox.Show(
            ownerProvider(),
            "Copiez ce fichier dans MyDocs du N9 en mode stockage USB. Sur le N9, ouvrez Terminal, lancez devel-su, puis saisissez :\n" +
            "sh /home/user/MyDocs/enable-usb-passwordless.sh\n\n" +
            "Revenez au mode USB SDK. ResurectPhone se connectera automatiquement.",
            "Préparer l’accès USB du N9",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
