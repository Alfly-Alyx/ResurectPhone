using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public async Task<N9MaintenanceReport> DiagnoseAsync(string featureId, CancellationToken cancellationToken = default)
    {
        if (!N9MaintenanceCatalog.FeatureIds.Contains(featureId)) throw new ArgumentException("Fonction N9 inconnue.", nameof(featureId));
        if (featureId == "n9.internet")
        {
            var installed = await InspectTlsAsync(cancellationToken);
            if (installed.BackupPath is not null) return installed;
        }
        if (featureId is "device.identity" or "n9.firmware")
        {
            var details = await ReadDeviceDetailsAsync(cancellationToken);
            var text = $"Modèle : {details.ProductName}\nType matériel : {details.ProductCode}\nCode produit régional : {details.SalesCode}\nSystème : {details.SystemName} {details.SystemVersion}\nBuild : {details.SystemBuild}\nNoyau : {details.KernelVersion}\nArchitecture : {details.Architecture}";
            if (featureId == "n9.firmware")
                text += "\n\nKernel-plus 2.6.32.61 (28 novembre 2013) : image ARM et 99 modules disponibles.\n" +
                    "La préparation télécharge la ROM de récupération correspondant à PR1.3 variante 005 et au code produit Nokia, sauvegarde le noyau et ses modules, puis copie kernel-plus dans MyDocs/ResurectPhone/Kernels.\n" +
                    "L’installation et le redémarrage avec ce noyau restent à valider.\n" +
                    "Source de l’archive : https://archive.org/details/n9-drivers-fw\nSources du noyau : https://github.com/harmattan/kernel-plus-harmattan";
            return new(featureId == "device.identity" ? "Identité lue sur le N9" : "Compatibilité firmware examinée", text);
        }
        if (featureId is "n9.package-backup" or "n9.cleanup")
        {
            var applications = await ReadApplicationsAsync(cancellationToken);
            return new("Inventaire prêt", $"{applications.Count} applications visibles identifiées sur le N9. Choisissez une application.\n" +
                "Les paquets essentiels, requis, importants et le compagnon ResurectPhone sont protégés. Les sauvegardes reconstruites ne sont pas automatiquement restaurables par Aegis.");
        }
        if (featureId == "n9.package-install")
            return new("Installation locale", "Choisissez un fichier .deb. Son identité, son architecture, son format et son empreinte seront vérifiés avant transfert.");
        if (featureId == "n9.repositories")
        {
            var probes = await N9OnlineResources.ProbeRepositoriesAsync(cancellationToken);
            return await WithMaintenanceClientAsync(async (client, _) =>
            {
                var sources = await RunMaintenanceScriptAsync(client,
                    "for f in /etc/apt/sources.list /etc/apt/sources.list.d/*.list; do test -f \"$f\" && grep -hE \"^[[:space:]]*deb[[:space:]]\" \"$f\"; done\nexit 0", null, cancellationToken);
                var redacted = Regex.Replace(sources.Output, @"://[^/\s@]+@", "://[identifiants]@");
                return new N9MaintenanceReport(probes.All(probe => probe.Available) ? "Miroirs Harmattan et SDK accessibles" : "Miroirs incomplets ou indisponibles",
                    "Sources du téléphone :\n" + redacted + "\nContrôle depuis le PC :\n" + string.Join('\n', probes.Select(probe => probe.Detail)) +
                    "\nLes téléchargements passent par le PC et le câble USB, avec contrôle HTTPS. Les sources ResurectPhone sont sauvegardées avant modification. Les anciennes adresses connues de N9 RepoMirror sont désactivées avec sauvegarde. Les autres dépôts sont conservés et exclus de cette actualisation. Aucun paquet système n’est mis à niveau automatiquement.",
                    probes.All(probe => probe.Available), "Réparer les dépôts et actualiser");
            }, cancellationToken);
        }
        if (featureId == "n9.alternative-stores")
            return await WithMaintenanceClientAsync(async (client, _) =>
            {
                var dependency = await RunMaintenanceScriptAsync(client, "command -v aegis-dpkg >/dev/null && test \"$(dpkg-query -W -f='${Status}' hack-installer 2>/dev/null)\" = 'install ok installed'", null, cancellationToken);
                return new N9MaintenanceReport("MeeShop GUI 0.8 — juillet 2026",
                    "Boutique OpenRepos : navigation, installation, suppression et mise à jour des applications. Son auteur annonce un fonctionnement sans correctif TLS système. La recherche OpenRepos reste incomplète.\n" +
                    "Le paquet officiel est téléchargé par le PC et son empreinte vérifiée avant installation. MeeShop GUI remplace l’ancien MeeShop CLI (même identifiant de paquet).\n" +
                    (dependency.Status == 0 ? "La dépendance hack-installer est présente.\n" : "La dépendance hack-installer manque ; installez son paquet officiel avant MeeShop GUI.\n") +
                    "Warehouse 0.1.9 est également proposé dans la liste. Son installation prépare le correctif TLS 1.2 automatiquement. MeeShop CLI 0.2.0 (2023) est archivé et remplace la GUI.\n" +
                    "Source : https://openrepos.net/content/iarchep/meeshop-gui", dependency.Status == 0, "Installer MeeShop GUI");
            }, cancellationToken);
        return await WithMaintenanceClientAsync(async (client, _) =>
        {
            if (featureId == "n9.dependencies") return await DiagnoseDependenciesAsync(client, cancellationToken);
            if (N9MaintenanceCatalog.DeveloperPackages.TryGetValue(featureId, out var packages))
            {
                var joined = string.Join(' ', packages.Select(Quote));
                var result = await RunMaintenanceScriptAsync(client, "apt-cache policy " + string.Join(' ', packages.Select(package => Quote(package.Split('=')[0]))) + "\napt-get -s" + AptInstallOptions + " install " + joined, null, cancellationToken);
                var applicable = result.Status == 0 && ParseAptRemovals(result.Output).Count == 0;
                return new N9MaintenanceReport(applicable ? "Installation simulée" :
                    result.Output.Contains("Unmet dependencies", StringComparison.Ordinal) ? "Dépendances à résoudre" : "Paquets ou dépôts manquants",
                    "Paquets Harmattan : " + string.Join(", ", packages) + "\n\n" + LimitOutput(result.Output), applicable, "Installer ces outils");
            }
            var script = featureId switch
            {
                "n9.internet" => "printf 'Heure du N9 : '; date -u\nprintf 'Secondes Unix : '; date +%s\nprintf '\\nCertificats :\\n'; ls /etc/ssl/certs 2>/dev/null | wc -l\nprintf '\\nNavigateurs :\\n'; dpkg-query -W -f='${Package} ${Version}\\n' browser meegotouch-browser libssl0.9.8 libssl1.0.0 2>/dev/null\ncommand -v acmcli\nexit 0",
                "n9.gps" => "printf 'Assistance GPS :\\n'; grep -E '^(PrimarySuplServer|SecondarySuplServer|SuplForced)=' /etc/xdg/nokia/location-settings.conf\nprintf '\\nApplications :\\n'; dpkg-query -W -f='${Package} ${Version}\\n' maps nokia-drive-qml nokia-drive-enabler 2>/dev/null\nprintf '\\nCartes hors ligne :\\n'; du -sk /home/user/MyDocs/cities /home/user/MyDocs/.maps 2>/dev/null\nexit 0",
                "n9.account" => "for file in /home/user/.config/Nokia/Maps.conf /home/user/.config/Nokia/Drive.ini; do printf '%s\\n' \"$file\"; if test -f \"$file\"; then grep -E '^[[:space:]]*(isSsoEnabled|ssoDone)[[:space:]]*=' \"$file\"; else echo absent; fi; done\nexit 0",
                "n9.nokia-store" => "dpkg-query -W -f='${Package} ${Version} ${Status}\\n' ovistoreclient ovi-store-adapter 2>/dev/null\nexit 0",
                _ => throw new ArgumentException("Fonction inconnue.")
            };
            var diagnosis = await RunMaintenanceScriptAsync(client, script, null, cancellationToken);
            RequireSuccess(diagnosis, "Diagnostic impossible.");
            return featureId switch
            {
                "n9.internet" => new("TLS 1.2 pour Harmattan PR1.3", LimitOutput(diagnosis.Output) + "\n\nHeure UTC du PC : " + DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture) +
                    "\nLe correctif WunderWungiel 0.0.2 ajoute TLS 1.2 à Qt et libcurl, et actualise les certificats. Les 16 paquets du correctif et les paquets Nokia de retour arrière sont téléchargés et vérifiés sur le PC avant installation.\n" +
                    "Les versions installées et l’espace libre sont contrôlés. Une installation SSL communautaire préexistante bloque l’opération. Les applications doivent être relancées après installation. OpenSSL 1.0.2u reste ancien ; les sites exigeant TLS 1.3 ou un navigateur récent peuvent rester incompatibles.\n" +
                    "Source : https://wunderwungiel.pl/MeeGo/content/?id=4", true, "Installer TLS 1.2 et les certificats"),
                "n9.gps" => new("Diagnostic GPS et cartes", LimitOutput(diagnosis.Output) +
                    "\nLe correctif sauvegarde les réglages, remplace supl.nokia.com par supl.google.com et supprime les demandes de compte de Cartes/Drive. Un test GPS en extérieur et la présence de cartes locales restent nécessaires.", true, "Préparer GPS, Cartes et Drive"),
                "n9.account" => new("Demandes de compte Nokia", LimitOutput(diagnosis.Output) +
                    "\nLe correctif concerne les demandes de connexion dans Cartes et Drive. Il ne supprime aucun compte personnel et ne recrée pas les services Nokia distants.", true, "Désactiver la demande dans Cartes/Drive"),
                _ => new("Nokia Store d’origine : service distant requis", LimitOutput(diagnosis.Output) +
                    "\nLe client installé ne suffit pas à rétablir la boutique Nokia. Aucun serveur compatible et vérifié n’est configuré. La remise en service de cette boutique reste bloquée. La fiche Boutiques alternatives permet d’essayer MeeShop.")
            };
        }, cancellationToken);
    }

    private async Task<N9MaintenanceReport> ApplyCoreAsync(string featureId, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (featureId == "n9.internet")
            {
                var existing = await InspectTlsAsync(cancellationToken);
                if (existing.BackupPath is not null)
                    return await VerifyTlsAsync(existing.BackupPath, administratorPassword, cancellationToken);
                var installed = await InstallTlsAsync(administratorPassword?.ToArray(), cancellationToken);
                try
                {
                    var verified = await VerifyTlsAsync(installed.BackupPath!, administratorPassword, cancellationToken);
                    return verified with { Detail = installed.Detail + "\n\n" + verified.Detail };
                }
                catch (Exception error) when (error is not (N9MaintenanceFailureException or N9AdministratorRequiredException))
                {
                    throw new N9MaintenanceFailureException("Correctif installé, mais vérification interrompue : " + error.Message, installed.BackupPath!);
                }
            }
            if (featureId == "n9.alternative-stores")
            {
                var download = await N9OnlineResources.DownloadMeeShopAsync(cancellationToken);
                var check = await DiagnoseAsync(featureId, cancellationToken);
                if (!check.CanApply) throw new N9ConnectionException(check.Detail);
                return await InstallLocalPackageAsync(await InspectLocalPackageAsync(download, cancellationToken), administratorPassword, cancellationToken);
            }
            var sources = featureId == "n9.repositories" ? await N9OnlineResources.ProbeRepositoriesAsync(cancellationToken) : [];
            if (featureId == "n9.repositories" && !sources.All(source => source.Available))
                throw new N9ConnectionException("Aucun miroir Harmattan vérifié n’est actuellement accessible. Les dépôts du téléphone n’ont pas été modifiés.");
            return await WithMaintenanceClientAsync((client, _) => WithAdministratorAsync(client, administratorPassword, async password =>
            {
                if (featureId == "n9.dependencies") return await RepairDependenciesAsync(client, password, cancellationToken);
                if (N9MaintenanceCatalog.DeveloperPackages.TryGetValue(featureId, out var packages))
                {
                    await using var relay = new N9RepositoryRelay(client);
                    var joined = string.Join(' ', packages.Select(Quote));
                    var preview = await RunMaintenanceScriptAsync(client, "apt-get -s" + AptInstallOptions + " install " + joined, null, cancellationToken);
                    RequireSuccess(preview, "Des paquets développeur ou leurs dépendances sont absents des dépôts.");
                    if (ParseAptRemovals(preview.Output).Count > 0) throw new N9ConnectionException("L’installation supprimerait d’autres paquets.");
                    await ReleasePackageManagerAsync(client, password, cancellationToken);
                    var install = await RunMaintenanceScriptAsync(client, "set -e\nexport DEBIAN_FRONTEND=noninteractive\napt-get -y" + AptInstallOptions + " install " + joined +
                        relay.AptOptions + "\nfor spec in " + joined + "; do package=${spec%%=*}; test \"$(dpkg-query -W -f='${Status}' \"$package\")\" = 'install ok installed'; " +
                        "case \"$spec\" in *=*) test \"$(dpkg-query -W -f='${Version}' \"$package\")\" = \"${spec#*=}\";; esac; done\napt-get check", password, cancellationToken, TimeSpan.FromMinutes(10));
                    RequireSuccess(install, "Installation interrompue ; consultez le rapport APT.");
                    return new N9MaintenanceReport("Outils installés et vérifiés", string.Join(", ", packages) + "\n" + LimitOutput(install.Output));
                }
                if (featureId == "n9.repositories") await ReleasePackageManagerAsync(client, password, cancellationToken);
                var backup = "/var/lib/resurectphone/maintenance/" + Guid.NewGuid().ToString("N");
                await using var repositoryRelay = featureId == "n9.repositories" ? new N9RepositoryRelay(client) : null;
                var script = featureId switch
                {
                    "n9.gps" => BuildSettingsScript(backup, true),
                    "n9.account" => BuildSettingsScript(backup, false),
                    "n9.repositories" => BuildRepositoryScript(backup, sources.Select(source => source.SourceLine).Distinct(), repositoryRelay!.AptOptions),
                    _ => throw new InvalidOperationException("Cette fonction propose uniquement un diagnostic ; aucune modification compatible n’est disponible.")
                };
                var result = await RunMaintenanceScriptAsync(client, script, password, cancellationToken);
                if (result.Status != 0)
                    throw new N9MaintenanceFailureException("L’opération a été refusée ou interrompue. Sauvegarde éventuelle : " + backup + "\n" + LimitOutput(result.Output), backup);
                return new N9MaintenanceReport("Opération terminée", LimitOutput(result.Output) + "\nSauvegarde : " + backup +
                    (featureId == "n9.gps" ? "\nFermez puis relancez Cartes/Drive et testez la localisation en extérieur." : ""), BackupPath: backup);
            }, cancellationToken), cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }

    public async Task<N9MaintenanceReport> RestoreSettingsAsync(string backupPath, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Regex.IsMatch(backupPath, @"^/var/lib/resurectphone/maintenance/[a-f0-9]{32}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Dossier de sauvegarde ResurectPhone invalide.");
            return await WithMaintenanceClientAsync((client, pairing) => WithAdministratorAsync(client, administratorPassword, async password =>
            {
                var restore = "if test -f " + backupPath + "/packages-before; then\n" + ReleaseIdlePackageManager + "\nfi\nsh " + backupPath + "/restore.sh";
                var result = await RunMaintenanceScriptAsync(client, restore, password, cancellationToken, TimeSpan.FromMinutes(10));
                RequireSuccess(result, "La restauration a échoué.");
                try { await new N9SettingsHistory(pairing.HostKeySha256).RemoveAsync(backupPath, CancellationToken.None); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { return new N9MaintenanceReport("Réglages restaurés", LimitOutput(result.Output) + "\nL’historique local n’a pas pu être actualisé : " + error.Message); }
                return new N9MaintenanceReport("Réglages restaurés", LimitOutput(result.Output));
            }, cancellationToken), cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }
}
