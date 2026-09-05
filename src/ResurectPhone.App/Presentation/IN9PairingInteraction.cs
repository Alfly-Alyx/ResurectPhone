using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.App.Presentation;

public interface IN9PairingInteraction
{
    string? RequestTemporaryPassword();

    bool ConfirmHostKey(N9HostKeyIdentity identity);

    bool ConfirmForgetPairing();
}
