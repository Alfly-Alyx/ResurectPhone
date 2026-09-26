using System.Text;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9AdministratorAuthenticationTests
{
    [Fact]
    public async Task FactoryPassword_IsTriedFirstEvenWhenCustomPasswordWasProvided()
    {
        var attempts = new List<string>();
        var custom = "custom-secret".ToCharArray();
        byte[]? retained = null;
        var result = await N9AdministratorAuthentication.UseAsync(
            candidate => { attempts.Add(Encoding.UTF8.GetString(candidate)); return Task.FromResult(true); },
            candidate => { retained = candidate; return Task.FromResult(42); },
            custom);

        Assert.Equal(42, result);
        Assert.Equal(new[] { "rootme" }, attempts);
        Assert.All(custom, character => Assert.Equal('\0', character));
        Assert.All(retained!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task CustomPassword_IsTriedOnlyAfterFactoryPasswordIsRejected()
    {
        var attempts = new List<string>();
        var buffers = new List<byte[]>();
        var custom = "changed-secret".ToCharArray();
        await N9AdministratorAuthentication.UseAsync(
            candidate =>
            {
                attempts.Add(Encoding.UTF8.GetString(candidate));
                buffers.Add(candidate);
                return Task.FromResult(attempts.Count == 2);
            },
            _ => Task.FromResult(true), custom);

        Assert.Equal(new[] { "rootme", "changed-secret" }, attempts);
        Assert.All(buffers, buffer => Assert.All(buffer, value => Assert.Equal(0, value)));
        Assert.All(custom, character => Assert.Equal('\0', character));
    }

    [Fact]
    public async Task RejectedDefault_RequestsCustomPasswordWithoutRunningAdminAction()
    {
        var ran = false;
        byte[]? retained = null;
        await Assert.ThrowsAsync<N9AdministratorRequiredException>(() =>
            N9AdministratorAuthentication.UseAsync(
                candidate => { retained = candidate; return Task.FromResult(false); },
                _ => { ran = true; return Task.FromResult(true); }));
        Assert.False(ran);
        Assert.All(retained!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task InterruptedAuthentication_ClearsBothPasswords()
    {
        byte[]? retained = null;
        var custom = "changed-secret".ToCharArray();
        await Assert.ThrowsAsync<IOException>(() => N9AdministratorAuthentication.UseAsync<int>(
            candidate => { retained = candidate; throw new IOException("connection lost"); },
            _ => Task.FromResult(0), custom));
        Assert.All(retained!, value => Assert.Equal(0, value));
        Assert.All(custom, character => Assert.Equal('\0', character));
    }
}
