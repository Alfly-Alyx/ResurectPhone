using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.App.Presentation;

public interface IN9PairingInteraction
{
    string? RequestTemporaryPassword();

    bool ConfirmHostKey(N9HostKeyIdentity identity);

    bool ConfirmForgetPairing();

    N9UsbSetupStageResult TryStageUsbSetupScript();

    void ExportUsbSetupScript();
}
