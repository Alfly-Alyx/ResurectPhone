using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Recovery;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.App.Presentation;

public sealed class RecoveryFeatureViewModel
{
    public RecoveryFeatureViewModel(RecoveryFeature feature, DetectedPhone? phone, Action<RecoveryFeature>? execute = null)
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
        if (feature.SupportedPlatforms.Contains(PhonePlatform.MeeGoHarmattan) && N9MaintenanceCatalog.FeatureIds.Contains(feature.Id) &&
            phone?.Platform == PhonePlatform.MeeGoHarmattan)
        {
            IsCompatible = true;
            IsAvailable = execute is not null;
            StatusText = IsAvailable ? (feature.Id is "n9.firmware" or "n9.nokia-store" ? "Diagnostic disponible" : "Prêt à vérifier") : "Connexion SSH requise";
            ActionText = "Ouvrir";
        }
        ActionCommand = new RelayCommand(() => execute?.Invoke(feature), () => IsAvailable && execute is not null);
    }

    public string Title { get; }
    public string Description { get; }
    public string RiskText { get; }
    public string StatusText { get; }
    public string ActionText { get; }
    public bool IsCompatible { get; }
    public bool IsAvailable { get; }
    public RelayCommand ActionCommand { get; }
}
