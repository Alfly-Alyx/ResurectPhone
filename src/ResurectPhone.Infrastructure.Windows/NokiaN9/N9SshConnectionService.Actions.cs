using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public async Task<N9MaintenanceReport> ExecuteActionAsync(string actionId,
        IProgress<N9OperationProgress>? progress = null, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        var reports = new List<N9MaintenanceReport>();
        async Task<N9MaintenanceReport> Apply(string id, string message)
        {
            progress?.Report(new(message));
            var report = await ApplyAsync(id, administratorPassword?.ToArray(), cancellationToken);
            reports.Add(report);
            return report;
        }
        try
        {
            // A single user action prepares its prerequisites. Each existing maintenance
            // operation retains its device checks, rollback and package safeguards.
            if (!N9MaintenanceCatalog.DeveloperPackages.ContainsKey(actionId) && actionId is not
                ("n9.repositories" or "n9.dependencies" or "n9.internet" or "n9.gps" or "n9.account" or
                 "n9.alternative-stores" or "n9.store.warehouse" or "n9.nokia-store"))
                throw new ArgumentException("Cette rubrique ne propose pas cette opération.", nameof(actionId));
            var needsPackages = N9MaintenanceCatalog.DeveloperPackages.ContainsKey(actionId) ||
                actionId is "n9.alternative-stores" or "n9.store.warehouse" or "n9.nokia-store";
            if (needsPackages)
            {
                await Apply("n9.repositories", "1/3 — Préparation des dépôts par USB…");
                progress?.Report(new("2/3 — Vérification des dépendances…"));
                var dependencies = await DiagnoseAsync("n9.dependencies", cancellationToken);
                if (dependencies.CanApply)
                    await Apply("n9.dependencies", "2/3 — Réparation des dépendances Nokia…");
                else
                {
                    await WithMaintenanceClientAsync(async (client, _) =>
                    {
                        var check = await RunMaintenanceScriptAsync(client, "apt-get -s check", null, cancellationToken);
                        RequireSuccess(check, "Les dépendances nécessitent une réparation qui ne peut pas être automatisée.");
                        return true;
                    }, cancellationToken);
                }
            }
            N9MaintenanceReport result;
            if (actionId == "n9.store.warehouse")
            {
                await Apply("n9.internet", "Préparation de TLS 1.2 pour Warehouse…");
                progress?.Report(new("Téléchargement et installation de Warehouse…"));
                var path = await N9OnlineResources.DownloadWarehouseAsync(cancellationToken);
                result = await InstallLocalPackageAsync(await InspectLocalPackageAsync(path, cancellationToken),
                    administratorPassword?.ToArray(), cancellationToken);
                reports.Add(result);
            }
            else result = await Apply(actionId == "n9.nokia-store" ? "n9.alternative-stores" : actionId,
                needsPackages ? "3/3 — Installation et vérification sur le N9…" : "Application et vérification sur le N9…");
            var backups = reports.Where(report => report.BackupPath is not null)
                .Select(report => report.BackupPath!).Distinct().ToArray();
            return result with
            {
                Detail = string.Join("\n\n", reports.Select(report => report.Summary + "\n" + report.Detail)),
                BackupPath = result.BackupPath ?? backups.LastOrDefault()
            };
        }
        catch (Exception exception) when (exception is not N9AdministratorRequiredException)
        {
            var backup = reports.LastOrDefault(report => report.BackupPath is not null)?.BackupPath;
            if (backup is null || exception is N9MaintenanceFailureException) throw;
            throw new N9MaintenanceFailureException(exception.Message +
                "\nÉtapes terminées : " + string.Join(" ; ", reports.Select(report => report.Summary)) +
                "\nSauvegardes :\n" + string.Join('\n', reports.Where(report => report.BackupPath is not null).Select(report => report.BackupPath)), backup);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }
}
