using System.Globalization;
using Renci.SshNet;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    private static async Task SynchronizeClockAsync(SshClient client, byte[] password, CancellationToken cancellationToken)
    {
        var current = await RunMaintenanceScriptAsync(client, "date +%s", null, cancellationToken);
        if (long.TryParse(current.Output.Trim(), out var seconds) &&
            Math.Abs(seconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) < 60) return;

        using var resource = typeof(N9SshConnectionService).Assembly.GetManifestResourceStream(
            "ResurectPhone.Infrastructure.Windows.NokiaN9.Resources.sync-time.py")
            ?? throw new InvalidOperationException("Le composant de synchronisation horaire est absent.");
        using var reader = new StreamReader(resource);
        var python = await reader.ReadToEndAsync(cancellationToken);
        // timed undoes date -s. Permit its clock method for root temporarily,
        // without modifying Nokia's policy or access to other methods.
        var script = """
            set -eu
            policy=/etc/dbus-1/system.d/resurectphone-time.conf
            test ! -e "$policy"
            reload() {
                dbus-send --system --type=method_call --print-reply --dest=org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus.ReloadConfig >/dev/null
            }
            cleanup() { rm -f "$policy"; reload; }
            trap cleanup 0
            trap 'exit 1' HUP INT TERM
            umask 022
            cat > "$policy" <<'RESURECTPHONE_TIME_POLICY'
            <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-BUS Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
            <busconfig><policy user="root"><allow send_destination="com.nokia.time" send_interface="com.nokia.time" send_member="wall_clock_settings"/></policy></busconfig>
            RESURECTPHONE_TIME_POLICY
            chmod 644 "$policy"
            reload
            """ + "\npython - " + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
            " <<'RESURECTPHONE_TIME_PYTHON'\n" + python + "\nRESURECTPHONE_TIME_PYTHON\nsleep 2\ndate -u\n";
        var result = await RunMaintenanceScriptAsync(client, script, password, cancellationToken);
        RequireSuccess(result, "La synchronisation de l’heure du N9 a échoué. La validation TLS exige une date correcte.");
        var verified = await RunMaintenanceScriptAsync(client, "test ! -e /etc/dbus-1/system.d/resurectphone-time.conf && date +%s", null, cancellationToken);
        if (verified.Status != 0 || !long.TryParse(verified.Output.Trim(), out seconds) ||
            Math.Abs(seconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) > 30)
            throw new N9ConnectionException("L’heure ou la fin de sa synchronisation n’a pas été confirmée.");
    }
}
