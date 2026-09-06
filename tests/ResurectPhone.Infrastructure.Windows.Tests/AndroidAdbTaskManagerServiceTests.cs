using ResurectPhone.Infrastructure.Windows.Android;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class AndroidAdbTaskManagerServiceTests
{
    [Fact]
    public void ParseDevices_ReadsReadyAndUnauthorizedPhones()
    {
        const string output = """
            List of devices attached
            emulator-5554 device product:sdk_gphone64_x86_64 model:Pixel_8 device:emu64xa
            R58M123456 unauthorized usb:1-2 transport_id:4

            """;

        var devices = AndroidAdbTaskManagerService.ParseDevices(output);

        Assert.Collection(
            devices,
            device =>
            {
                Assert.Equal("emulator-5554", device.Serial);
                Assert.True(device.IsReady);
                Assert.Equal("Pixel 8", device.Model);
            },
            device =>
            {
                Assert.Equal("R58M123456", device.Serial);
                Assert.False(device.IsReady);
                Assert.Equal("unauthorized", device.State);
            });
    }

    [Fact]
    public void ParseRawSnapshot_CombinesProcAndPsData()
    {
        const string output = """
            __CPU__
            cpu  100 20 30 800 50 0 0 0 0 0
            __MEMORY__
            MemTotal:        2048000 kB
            MemAvailable:     512000 kB
            __PAGE__
            4096
            __PROCESS_STATS__
            PROC	/system/bin/demo --service	4096	8192	123 (com.demo) S 1 0 0 0 0 0 0 0 0 0 10 5 0 0 20 0 3 0 1000 12345 50
            __PROCESS_LIST__
            PID PPID USER STAT RSS NAME
            123 1 u0_a123 S 200 com.demo
            456 1 shell R 2M fallback
            """;

        var snapshot = AndroidAdbTaskManagerService.ParseRawSnapshot(output);

        Assert.Equal(1_000, snapshot.TotalCpuTicks);
        Assert.Equal(850, snapshot.IdleCpuTicks);
        Assert.Equal(2_048_000L * 1024, snapshot.TotalMemoryBytes);
        Assert.Equal(512_000L * 1024, snapshot.AvailableMemoryBytes);
        Assert.True(snapshot.IsPartial);
        Assert.Equal(2, snapshot.Processes.Count);
        var detailed = Assert.Single(snapshot.Processes, process => process.ProcessId == 123);
        Assert.Equal("u0_a123", detailed.User);
        Assert.Equal("/system/bin/demo --service", detailed.CommandLine);
        Assert.Equal(50L * 4096, detailed.ResidentMemoryBytes);
        Assert.Equal(4096, detailed.StorageReadBytes);
        Assert.Equal(8192, detailed.StorageWriteBytes);
        Assert.Equal(3, detailed.ThreadCount);
        var fallback = Assert.Single(snapshot.Processes, process => process.ProcessId == 456);
        Assert.Equal(2L * 1024 * 1024, fallback.ResidentMemoryBytes);
        Assert.Equal("En cours", fallback.State);
    }

    [Fact]
    public void ApplyUsage_ComputesDeviceAndProcessCpuFromTwoSamples()
    {
        var previous = Baseline(total: 1000, idle: 800, processTicks: 50, startTime: 1000);
        var current = Raw(total: 1200, idle: 900, processTicks: 70, startTime: 1000);

        var result = AndroidAdbTaskManagerService.ApplyUsage(current, previous);

        Assert.Equal(50, result.CpuUsagePercent);
        Assert.Equal(10, Assert.Single(result.Processes).CpuUsagePercent);
    }

    [Fact]
    public void ApplyUsage_DoesNotAttributeCpuWhenPidWasReused()
    {
        var previous = Baseline(total: 1000, idle: 800, processTicks: 50, startTime: 1000);
        var current = Raw(total: 1200, idle: 900, processTicks: 70, startTime: 2000);

        var result = AndroidAdbTaskManagerService.ApplyUsage(current, previous);

        Assert.Null(Assert.Single(result.Processes).CpuUsagePercent);
    }

    [Fact]
    public void ApplyUsage_ComputesStorageRatesWhenCountersAreVisible()
    {
        var capturedAt = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var previousRaw = new AndroidAdbTaskManagerService.RawSnapshot(
            capturedAt,
            1000,
            800,
            4096,
            2048,
            [new(123, 1, "demo", "shell", "En cours", "demo", 50, 1000, 4096, 1000, 2000, 3)],
            false);
        var current = new AndroidAdbTaskManagerService.RawSnapshot(
            capturedAt.AddSeconds(2),
            1200,
            900,
            4096,
            2048,
            [new(123, 1, "demo", "shell", "En cours", "demo", 70, 1000, 4096, 5000, 10000, 3)],
            false);

        var result = AndroidAdbTaskManagerService.ApplyUsage(
            current,
            AndroidAdbTaskManagerService.CpuBaseline.From(previousRaw));

        var process = Assert.Single(result.Processes);
        Assert.Equal(2000, process.StorageReadBytesPerSecond);
        Assert.Equal(4000, process.StorageWriteBytesPerSecond);
    }

    private static AndroidAdbTaskManagerService.RawSnapshot Raw(
        long total,
        long idle,
        long processTicks,
        long startTime) => new(
        DateTimeOffset.UtcNow,
        total,
        idle,
        4L * 1024 * 1024 * 1024,
        2L * 1024 * 1024 * 1024,
        [new(123, 1, "demo", "shell", "En cours", "demo", processTicks, startTime, 4096, 1024, 2048, 3)],
        false);

    private static AndroidAdbTaskManagerService.CpuBaseline Baseline(
        long total,
        long idle,
        long processTicks,
        long startTime) => AndroidAdbTaskManagerService.CpuBaseline.From(
        Raw(total, idle, processTicks, startTime));
}
