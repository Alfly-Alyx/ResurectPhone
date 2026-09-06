using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ResurectPhone.Core.Android;

namespace ResurectPhone.Infrastructure.Windows.Android;

public sealed partial class AndroidAdbTaskManagerService : IAndroidTaskManagerService
{
    private const int MaximumOutputCharacters = 16 * 1024 * 1024;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<string, CpuBaseline> _baselines = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _captureGate = new(1, 1);

    public AndroidAdbTaskManagerService(string? configuredAdbPath = null)
    {
        AdbPath = LocateAdb(configuredAdbPath);
    }

    internal string? AdbPath { get; }
    public bool IsAdbAvailable => AdbPath is not null;

    public async Task<IReadOnlyList<AndroidDeviceIdentity>> DiscoverDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAdbAvailable();
        var result = await RunAdbAsync(["devices", "-l"], cancellationToken);
        EnsureSuccess(result, "ADB n’a pas pu obtenir la liste des téléphones Android.");
        var devices = ParseDevices(result.StandardOutput);
        var identities = new List<AndroidDeviceIdentity>(devices.Count);
        foreach (var device in devices)
        {
            if (!device.IsReady)
            {
                identities.Add(device);
                continue;
            }

            var identityResult = await RunAdbAsync(
                [
                    "-s", device.Serial, "shell",
                    "printf 'MANUFACTURER\\t%s\\n' \"$(getprop ro.product.manufacturer)\"; " +
                    "printf 'MODEL\\t%s\\n' \"$(getprop ro.product.model)\"; " +
                    "printf 'ANDROID\\t%s\\n' \"$(getprop ro.build.version.release)\"; " +
                    "printf 'API\\t%s\\n' \"$(getprop ro.build.version.sdk)\""
                ],
                cancellationToken);
            identities.Add(identityResult.ExitCode == 0
                ? ParseIdentity(device, identityResult.StandardOutput)
                : device);
        }
        return identities;
    }

    public async Task<AndroidProcessSnapshot> CaptureAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        EnsureAdbAvailable();
        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            var result = await RunAdbAsync(
                ["-s", serial, "shell", SnapshotScript],
                cancellationToken);
            EnsureSuccess(result, "ADB n’a pas pu lire les processus du téléphone.");
            var raw = ParseRawSnapshot(result.StandardOutput);
            _baselines.TryGetValue(serial, out var previous);
            var snapshot = ApplyUsage(raw, previous);
            _baselines[serial] = CpuBaseline.From(raw);
            return snapshot;
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public async Task<AndroidMemoryReleaseResult> ReleaseMemoryAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        var before = await CaptureAsync(serial, cancellationToken);
        var result = await RunAdbAsync(
            ["-s", serial, "shell", "am", "kill-all"],
            cancellationToken);
        EnsureSuccess(result, "Android n’a pas autorisé la fermeture des applications en arrière-plan.");
        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        var after = await CaptureAsync(serial, cancellationToken);
        return new(before.AvailableMemoryBytes, after.AvailableMemoryBytes, after);
    }

    internal static IReadOnlyList<AndroidDeviceIdentity> ParseDevices(string output)
    {
        var devices = new List<AndroidDeviceIdentity>();
        foreach (var line in output.Replace("\r", string.Empty, StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Skip(1))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            var serial = parts[0];
            var state = parts[1];
            var model = ReadAdbProperty(parts, "model").Replace('_', ' ');
            devices.Add(new(serial, state, string.Empty, model, string.Empty, string.Empty));
        }
        return devices;
    }

    internal static RawSnapshot ParseRawSnapshot(string output)
    {
        var section = SnapshotSection.None;
        long totalCpuTicks = 0;
        long idleCpuTicks = 0;
        long totalMemoryKb = 0;
        long availableMemoryKb = 0;
        long freeMemoryKb = 0;
        long buffersKb = 0;
        long cachedKb = 0;
        var pageSize = 4096L;
        var processes = new Dictionary<int, RawProcess>();
        var psLines = new List<string>();

        foreach (var rawLine in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line == "__CPU__") { section = SnapshotSection.Cpu; continue; }
            if (line == "__MEMORY__") { section = SnapshotSection.Memory; continue; }
            if (line == "__PAGE__") { section = SnapshotSection.Page; continue; }
            if (line == "__PROCESS_STATS__") { section = SnapshotSection.ProcessStats; continue; }
            if (line == "__PROCESS_LIST__") { section = SnapshotSection.ProcessList; continue; }

            switch (section)
            {
                case SnapshotSection.Cpu when line.StartsWith("cpu ", StringComparison.Ordinal):
                    (totalCpuTicks, idleCpuTicks) = ParseCpu(line);
                    break;
                case SnapshotSection.Memory:
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) totalMemoryKb = ParseFirstLong(line);
                    else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) availableMemoryKb = ParseFirstLong(line);
                    else if (line.StartsWith("MemFree:", StringComparison.Ordinal)) freeMemoryKb = ParseFirstLong(line);
                    else if (line.StartsWith("Buffers:", StringComparison.Ordinal)) buffersKb = ParseFirstLong(line);
                    else if (line.StartsWith("Cached:", StringComparison.Ordinal)) cachedKb = ParseFirstLong(line);
                    break;
                case SnapshotSection.Page:
                    if (long.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPageSize) &&
                        parsedPageSize is >= 1024 and <= 65536)
                        pageSize = parsedPageSize;
                    break;
                case SnapshotSection.ProcessStats when line.StartsWith("PROC\t", StringComparison.Ordinal):
                    var process = ParseProcessStatLine(line, pageSize);
                    if (process is not null)
                    {
                        processes[process.ProcessId] = process;
                    }
                    break;
                case SnapshotSection.ProcessList when !string.IsNullOrWhiteSpace(line):
                    psLines.Add(line);
                    break;
            }
        }

        var psProcesses = ParsePsProcesses(psLines);
        var hasFallbackProcesses = false;
        foreach (var process in psProcesses)
        {
            if (processes.TryGetValue(process.ProcessId, out var existing))
            {
                processes[process.ProcessId] = existing with
                {
                    User = process.User,
                    State = string.IsNullOrWhiteSpace(existing.State) ? process.State : existing.State,
                    Name = string.IsNullOrWhiteSpace(existing.Name) ? process.Name : existing.Name
                };
            }
            else
            {
                processes[process.ProcessId] = process;
                hasFallbackProcesses = true;
            }
        }

        if (availableMemoryKb <= 0)
            availableMemoryKb = freeMemoryKb + buffersKb + cachedKb;
        var hasHiddenCounters = hasFallbackProcesses || processes.Values.Any(process =>
            process.StorageReadBytes is null || process.StorageWriteBytes is null);
        return new(
            DateTimeOffset.UtcNow,
            totalCpuTicks,
            idleCpuTicks,
            KilobytesToBytes(totalMemoryKb),
            KilobytesToBytes(availableMemoryKb),
            processes.Values.ToArray(),
            hasHiddenCounters);
    }

    internal static AndroidProcessSnapshot ApplyUsage(RawSnapshot current, CpuBaseline? previous)
    {
        var totalDelta = previous is null ? 0 : current.TotalCpuTicks - previous.TotalCpuTicks;
        var idleDelta = previous is null ? 0 : current.IdleCpuTicks - previous.IdleCpuTicks;
        double? totalCpu = totalDelta > 0
            ? Math.Clamp((totalDelta - Math.Max(0, idleDelta)) * 100d / totalDelta, 0, 100)
            : null;

        var processes = current.Processes.Select(process =>
        {
            double? cpu = null;
            double? readRate = null;
            double? writeRate = null;
            if (previous is not null && totalDelta > 0 && process.StartTimeTicks > 0 &&
                previous.Processes.TryGetValue(process.ProcessId, out var oldProcess) &&
                oldProcess.StartTimeTicks == process.StartTimeTicks)
            {
                var processDelta = process.CpuTicks - oldProcess.CpuTicks;
                if (processDelta >= 0)
                    cpu = Math.Clamp(processDelta * 100d / totalDelta, 0, 100);
                var elapsedSeconds = (current.CapturedAt - previous.CapturedAt).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    readRate = CalculateRate(process.StorageReadBytes, oldProcess.StorageReadBytes, elapsedSeconds);
                    writeRate = CalculateRate(process.StorageWriteBytes, oldProcess.StorageWriteBytes, elapsedSeconds);
                }
            }

            return new AndroidProcessInfo(
                process.ProcessId,
                process.ParentProcessId,
                process.Name,
                process.User,
                process.State,
                process.CommandLine,
                cpu,
                process.ResidentMemoryBytes,
                readRate,
                writeRate,
                process.ThreadCount);
        }).OrderByDescending(process => process.CpuUsagePercent ?? -1)
          .ThenByDescending(process => process.ResidentMemoryBytes ?? -1)
          .ThenBy(process => process.Name, StringComparer.CurrentCultureIgnoreCase)
          .ToArray();

        return new(
            current.CapturedAt,
            processes,
            totalCpu,
            current.TotalMemoryBytes,
            current.AvailableMemoryBytes,
            current.IsPartial,
            current.IsPartial
                ? "Android masque certains compteurs de processus ; les lignes restent affichées avec « — ». Le réseau et le GPU ne disposent pas d’une mesure ADB stable par processus."
                : "CPU, mémoire et entrées/sorties sont disponibles. Le réseau et le GPU ne disposent pas d’une mesure ADB stable par processus.");
    }

    private async Task<AdbResult> RunAdbAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var adbPath = AdbPath ?? throw new AndroidAdbException(
            "ADB est introuvable. Android Platform Tools devra être installé ou intégré à ResurectPhone.");
        var start = new ProcessStartInfo(adbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(adbPath) ?? AppContext.BaseDirectory
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new AndroidAdbException("ADB n’a pas pu démarrer.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (stdout.Length > MaximumOutputCharacters || stderr.Length > MaximumOutputCharacters)
                throw new AndroidAdbException("ADB a renvoyé une quantité de données anormalement élevée.");
            return new(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryStop(process);
            throw new AndroidAdbException("La commande ADB a dépassé le délai autorisé.");
        }
        catch (OperationCanceledException)
        {
            TryStop(process);
            throw;
        }
        catch (Win32Exception exception)
        {
            throw new AndroidAdbException("ADB n’a pas pu être exécuté.", exception);
        }
    }

    private static AndroidDeviceIdentity ParseIdentity(AndroidDeviceIdentity device, string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var separator = line.IndexOf('\t');
            if (separator > 0)
                values[line[..separator]] = line[(separator + 1)..].Trim();
        }
        return device with
        {
            Manufacturer = values.GetValueOrDefault("MANUFACTURER", string.Empty),
            Model = values.GetValueOrDefault("MODEL", device.Model),
            AndroidVersion = values.GetValueOrDefault("ANDROID", string.Empty),
            ApiLevel = values.GetValueOrDefault("API", string.Empty)
        };
    }

    private static RawProcess? ParseProcessStatLine(string line, long pageSize)
    {
        var fields = line.Split('\t', 5, StringSplitOptions.None);
        if (fields.Length != 5 || fields[0] != "PROC")
            return null;
        var command = fields[1].Trim();
        var readBytes = ParseOptionalLong(fields[2]);
        var writeBytes = ParseOptionalLong(fields[3]);
        var stat = fields[4].Trim();
        var open = stat.IndexOf('(');
        var close = stat.LastIndexOf(')');
        if (open <= 0 || close <= open || !int.TryParse(stat[..open].Trim(), out var pid))
            return null;
        var values = stat[(close + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 22 || !int.TryParse(values[1], out var parentPid) ||
            !long.TryParse(values[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userTicks) ||
            !long.TryParse(values[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var systemTicks) ||
            !int.TryParse(values[17], out var threads) ||
            !long.TryParse(values[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startTime) ||
            !long.TryParse(values[21], NumberStyles.Integer, CultureInfo.InvariantCulture, out var residentPages))
            return null;
        if (userTicks < 0 || systemTicks < 0 || startTime < 0 || userTicks > long.MaxValue - systemTicks)
            return null;
        var name = stat[(open + 1)..close];
        long? residentMemoryBytes = residentPages >= 0 && residentPages <= long.MaxValue / pageSize
            ? residentPages * pageSize
            : null;
        return new(
            pid,
            parentPid,
            name,
            string.Empty,
            DescribeState(values[0]),
            string.IsNullOrWhiteSpace(command) ? name : command,
            userTicks + systemTicks,
            startTime,
            residentMemoryBytes,
            readBytes,
            writeBytes,
            threads >= 0 ? threads : null);
    }

    private static IReadOnlyList<RawProcess> ParsePsProcesses(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2)
            return [];
        var headers = WhitespaceRegex().Split(lines[0].Trim());
        var pidIndex = FindColumn(headers, "PID");
        if (pidIndex < 0)
            return [];
        var ppidIndex = FindColumn(headers, "PPID");
        var userIndex = FindColumn(headers, "USER", "UID");
        var stateIndex = FindColumn(headers, "STAT", "S");
        var rssIndex = FindColumn(headers, "RSS");
        var nameIndex = FindColumn(headers, "NAME", "CMD", "COMMAND");
        var processes = new List<RawProcess>();
        foreach (var line in lines.Skip(1))
        {
            var values = WhitespaceRegex().Split(line.Trim());
            if (pidIndex >= values.Length || !int.TryParse(values[pidIndex], out var pid))
                continue;
            var parentPid = ppidIndex >= 0 && ppidIndex < values.Length && int.TryParse(values[ppidIndex], out var parsedParent)
                ? parsedParent
                : 0;
            var user = userIndex >= 0 && userIndex < values.Length ? values[userIndex] : string.Empty;
            var state = stateIndex >= 0 && stateIndex < values.Length ? DescribeState(values[stateIndex]) : string.Empty;
            var name = nameIndex >= 0 && nameIndex < values.Length ? values[nameIndex] : $"PID {pid}";
            var memoryKilobytes = rssIndex >= 0 && rssIndex < values.Length
                ? ParseMemoryKilobytes(values[rssIndex])
                : null;
            long? memory = memoryKilobytes is >= 0 and <= long.MaxValue / 1024
                ? memoryKilobytes * 1024
                : null;
            processes.Add(new(pid, parentPid, name, user, state, name, 0, 0, memory, null, null, null));
        }
        return processes;
    }

    private static (long Total, long Idle) ParseCpu(string line)
    {
        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : -1)
            .ToArray();
        if (values.Length < 4 || values.Any(value => value < 0))
            return (0, 0);
        return (values.Sum(), values[3] + (values.Length > 4 ? values[4] : 0));
    }

    private static long ParseFirstLong(string line)
    {
        foreach (var value in WhitespaceRegex().Split(line))
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return number;
        return 0;
    }

    private static long KilobytesToBytes(long value) =>
        value is >= 0 and <= long.MaxValue / 1024 ? value * 1024 : 0;

    private static long? ParseMemoryKilobytes(string value)
    {
        var normalized = value.Trim();
        var multiplier = normalized.EndsWith("G", StringComparison.OrdinalIgnoreCase) ? 1024L * 1024 :
            normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase) ? 1024L : 1L;
        normalized = normalized.TrimEnd('K', 'k', 'M', 'm', 'G', 'g');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ||
            !double.IsFinite(amount) || amount < 0 || amount > long.MaxValue / multiplier)
            return null;
        return (long)(amount * multiplier);
    }

    private static long? ParseOptionalLong(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0
            ? number
            : null;

    private static double? CalculateRate(long? current, long? previous, double elapsedSeconds) =>
        current is { } currentValue && previous is { } previousValue && currentValue >= previousValue
            ? (currentValue - previousValue) / elapsedSeconds
            : null;

    private static int FindColumn(IReadOnlyList<string> headers, params string[] names)
    {
        for (var index = 0; index < headers.Count; index++)
            if (names.Any(name => headers[index].Equals(name, StringComparison.OrdinalIgnoreCase)))
                return index;
        return -1;
    }

    private static string DescribeState(string value) => value.Length == 0 ? string.Empty : value[0] switch
    {
        'R' => "En cours",
        'S' => "Veille",
        'D' => "Attente disque",
        'T' or 't' => "Suspendu",
        'Z' => "Zombie",
        'I' => "Inactif",
        _ => value
    };

    private static string? LocateAdb(string? configuredPath)
    {
        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        var candidates = new[]
        {
            configuredPath,
            Environment.GetEnvironmentVariable("ADB_PATH"),
            Path.Combine(AppContext.BaseDirectory, "tools", "platform-tools", executable),
            Path.Combine(userProfile, "Android", "Sdk", "platform-tools", executable),
            Path.Combine(localAppData, "Android", "Sdk", "platform-tools", executable),
            string.IsNullOrWhiteSpace(sdkRoot) ? null : Path.Combine(sdkRoot, "platform-tools", executable),
            string.IsNullOrWhiteSpace(androidHome) ? null : Path.Combine(androidHome, "platform-tools", executable)
        };
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim(), executable))
            .FirstOrDefault(File.Exists);
    }

    private void EnsureAdbAvailable()
    {
        if (!IsAdbAvailable)
            throw new AndroidAdbException("ADB est introuvable. Android Platform Tools devra être installé ou intégré à ResurectPhone.");
    }

    private static void EnsureSuccess(AdbResult result, string message)
    {
        if (result.ExitCode != 0)
            throw new AndroidAdbException(string.IsNullOrWhiteSpace(result.StandardError)
                ? message
                : $"{message} {SanitizeError(result.StandardError)}");
    }

    private static void ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Length > 256 || serial.Any(char.IsControl))
            throw new ArgumentException("L’identifiant ADB du téléphone est invalide.", nameof(serial));
    }

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static string SanitizeError(string error)
    {
        var sanitized = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 500 ? sanitized : sanitized[..500] + "…";
    }

    private static string ReadAdbProperty(IEnumerable<string> parts, string name) =>
        parts.FirstOrDefault(part => part.StartsWith(name + ":", StringComparison.Ordinal))?[(name.Length + 1)..] ?? string.Empty;

    private const string SnapshotScriptSource = """
        printf '__CPU__\n'
        head -n 1 /proc/stat 2>/dev/null
        printf '__MEMORY__\n'
        grep -E '^(MemTotal|MemAvailable|MemFree|Buffers|Cached):' /proc/meminfo 2>/dev/null
        printf '__PAGE__\n'
        getconf PAGESIZE 2>/dev/null || echo 4096
        printf '__PROCESS_STATS__\n'
        for path in /proc/[0-9]*; do
            pid=${path#/proc/}
            stat=$(tr '\011\012\015' '   ' < "$path/stat" 2>/dev/null) || continue
            command=$(tr '\000\011\012\015' '    ' < "$path/cmdline" 2>/dev/null | cut -c 1-256)
            read_bytes=$(grep '^read_bytes:' "$path/io" 2>/dev/null | cut -d ' ' -f 2)
            write_bytes=$(grep '^write_bytes:' "$path/io" 2>/dev/null | cut -d ' ' -f 2)
            printf 'PROC\t%s\t%s\t%s\t%s\n' "$command" "$read_bytes" "$write_bytes" "$stat"
        done
        printf '__PROCESS_LIST__\n'
        ps -A -o PID,PPID,USER,STAT,RSS,NAME 2>/dev/null || ps -A 2>/dev/null || ps 2>/dev/null
        """;

    private static readonly string SnapshotScript = SnapshotScriptSource
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');

    internal sealed record RawSnapshot(
        DateTimeOffset CapturedAt,
        long TotalCpuTicks,
        long IdleCpuTicks,
        long TotalMemoryBytes,
        long AvailableMemoryBytes,
        IReadOnlyList<RawProcess> Processes,
        bool IsPartial);

    internal sealed record RawProcess(
        int ProcessId,
        int ParentProcessId,
        string Name,
        string User,
        string State,
        string CommandLine,
        long CpuTicks,
        long StartTimeTicks,
        long? ResidentMemoryBytes,
        long? StorageReadBytes,
        long? StorageWriteBytes,
        int? ThreadCount);

    internal sealed record CpuBaseline(
        DateTimeOffset CapturedAt,
        long TotalCpuTicks,
        long IdleCpuTicks,
        IReadOnlyDictionary<int, ProcessBaseline> Processes)
    {
        public static CpuBaseline From(RawSnapshot snapshot) => new(
            snapshot.CapturedAt,
            snapshot.TotalCpuTicks,
            snapshot.IdleCpuTicks,
            snapshot.Processes.Where(process => process.StartTimeTicks > 0)
                .ToDictionary(
                    process => process.ProcessId,
                    process => new ProcessBaseline(
                        process.CpuTicks,
                        process.StartTimeTicks,
                        process.StorageReadBytes,
                        process.StorageWriteBytes)));
    }

    internal sealed record ProcessBaseline(
        long CpuTicks,
        long StartTimeTicks,
        long? StorageReadBytes,
        long? StorageWriteBytes);
    private sealed record AdbResult(int ExitCode, string StandardOutput, string StandardError);

    private enum SnapshotSection
    {
        None,
        Cpu,
        Memory,
        Page,
        ProcessStats,
        ProcessList
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
