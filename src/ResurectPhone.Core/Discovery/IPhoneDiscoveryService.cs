using ResurectPhone.Core.Devices;

namespace ResurectPhone.Core.Discovery;

public interface IPhoneDiscoveryService
{
    Task<IReadOnlyList<DetectedPhone>> DiscoverAsync(CancellationToken cancellationToken = default);
}
