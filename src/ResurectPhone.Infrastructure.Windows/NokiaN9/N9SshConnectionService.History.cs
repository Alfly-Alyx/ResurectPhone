using System.Text.Json;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public async Task<IReadOnlyList<N9SettingsBackup>> ReadSettingsBackupsAsync(CancellationToken cancellationToken = default)
    {
        var pairing = _pairingStore.TryLoad();
        return pairing is null ? [] : await new N9SettingsHistory(pairing.HostKeySha256).ReadAsync(cancellationToken);
    }

    public async Task<N9MaintenanceReport> ApplyAsync(string featureId, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = _pairingStore.TryLoad()?.HostKeySha256;
        async Task<string?> Record(string? backup)
        {
            if (backup is null || fingerprint is null) return null;
            try
            {
                var label = RecoveryCatalog.Features.FirstOrDefault(feature => feature.Id == featureId)?.Title ?? featureId;
                await new N9SettingsHistory(fingerprint).SaveAsync(featureId, backup, label, CancellationToken.None);
                return null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { return "L’historique local n’a pas pu être enregistré. Conservez le chemin de sauvegarde : " + backup; }
        }
        try
        {
            var result = await ApplyCoreAsync(featureId, administratorPassword, cancellationToken);
            var warning = await Record(result.BackupPath);
            return warning is null ? result : result with { Detail = result.Detail + "\n" + warning };
        }
        catch (N9MaintenanceFailureException failure)
        {
            await Record(failure.BackupPath);
            throw;
        }
    }
}
