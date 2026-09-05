using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.App.Presentation;

public sealed class RecoveryFeatureViewModel
{
    public RecoveryFeatureViewModel(RecoveryFeature feature, DetectedPhone? phone)
    {
        Title = feature.Title;
        Description = feature.Description;
        RiskText = feature.Risk switch
        {
            RecoveryRisk.ReadOnly => "Lecture seule",
            RecoveryRisk.ReversibleChange => "Modification réversible",
            RecoveryRisk.SystemChange => "Modification du système",
            RecoveryRisk.FirmwareChange => "Modification du micrologiciel",
            _ => string.Empty
        };

        IsCompatible = phone is not null && feature.Supports(phone);
        var isDeveloperTool = feature.Area == RecoveryArea.DeveloperTools;
        StatusText = feature.Availability switch
        {
            RecoveryAvailability.Ready when IsCompatible => "Disponible",
            RecoveryAvailability.Ready => "Téléphone compatible requis",
            RecoveryAvailability.Researching when isDeveloperTool => "Paquets en cours d’inventaire",
            RecoveryAvailability.Researching => "En cours d’étude",
            _ => "Prévu"
        };
        IsAvailable = feature.Availability == RecoveryAvailability.Ready && IsCompatible;
        ActionText = isDeveloperTool ? "Installer" : "Commencer";
    }

    public string Title { get; }
    public string Description { get; }
    public string RiskText { get; }
    public string StatusText { get; }
    public string ActionText { get; }
    public bool IsCompatible { get; }
    public bool IsAvailable { get; }
}
