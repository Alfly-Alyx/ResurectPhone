using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.App.Presentation;

public interface IN9PairingInteraction
{
    string? RequestTemporaryPassword();

    char[]? RequestAdministratorPassword();

    byte[] ReadUsbSetupScript();

    void ShowMaintenance(RecoveryFeature feature, IN9MaintenanceService service);

    bool ConfirmHostKey(N9HostKeyIdentity identity);

    bool ConfirmForgetPairing();

    N9UsbSetupStageResult TryStageUsbSetupScript();

    void ExportUsbSetupScript();
}
