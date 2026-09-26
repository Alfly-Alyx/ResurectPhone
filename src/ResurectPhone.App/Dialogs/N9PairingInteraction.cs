using System.IO;
using System.Windows;
using Microsoft.Win32;
using ResurectPhone.App.Presentation;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.App.Dialogs;

public sealed class N9PairingInteraction(Func<Window?> ownerProvider) : IN9PairingInteraction
{
    public void ShowMaintenance(RecoveryFeature feature, IN9MaintenanceService service)
    {
        var dialog = new N9MaintenanceDialog(feature, service, RequestAdministratorPassword) { Owner = ownerProvider() };
        dialog.ShowDialog();
    }

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
        "ResurectPhone supprimera de ce PC la clé et l’empreinte enregistrées pour le N9. Il créera une nouvelle liaison lorsque le N9 sera reconnecté en USB.",
        "Oublier la liaison N9",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No) == MessageBoxResult.Yes;

    public N9UsbSetupStageResult TryStageUsbSetupScript() =>
        N9UsbSetupStager.TryStage(ReadUsbSetupScript());

    public void ExportUsbSetupScript()
    {
        var dialog = new SaveFileDialog
        {
            FileName = N9UsbSetupStager.FileName,
            Filter = "Script N9 (*.sh)|*.sh",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(ownerProvider()) != true)
            return;

        File.WriteAllBytes(dialog.FileName, ReadUsbSetupScript());

        MessageBox.Show(
            ownerProvider(),
            "Copiez ce fichier dans MyDocs/ResurectPhone du N9 en mode stockage USB. Sur le N9, ouvrez Terminal, lancez devel-su, puis saisissez :\n" +
            "sh /home/user/MyDocs/ResurectPhone/resurectphone-usb-setup.sh\n\n" +
            "Revenez au mode USB SDK. ResurectPhone se connectera automatiquement.",
            "Préparer l’accès USB du N9",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public char[]? RequestAdministratorPassword()
    {
        var dialog = new N9PairingDialog(administrator: true) { Owner = ownerProvider() };
        try
        {
            return dialog.ShowDialog() == true ? dialog.ReadPasswordCharacters() : null;
        }
        finally
        {
            dialog.ClearPassword();
        }
    }

    public byte[] ReadUsbSetupScript()
    {
        using var source = typeof(N9PairingInteraction).Assembly.GetManifestResourceStream(
            "ResurectPhone.N9UsbSetup") ?? throw new InvalidOperationException(
            "Le script de préparation USB est absent de l’application.");
        using var destination = new MemoryStream();
        source.CopyTo(destination);
        return destination.ToArray();
    }
}
