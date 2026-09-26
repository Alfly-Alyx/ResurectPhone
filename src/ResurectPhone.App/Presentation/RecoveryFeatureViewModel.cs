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
            StatusText = IsAvailable ? "Action sur le N9" : "Connexion SSH requise";
            ActionText = feature.Id switch
            {
                "device.identity" => "Identifier",
                "n9.firmware" => "Préparer le noyau",
                "n9.repositories" => "Réparer et actualiser",
                "n9.dependencies" => "Réparer",
                "n9.nokia-store" => "Installer une alternative",
                "n9.alternative-stores" => "Choisir et installer",
                "n9.package-backup" => "Sauvegarder",
                "n9.package-install" => "Installer un .deb",
                "n9.cleanup" => "Choisir et supprimer",
                "n9.internet" => "Installer ou vérifier TLS",
                "n9.gps" => "Configurer GPS et Cartes",
                "n9.account" => "Supprimer les demandes",
                _ => "Installer ces outils"
            };
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
