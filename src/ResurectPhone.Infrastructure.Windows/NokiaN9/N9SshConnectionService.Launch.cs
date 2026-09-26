using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public Task<N9MaintenanceReport> LaunchApplicationAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        var (name, binary, command, process) = applicationId switch
        {
            "meeshop" => ("MeeShop", "/opt/meeshop/bin/meeshop", "env TERM=xterm LD_LIBRARY_PATH=/opt/meeshop/lib /usr/bin/invoker --splash=/opt/meeshop/share/meeshop-splash.png --type=d -s /opt/meeshop/bin/meeshop", "meeshop"),
            "warehouse" => ("Warehouse", "/opt/warehouse/bin/warehouse", "/usr/bin/xdg-open openrepos://client/start", "warehouse"),
            "maps" => ("Cartes", "/usr/bin/maps", "/usr/bin/invoker --type=e /usr/bin/maps", "maps"),
            "drive" => ("Drive", "/usr/bin/drive-qml", "/usr/bin/drive-qml", "drive-qml"),
            "browser" => ("Navigateur", "/usr/bin/grob", "/usr/bin/invoker --type=m /usr/bin/grob", "grob"),
            _ => throw new ArgumentException("Application de test inconnue.", nameof(applicationId))
        };
        return WithMaintenanceClientAsync(async (client, _) =>
        {
            var script = "set -e\ntest -x " + binary + "\n" +
                "session=$(sed -n -e 's/^export DBUS_SESSION_BUS_ADDRESS=//p' -e 's/^DBUS_SESSION_BUS_ADDRESS=//p' /tmp/session_bus_address.user | head -n 1)\n" +
                "case \"$session\" in unix:*) ;; *) echo 'Session graphique du N9 indisponible.' >&2; exit 1;; esac\n" +
                "export DBUS_SESSION_BUS_ADDRESS=\"$session\" DISPLAY=:0\n" +
                "nohup " + command + " >/tmp/resurectphone-launch-" + applicationId + ".log 2>&1 </dev/null &\n" +
                "sleep 3\npidof " + process;
            var result = await RunMaintenanceScriptAsync(client, script, null, cancellationToken);
            RequireSuccess(result, name + " n’a pas démarré. Vérifiez son installation et déverrouillez le N9.");
            return new N9MaintenanceReport(name + " démarré sur le N9", "Le processus de l’application est présent. Son écran s’utilise sur le téléphone.");
        }, cancellationToken);
    }
}
