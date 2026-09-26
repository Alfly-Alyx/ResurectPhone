using System.Buffers.Binary;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9KernelAssetsTests
{
    private static readonly N9DeviceDetails Supported = new()
    { ProductCode = "RM-696", SystemBuild = "DFL61_HARMATTAN_40.2012.21-3_PR_005", SalesCode = "059K114" };
    private static readonly string Row = "059K114,005,RM-696 NDT MALAYSIA CYAN 16GB,7076673555," + N9KernelAssets.Stock005.Name + ",emmc.bin";

    [Fact]
    public void RecoveryImageRequiresExactProductVariantAndUniqueTableRow()
    {
        Assert.Equal(N9KernelAssets.Stock005, N9KernelAssets.ResolveFirmware(Supported, Row));
        Assert.Throws<N9ConnectionException>(() => N9KernelAssets.ResolveFirmware(Supported with { ProductCode = "RM-680" }, Row));
        Assert.Throws<N9ConnectionException>(() => N9KernelAssets.ResolveFirmware(Supported with { SystemBuild = "DFL61_HARMATTAN_40.2012.21-3_PR_001" }, Row));
        Assert.Throws<N9ConnectionException>(() => N9KernelAssets.ResolveFirmware(Supported with { SalesCode = "059AAAA" }, Row));
        Assert.Throws<N9ConnectionException>(() => N9KernelAssets.ResolveFirmware(Supported, Row + "\n" + Row));
        Assert.Throws<N9ConnectionException>(() => N9KernelAssets.ResolveFirmware(Supported, Row.Replace(",005,", ",001,")));
        Assert.Throws<N9ConnectionException>(() => N9KernelAssets.ResolveFirmware(Supported, Row.Replace(N9KernelAssets.Stock005.Name, "other.bin")));
    }

    [Fact]
    public void ArmImageRejectsTruncationWrongMagicAndWrongLength()
    {
        var data = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(36, 4), 0x016f2818);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44, 4), 128);
        N9KernelAssets.ValidateZImage(data);
        Assert.Throws<InvalidDataException>(() => N9KernelAssets.ValidateZImage(data[..47]));
        Assert.Throws<InvalidDataException>(() => N9KernelAssets.ValidateZImage(data[..127]));
        data[36] = 0;
        Assert.Throws<InvalidDataException>(() => N9KernelAssets.ValidateZImage(data));
    }

    [Fact]
    public async Task UnknownActionCannotPrepareRepositoriesAndClearsPassword()
    {
        var password = "example".ToCharArray();
        await Assert.ThrowsAsync<ArgumentException>(() => new N9SshConnectionService().ExecuteActionAsync("n9.devtools.nonexistent", administratorPassword: password));
        Assert.All(password, value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task CancelledPreparationClearsPasswordBeforeLockAcquisition()
    {
        var password = "example".ToCharArray();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new N9SshConnectionService().PrepareKernelAsync(administratorPassword: password, cancellationToken: new(true)));
        Assert.All(password, value => Assert.Equal('\0', value));
    }
}
