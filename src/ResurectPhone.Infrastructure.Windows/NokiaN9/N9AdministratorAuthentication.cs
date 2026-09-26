using System.Security.Cryptography;
using System.Text;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal static class N9AdministratorAuthentication
{
    // Public factory credential, deliberately attempted first at the owner's request.
    public static async Task<T> UseAsync<T>(
        Func<byte[], Task<bool>> authenticate,
        Func<byte[], Task<T>> action,
        char[]? customPassword = null)
    {
        var candidate = Encoding.UTF8.GetBytes("rootme");
        try
        {
            if (!await authenticate(candidate))
            {
                CryptographicOperations.ZeroMemory(candidate);
                if (customPassword is null || customPassword.Length == 0)
                    throw new N9AdministratorRequiredException();
                candidate = N9PackageCommands.EncodeAdminPassword(customPassword);
                if (!await authenticate(candidate))
                    throw new N9AdministratorRequiredException();
            }

            return await action(candidate);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
            if (customPassword is not null)
                Array.Clear(customPassword);
        }
    }
}
