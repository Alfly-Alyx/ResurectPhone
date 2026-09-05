using ResurectPhone.Core.Devices;

namespace ResurectPhone.Core.Recovery;

public static class RecoveryCatalog
{
    private static readonly IReadOnlySet<PhonePlatform> N9 =
        new HashSet<PhonePlatform> { PhonePlatform.MeeGoHarmattan };

    private static readonly IReadOnlySet<PhonePlatform> WindowsPhone =
        new HashSet<PhonePlatform> { PhonePlatform.WindowsPhone, PhonePlatform.Windows10Mobile };

    public static IReadOnlyList<RecoveryFeature> Features { get; } =
    [
        new(
            "device.identity",
            RecoveryArea.Device,
            "Identifier le téléphone",
            "Lire le modèle, le système, la version, le build et le code produit sans modifier l’appareil.",
            new HashSet<PhonePlatform> { PhonePlatform.MeeGoHarmattan, PhonePlatform.WindowsPhone, PhonePlatform.Windows10Mobile },
            RecoveryRisk.ReadOnly,
            RecoveryAvailability.Researching,
            PhoneCapability.ReadIdentity),
        new(
            "n9.firmware",
            RecoveryArea.Firmware,
            "ROM Harmattan PR1.3",
            "Répertorier les variantes, expliquer leurs différences et ne proposer que les ROM compatibles.",
            N9,
            RecoveryRisk.FirmwareChange,
            RecoveryAvailability.Researching,
            PhoneCapability.ReadFirmware),
        new(
            "n9.repositories",
            RecoveryArea.StoresAndApplications,
            "Dépôts et mises à jour",
            "Remplacer les anciens dépôts par des sources fonctionnelles et remettre les applications à jour.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.ManageRepositories),
        new(
            "n9.nokia-store",
            RecoveryArea.StoresAndApplications,
            "Nokia Store d’origine",
            "Rendre de nouveau fonctionnelle l’application Nokia Store installée sur le N9.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages),
        new(
            "n9.alternative-stores",
            RecoveryArea.StoresAndApplications,
            "Boutiques alternatives",
            "Présenter les catalogues compatibles et installer une application choisie en un clic.",
            N9,
            RecoveryRisk.ReversibleChange,
            RecoveryAvailability.Planned,
            PhoneCapability.InstallPackages),
        new(
            "n9.package-backup",
            RecoveryArea.StoresAndApplications,
            "Sauvegarde des applications (.deb)",
            "Reconstruire une sauvegarde hors de MyDocs et distinguer un paquet lisible par Debian d’un paquet réellement restaurable par Aegis.",
            N9,
            RecoveryRisk.ReadOnly,
            RecoveryAvailability.Researching,
            PhoneCapability.ReadApplications),
        new(
            "n9.package-install",
            RecoveryArea.StoresAndApplications,
            "Installer un paquet .deb",
            "Vérifier Package, Version, ARMEL et la provenance Aegis avant toute installation.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages),
        new(
            "n9.internet",
            RecoveryArea.Internet,
            "Navigation Internet",
            "Actualiser les certificats et le chiffrement, en conservant le navigateur d’origine en priorité.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.ManageCertificates),
        new(
            "n9.gps",
            RecoveryArea.Navigation,
            "GPS, Cartes et Drive",
            "Remettre en service la localisation, l’assistance GPS et les applications de navigation Nokia.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.ManageNavigation),
        new(
            "n9.account",
            RecoveryArea.NokiaAccount,
            "Compte Nokia",
            "Désactiver la demande de connexion aux services Nokia disparus.",
            N9,
            RecoveryRisk.ReversibleChange,
            RecoveryAvailability.Planned),
        new(
            "n9.cleanup",
            RecoveryArea.Cleanup,
            "Applications devenues inutilisables",
            "Repérer les applications dépendant de services disparus, les sauvegarder, puis proposer une suppression normale ou forcée en mode expert sans toucher aux paquets essentiels.",
            N9,
            RecoveryRisk.ReversibleChange,
            RecoveryAvailability.Planned,
            PhoneCapability.ReadApplications),
        new(
            "n9.devtools.debugging",
            RecoveryArea.DeveloperTools,
            "Débogage",
            "Installer à la demande les outils de débogage du N9, notamment GDB et GDB Server.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "gdb", "gdbserver" ]),
        new(
            "n9.devtools.networking",
            RecoveryArea.DeveloperTools,
            "Réseau",
            "Installer à la demande les outils de diagnostic et d’analyse réseau disponibles pour Harmattan.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "devtools-networking" ]),
        new(
            "n9.devtools.resources",
            RecoveryArea.DeveloperTools,
            "Analyse des ressources",
            "Mesurer la mémoire utilisée et examiner la répartition des ressources du système.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "devtools-memory" ]),
        new(
            "n9.devtools.power",
            RecoveryArea.DeveloperTools,
            "Analyse de l’énergie",
            "Installer Nokia Energy Profiler et les outils associés d’analyse de la consommation.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "devtools-power-resource" ]),
        new(
            "n9.devtools.performance",
            RecoveryArea.DeveloperTools,
            "Performances",
            "Mesurer les performances, la charge et l’endurance du téléphone.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "devtools-endurance" ]),
        new(
            "n9.devtools.tracing",
            RecoveryArea.DeveloperTools,
            "Traçage",
            "Installer les traceurs système et applicatifs prévus pour Harmattan.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "devtools-tracers" ]),
        new(
            "n9.devtools.test-automation",
            RecoveryArea.DeveloperTools,
            "Automatisation des tests",
            "Installer à la demande les composants d’automatisation et de pilotage des tests.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "qttas" ]),
        new(
            "n9.devtools.utilities",
            RecoveryArea.DeveloperTools,
            "Utilitaires",
            "Ajouter les utilitaires Harmattan proposés avec le mode développeur.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "devtools-utilities", "devtools-x11-utilities" ]),
        new(
            "n9.devtools.logging",
            RecoveryArea.DeveloperTools,
            "Journalisation",
            "Installer les outils de collecte et de consultation des journaux du N9.",
            N9,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages,
            [ "sysklogd" ]),
        new(
            "windows-phone.alternative-stores",
            RecoveryArea.StoresAndApplications,
            "Boutiques alternatives",
            "Embarquer dans ResurectPhone les paquets des boutiques validées et permettre leur installation hors connexion en un clic sur les appareils compatibles.",
            WindowsPhone,
            RecoveryRisk.SystemChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallPackages),
        new(
            "lumia.wpinternals",
            RecoveryArea.WindowsInternals,
            "Windows Internals",
            "Préparer les outils avancés uniquement pour un appareil Windows Phone et un état système compatibles.",
            WindowsPhone,
            RecoveryRisk.FirmwareChange,
            RecoveryAvailability.Planned,
            PhoneCapability.FlashFirmware),
        new(
            "lumia.android",
            RecoveryArea.AlternativeSystem,
            "Android sur Windows Phone",
            "Évaluer le projet Android existant et proposer son installation uniquement sur les appareils Windows Phone compatibles.",
            WindowsPhone,
            RecoveryRisk.FirmwareChange,
            RecoveryAvailability.Researching,
            PhoneCapability.InstallAlternativeSystem)
    ];

    public static IReadOnlyList<RecoveryFeature> ForArea(RecoveryArea area) =>
        Features.Where(feature => feature.Area == area).ToArray();

    public static IReadOnlyList<RecoveryFeature> ForPlatforms(IReadOnlySet<PhonePlatform> platforms) =>
        Features.Where(feature => feature.SupportedPlatforms.Any(platforms.Contains)).ToArray();

    public static IReadOnlyList<RecoveryFeature> ForAreaAndPlatforms(
        RecoveryArea area,
        IReadOnlySet<PhonePlatform> platforms) =>
        Features.Where(feature =>
            feature.Area == area && feature.SupportedPlatforms.Any(platforms.Contains)).ToArray();

    public static IReadOnlyList<RecoveryFeature> CompatibleWith(DetectedPhone phone) =>
        Features.Where(feature => feature.Supports(phone)).ToArray();
}
