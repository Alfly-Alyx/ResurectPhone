using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Discovery;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Core.Recovery;

namespace ResurectPhone.App.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlySet<PhonePlatform> N9Platforms =
        new HashSet<PhonePlatform> { PhonePlatform.MeeGoHarmattan };

    private static readonly IReadOnlySet<PhonePlatform> WindowsPhonePlatforms =
        new HashSet<PhonePlatform> { PhonePlatform.WindowsPhone, PhonePlatform.Windows10Mobile };

    private readonly IPhoneDiscoveryService _discovery;
    private readonly IN9ConnectionService _n9Connection;
    private readonly IN9PairingInteraction _n9PairingInteraction;
    private readonly NavigationSectionViewModel _homeSection;
    private readonly IReadOnlyList<NavigationSectionViewModel> _n9Sections;
    private readonly IReadOnlyList<NavigationSectionViewModel> _windowsPhoneSections;
    private NavigationSectionViewModel _selectedSection;
    private string _activeFamilyTitle = string.Empty;
    private DetectedPhone? _connectedPhone;
    private string _connectionTitle = "Aucun téléphone détecté";
    private string _connectionDetail = "Branchez un Nokia N9 ou un Windows Phone en USB.";
    private bool _isScanning;
    private bool _isConnectingN9;

    public MainWindowViewModel(
        IPhoneDiscoveryService discovery,
        IN9ConnectionService n9Connection,
        IN9PairingInteraction n9PairingInteraction)
    {
        _discovery = discovery;
        _n9Connection = n9Connection;
        _n9PairingInteraction = n9PairingInteraction;
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
            new("windows-phone.stores", "Windows Phone", "\uE719", "Boutiques alternatives", "Paquets de boutiques intégrés au logiciel et installation hors connexion selon la compatibilité.", RecoveryArea.StoresAndApplications, WindowsPhonePlatforms),
            new("windows-phone.internals", "Windows Phone", "\uE8A7", "Windows Internals", "Déverrouillage et opérations avancées sur les modèles compatibles.", RecoveryArea.WindowsInternals, WindowsPhonePlatforms),
            new("windows-phone.android", "Windows Phone", "\uE8D7", "Android", "Projet Android et possibilité d’installation selon l’appareil.", RecoveryArea.AlternativeSystem, WindowsPhonePlatforms)
        ];
        Sections = [_homeSection];
        _selectedSection = _homeSection;
        OpenN9Command = new RelayCommand(() => OpenFamily(_n9Sections));
        OpenWindowsPhoneCommand = new RelayCommand(() => OpenFamily(_windowsPhoneSections));
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        ConnectN9Command = new AsyncRelayCommand(ConnectN9Async, () => !IsConnectingN9);
        ForgetN9PairingCommand = new RelayCommand(ForgetN9Pairing, () => CanForgetN9Pairing);
        RefreshFeatures();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NavigationSectionViewModel> Sections { get; }
    public ObservableCollection<RecoveryFeatureViewModel> SelectedFeatures { get; } = [];
    public RelayCommand OpenN9Command { get; }
    public RelayCommand OpenWindowsPhoneCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ConnectN9Command { get; }
    public RelayCommand ForgetN9PairingCommand { get; }

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
    public bool IsN9Family => ActiveFamilyTitle == "Nokia N9";
    public bool CanForgetN9Pairing => IsN9Family && _n9Connection.HasPairing;
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

    public bool IsConnectingN9
    {
        get => _isConnectingN9;
        private set
        {
            if (SetField(ref _isConnectingN9, value))
            {
                OnPropertyChanged(nameof(N9ConnectionButtonText));
                ConnectN9Command.RaiseCanExecuteChanged();
            }
        }
    }

    public string N9ConnectionButtonText => IsConnectingN9
        ? "Connexion…"
        : _n9Connection.HasPairing ? "Lire le N9" : "Appairer le N9";

    private void OpenFamily(IReadOnlyList<NavigationSectionViewModel> familySections)
    {
        _activeFamilyTitle = familySections[0].FamilyTitle;
        Sections.Clear();
        Sections.Add(_homeSection);
        foreach (var section in familySections)
            Sections.Add(section);

        OnPropertyChanged(nameof(ActiveFamilyTitle));
        OnPropertyChanged(nameof(IsN9Family));
        OnPropertyChanged(nameof(CanForgetN9Pairing));
        OnPropertyChanged(nameof(N9ConnectionButtonText));
        ForgetN9PairingCommand.RaiseCanExecuteChanged();
        SelectSection(familySections[0]);
    }

    private void ShowHome()
    {
        _activeFamilyTitle = string.Empty;
        Sections.Clear();
        Sections.Add(_homeSection);
        OnPropertyChanged(nameof(ActiveFamilyTitle));
        OnPropertyChanged(nameof(IsN9Family));
        OnPropertyChanged(nameof(CanForgetN9Pairing));
        ForgetN9PairingCommand.RaiseCanExecuteChanged();
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
            _connectedPhone = phones.FirstOrDefault(phone =>
                SelectedSection.Platforms.Contains(phone.Platform));
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

    private async Task ConnectN9Async()
    {
        var previousTitle = ConnectionTitle;
        var previousDetail = ConnectionDetail;
        IsConnectingN9 = true;
        try
        {
            N9ConnectionStatus status;
            if (_n9Connection.HasPairing)
            {
                ConnectionTitle = "Connexion au Nokia N9…";
                ConnectionDetail = "Vérification de la liaison développeur et de l’empreinte enregistrée.";
                status = await _n9Connection.GetStatusAsync();
            }
            else
            {
                var password = _n9PairingInteraction.RequestTemporaryPassword();
                if (password is null)
                {
                    ConnectionTitle = previousTitle;
                    ConnectionDetail = previousDetail;
                    return;
                }

                ConnectionTitle = "Appairage du Nokia N9…";
                ConnectionDetail = "Connexion au compte developer et vérification de l’identité du téléphone.";
                status = await _n9Connection.PairAsync(
                    password,
                    _n9PairingInteraction.ConfirmHostKey);
            }

            if (!status.IsReachable || !status.IsHarmattan)
            {
                ConnectionTitle = status.IsPaired ? "Nokia N9 appairé" : "Connexion impossible";
                ConnectionDetail = status.Detail;
                return;
            }

            var details = await _n9Connection.ReadDeviceDetailsAsync();
            var existingId = _connectedPhone?.Platform == PhonePlatform.MeeGoHarmattan
                ? _connectedPhone.DeviceId
                : "n9-sdk-usb";
            _connectedPhone = new DetectedPhone(
                existingId,
                string.IsNullOrWhiteSpace(details.ProductName) ? "Nokia N9" : details.ProductName,
                PhonePlatform.MeeGoHarmattan,
                details.SystemName,
                details.SystemVersion,
                details.SystemBuild,
                details.ProductCode,
                PhoneCapability.ReadIdentity | PhoneCapability.ReadFirmware);
            ConnectionTitle = _connectedPhone.DisplayName;
            ConnectionDetail = DescribeN9(_connectedPhone, details);
        }
        catch (N9ConnectionException exception)
        {
            ConnectionTitle = "Connexion au N9 impossible";
            ConnectionDetail = exception.Message;
        }
        catch (Exception)
        {
            ConnectionTitle = "Connexion au N9 interrompue";
            ConnectionDetail = "ResurectPhone n’a pas pu lire le téléphone. Vérifiez SDK Connectivity et reconnectez le câble USB.";
        }
        finally
        {
            IsConnectingN9 = false;
            OnPropertyChanged(nameof(N9ConnectionButtonText));
            OnPropertyChanged(nameof(CanForgetN9Pairing));
            ForgetN9PairingCommand.RaiseCanExecuteChanged();
            RefreshFeatures();
        }
    }

    private void ForgetN9Pairing()
    {
        if (!_n9PairingInteraction.ConfirmForgetPairing())
            return;

        _n9Connection.ForgetPairing();
        ConnectionTitle = "Liaison N9 oubliée";
        ConnectionDetail = "La clé privée et l’empreinte du téléphone ont été supprimées de ce PC.";
        OnPropertyChanged(nameof(N9ConnectionButtonText));
        OnPropertyChanged(nameof(CanForgetN9Pairing));
        ForgetN9PairingCommand.RaiseCanExecuteChanged();
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

    private static string DescribeN9(DetectedPhone phone, N9DeviceDetails details)
    {
        var values = new[]
        {
            phone.SystemName,
            phone.SystemVersion,
            string.IsNullOrWhiteSpace(phone.BuildNumber) ? null : phone.BuildNumber,
            string.IsNullOrWhiteSpace(phone.ProductCode) ? null : phone.ProductCode,
            string.IsNullOrWhiteSpace(details.KernelVersion) ? null : $"noyau {details.KernelVersion}"
        };
        return string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
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
