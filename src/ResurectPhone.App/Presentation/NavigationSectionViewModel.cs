using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.App.Presentation;

public sealed record NavigationSectionViewModel(
    string Key,
    string FamilyTitle,
    string Glyph,
    string Title,
    string Subtitle,
    RecoveryArea? Area,
    IReadOnlySet<PhonePlatform> Platforms,
    bool IsHome = false);
