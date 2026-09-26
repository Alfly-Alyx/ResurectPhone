using System.Text.RegularExpressions;
using Renci.SshNet;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    private const string AptInstallOptions = " -o APT::Get::HideAutoRemove=true --no-remove";
    private const string ReleaseIdlePackageManager = """
        set -eu
        holders=$(pidof pkgmgrd || true)
        if test -n "$holders"; then
            current=$(pkgmgr current-operation 2>&1)
            if test "$current" != 'No current operation'; then
                printf 'Le gestionnaire Nokia travaille encore : %s\n' "$current" >&2
                exit 1
            fi
            for pid in $holders; do
                case "$pid" in *[!0-9]*|'') exit 1 ;; esac
                # /proc/PID/comm is absent on the N9's 2.6.32 kernel.
                test "$(sed -n 's/^Name:[[:space:]]*//p' /proc/$pid/status 2>/dev/null)" = pkgmgrd || continue
                kill -TERM "$pid"
            done
            attempts=0
            while pidof pkgmgrd >/dev/null; do
                attempts=$((attempts + 1))
                if test "$attempts" -gt 30; then echo 'Le gestionnaire Nokia ne libère pas les paquets.' >&2; exit 1; fi
                sleep 1
            done
            echo 'Gestionnaire Nokia inactif fermé temporairement ; il redémarrera à la prochaine demande.'
        fi
        """;

    private static async Task ReleasePackageManagerAsync(SshClient client, byte[] password, CancellationToken cancellationToken)
    {
        var result = await RunMaintenanceScriptAsync(client, ReleaseIdlePackageManager, password, cancellationToken);
        RequireSuccess(result, "Les paquets sont utilisés par le gestionnaire Nokia. Réessayez à la fin de son opération.");
    }

    private const string DependencyInventory = """
        dpkg-query -W -f='${Package}\t${Version}\t${Status}\n' mp-harmattan-005-pr facebook facebookqml twitter twitter-qml 2>/dev/null
        exit 0
        """;

    internal static string[] FindMissingNokiaApplications(string inventory)
    {
        var rows = inventory.Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Split('\t')).Where(fields => fields.Length == 3)
            .ToDictionary(fields => fields[0], fields => (Version: fields[1], Status: fields[2]), StringComparer.Ordinal);
        if (!rows.TryGetValue("mp-harmattan-005-pr", out var system) ||
            system != ("40.2012.21-3", "install ok installed")) return [];
        var result = new List<string>();
        foreach (var item in new[]
        {
            (Wrapper: "facebook", Version: "1.3.0+0m8", App: "facebookqml", AppVersion: "1.3.2+0m8"),
            (Wrapper: "twitter", Version: "1.3.50+0m8", App: "twitter-qml", AppVersion: "1.3.50+0m8")
        })
        {
            if (!rows.TryGetValue(item.Wrapper, out var wrapper) || wrapper != (item.Version, "install ok installed")) continue;
            if (rows.TryGetValue(item.App, out var app) && app.Status is not
                ("deinstall ok config-files" or "unknown ok not-installed" or "deinstall ok not-installed")) continue;
            result.Add(item.App + "=" + item.AppVersion);
        }
        return result.ToArray();
    }

    internal static bool IsExactRepairPlan(int? status, string output, string[] specifications)
    {
        if (status != 0 || ParseAptRemovals(output).Count > 0) return false;
        var expected = specifications.ToHashSet(StringComparer.Ordinal);
        var installed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("Inst ", StringComparison.Ordinal))
            {
                // A repair may only add the exact stock versions, never upgrade a package.
                var match = Regex.Match(line, @"^Inst ([a-z0-9+.-]+) \(([^ )]+)[ )]", RegexOptions.CultureInvariant);
                if (!match.Success || !installed.Add(match.Groups[1].Value + "=" + match.Groups[2].Value)) return false;
            }
            else if (line.StartsWith("Conf ", StringComparison.Ordinal))
            {
                var match = Regex.Match(line, @"^Conf ([a-z0-9+.-]+) \(([^ )]+)[ )]", RegexOptions.CultureInvariant);
                if (!match.Success || !expected.Contains(match.Groups[1].Value + "=" + match.Groups[2].Value)) return false;
            }
        }
        return expected.Count > 0 && installed.SetEquals(expected);
    }

    private static async Task<(string[] Packages, string Output, bool Ready, bool Healthy)> DependencyRepairPlanAsync(
        SshClient client, CancellationToken cancellationToken)
    {
        var inventory = await RunMaintenanceScriptAsync(client, DependencyInventory, null, cancellationToken);
        RequireSuccess(inventory, "L’état des dépendances Nokia n’a pas pu être lu.");
        var packages = FindMissingNokiaApplications(inventory.Output);
        var command = packages.Length > 0 ? "apt-get -s" + AptInstallOptions + " install " + string.Join(' ', packages.Select(Quote)) : "apt-get -s check";
        var plan = await RunMaintenanceScriptAsync(client, command, null, cancellationToken);
        return (packages, plan.Output, IsExactRepairPlan(plan.Status, plan.Output, packages), packages.Length == 0 && plan.Status == 0);
    }

    private static async Task<N9MaintenanceReport> DiagnoseDependenciesAsync(SshClient client, CancellationToken cancellationToken)
    {
        var plan = await DependencyRepairPlanAsync(client, cancellationToken);
        return new N9MaintenanceReport(plan.Ready ? "Réparation ciblée disponible" : plan.Healthy ? "Dépendances en bon état" : "Dépendances à examiner",
            (plan.Ready ? "Les applications Nokia manquantes seront réinstallées dans leurs versions compatibles avec Harmattan : " +
                string.Join(", ", plan.Packages) + ". Le paquet système est conservé. Les services Facebook/Twitter eux-mêmes ne sont pas rétablis.\n\n" : "") +
            LimitOutput(plan.Output), plan.Ready, "Réparer les dépendances Nokia");
    }

    private static async Task<N9MaintenanceReport> RepairDependenciesAsync(SshClient client, byte[] password,
        CancellationToken cancellationToken)
    {
        var plan = await DependencyRepairPlanAsync(client, cancellationToken);
        if (!plan.Ready) throw new N9ConnectionException("Aucune réparation limitée aux applications Nokia manquantes n’a été validée.\n" + LimitOutput(plan.Output));
        await ReleasePackageManagerAsync(client, password, cancellationToken);
        var backup = "/var/lib/resurectphone/maintenance/" + Guid.NewGuid().ToString("N");
        var names = plan.Packages.Select(spec => spec.Split('=')[0]).ToArray();
        // Restore only the additions made by this repair; keep configuration files.
        var restore = string.Join('\n', plan.Packages.Select(spec =>
            "test \"$(dpkg-query -W -f='${Version}' " + Quote(spec.Split('=')[0]) + ")\" = " + Quote(spec.Split('=')[1]))) +
            "\ndpkg --force-depends --remove " + string.Join(' ', names.Select(Quote)) +
            "\necho 'Applications ajoutées retirées. Les dépendances Nokia précédemment cassées sont à nouveau présentes.'";
        await using var relay = new N9RepositoryRelay(client);
        var script = BackupPreamble(backup, restore) + "dpkg-query -W > \"$backup/packages-before\"\n" +
            "set -e\nexport DEBIAN_FRONTEND=noninteractive\napt-get -y" + AptInstallOptions +
            " -o Dpkg::Options::=--force-confold install " + string.Join(' ', plan.Packages.Select(Quote)) + relay.AptOptions +
            "\nfor package in " + string.Join(' ', names.Select(Quote)) +
            "; do test \"$(dpkg-query -W -f='${Status}' \"$package\")\" = 'install ok installed'; done\n" +
            "test \"$(dpkg-query -W -f='${Status}' mp-harmattan-005-pr)\" = 'install ok installed'\napt-get check\n";
        var result = await RunMaintenanceScriptAsync(client, script, password, cancellationToken, TimeSpan.FromMinutes(10));
        if (result.Status != 0) throw new N9MaintenanceFailureException("Réparation interrompue.\n" + LimitOutput(result.Output), backup);
        return new N9MaintenanceReport("Dépendances Nokia réparées", LimitOutput(result.Output), BackupPath: backup);
    }
}
