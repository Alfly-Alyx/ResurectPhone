using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Discovery;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.App.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlySet<PhonePlatform> N9Platforms =
        new HashSet<PhonePlatform> { PhonePlatform.MeeGoHarmattan };

    private static readonly IReadOnlySet<PhonePlatform> WindowsPhonePlatforms =
        new HashSet<PhonePlatform> { PhonePlatform.WindowsPhone, PhonePlatform.Windows10Mobile };

    private readonly IPhoneDiscoveryService _discovery;
    private readonly NavigationSectionViewModel _homeSection;
    private readonly IReadOnlyList<NavigationSectionViewModel> _n9Sections;
    private readonly IReadOnlyList<NavigationSectionViewModel> _windowsPhoneSections;
    private NavigationSectionViewModel _selectedSection;
    private string _activeFamilyTitle = string.Empty;
    private DetectedPhone? _connectedPhone;
    private string _connectionTitle = "Aucun téléphone détecté";
    private string _connectionDetail = "Branchez un Nokia N9 ou un Windows Phone en USB.";
    private bool _isScanning;

    public MainWindowViewModel(IPhoneDiscoveryService discovery)
    {
        _discovery = discovery;
        _homeSection = new(
            "home", string.Empty, "\uE80F", "Accueil",
            "Choisissez le téléphone à remettre en service.", null,
            new HashSet<PhonePlatform>(), true);
        _n9Sections =
        [
            new("n9.home", "Nokia N9", "\uE80F", "Nokia N9", "Toutes les solutions prévues pour remettre un Nokia N9 en service.", null, N9Platforms),
            new("n9.device", "Nokia N9", "\uE946", "Appareil", "Identité, version d’Harmattan et compatibilité.", RecoveryArea.Device, N9Platforms),
            new("n9.developer-tools", "Nokia N9", "\uE943", "Outils développeur", "Les neuf ensembles d’outils installables à la demande.", RecoveryArea.DeveloperTools, N9Platforms),
            new("n9.firmware", "Nokia N9", "\uE950", "ROM et système", "ROM Harmattan PR1.3 et opérations système.", RecoveryArea.Firmware, N9Platforms),
            new("n9.stores", "Nokia N9", "\uE719", "Boutiques et applications", "Nokia Store, boutiques alternatives et mises à jour.", RecoveryArea.StoresAndApplications, N9Platforms),
            new("n9.internet", "Nokia N9", "\uE774", "Internet", "Certificats, chiffrement et navigation Web.", RecoveryArea.Internet, N9Platforms),
            new("n9.navigation", "Nokia N9", "\uE707", "GPS et cartes", "Localisation, Cartes et Drive.", RecoveryArea.Navigation, N9Platforms),
            new("n9.account", "Nokia N9", "\uE77B", "Compte Nokia", "Services de compte devenus indisponibles.", RecoveryArea.NokiaAccount, N9Platforms),
            new("n9.cleanup", "Nokia N9", "\uE74D", "Nettoyage", "Applications dépendant de services disparus.", RecoveryArea.Cleanup, N9Platforms)
        ];
        _windowsPhoneSections =
        [
            new("windows-phone.home", "Windows Phone", "\uE80F", "Windows Phone", "Solutions avancées prévues pour les appareils Windows Phone compatibles.", null, WindowsPhonePlatforms),
            new("windows-phone.device", "Windows Phone", "\uE946", "Appareil", "Modèle, version de Windows Phone et compatibilité.", RecoveryArea.Device, WindowsPhonePlatforms),
            new("windows-phone.internals", "Windows Phone", "\uE8A7", "Windows Internals", "Déverrouillage et opérations avancées sur les modèles compatibles.", RecoveryArea.WindowsInternals, WindowsPhonePlatforms),
            new("windows-phone.android", "Windows Phone", "\uE8D7", "Android", "Projet Android et possibilité d’installation selon l’appareil.", RecoveryArea.AlternativeSystem, WindowsPhonePlatforms)
        ];
        Sections = [_homeSection];
        _selectedSection = _homeSection;
        OpenN9Command = new RelayCommand(() => OpenFamily(_n9Sections));
        OpenWindowsPhoneCommand = new RelayCommand(() => OpenFamily(_windowsPhoneSections));
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        RefreshFeatures();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NavigationSectionViewModel> Sections { get; }
    public ObservableCollection<RecoveryFeatureViewModel> SelectedFeatures { get; } = [];
    public RelayCommand OpenN9Command { get; }
    public RelayCommand OpenWindowsPhoneCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }

    public NavigationSectionViewModel SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is null || ReferenceEquals(_selectedSection, value))
                return;

            if (value.IsHome)
                ShowHome();
            else
                SelectSection(value);
        }
    }

    public bool IsHome => SelectedSection.IsHome;
    public bool IsFamilyView => !IsHome;
    public string ActiveFamilyTitle => _activeFamilyTitle;
    public string PageTitle => SelectedSection.Title;
    public string PageSubtitle => SelectedSection.Subtitle;
    public string SelectedFamilyTitle => SelectedSection.FamilyTitle;
    public string FeaturesTitle => $"Fonctions pour {SelectedSection.FamilyTitle}";
    public string ConnectionTitle
    {
        get => _connectionTitle;
        private set => SetField(ref _connectionTitle, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetField(ref _connectionDetail, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetField(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
                ScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ScanButtonText => IsScanning ? "Recherche…" : "Rechercher";

    private void OpenFamily(IReadOnlyList<NavigationSectionViewModel> familySections)
    {
        _activeFamilyTitle = familySections[0].FamilyTitle;
        Sections.Clear();
        Sections.Add(_homeSection);
        foreach (var section in familySections)
            Sections.Add(section);

        OnPropertyChanged(nameof(ActiveFamilyTitle));
        SelectSection(familySections[0]);
    }

    private void ShowHome()
    {
        _activeFamilyTitle = string.Empty;
        Sections.Clear();
        Sections.Add(_homeSection);
        OnPropertyChanged(nameof(ActiveFamilyTitle));
        SelectSection(_homeSection);
    }

    private void SelectSection(NavigationSectionViewModel section)
    {
        _selectedSection = section;
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsFamilyView));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(SelectedFamilyTitle));
        OnPropertyChanged(nameof(FeaturesTitle));
        RefreshFeatures();
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        ConnectionTitle = "Recherche du téléphone…";
        ConnectionDetail = "ResurectPhone consulte les appareils reconnus par Windows.";
        try
        {
            var phones = await _discovery.DiscoverAsync();
            _connectedPhone = phones.FirstOrDefault();
            if (_connectedPhone is null)
            {
                ConnectionTitle = "Aucun téléphone détecté";
                ConnectionDetail = "Branchez un Nokia N9 ou un Windows Phone en USB, puis réessayez.";
            }
            else
            {
                ConnectionTitle = _connectedPhone.DisplayName;
                ConnectionDetail = Describe(_connectedPhone);
            }
        }
        catch (Exception)
        {
            _connectedPhone = null;
            ConnectionTitle = "Recherche impossible";
            ConnectionDetail = "Windows n’a pas permis de consulter les appareils. Réessayez après avoir reconnecté le téléphone.";
        }
        finally
        {
            IsScanning = false;
            RefreshFeatures();
        }
    }

    private void RefreshFeatures()
    {
        SelectedFeatures.Clear();
        if (IsHome)
            return;

        var features = SelectedSection.Area is { } area
            ? RecoveryCatalog.ForAreaAndPlatforms(area, SelectedSection.Platforms)
            : RecoveryCatalog.ForPlatforms(SelectedSection.Platforms);
        foreach (var feature in features)
            SelectedFeatures.Add(new RecoveryFeatureViewModel(feature, _connectedPhone));
    }

    private static string Describe(DetectedPhone phone)
    {
        var details = new[]
        {
            phone.SystemName,
            phone.SystemVersion,
            string.IsNullOrWhiteSpace(phone.BuildNumber) ? null : $"build {phone.BuildNumber}"
        };
        var description = string.Join(" · ", details.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(description)
            ? "Téléphone reconnu. L’identification détaillée reste à effectuer."
            : description;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
