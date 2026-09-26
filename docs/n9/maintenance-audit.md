# Audit des fonctions Nokia N9

Essais du 26 septembre 2026 sur le N9 branché au PC : RM-696, code régional
059K114, Harmattan PR1.3 `DFL61_HARMATTAN_40.2012.21-3_PR_005`, noyau
`2.6.32.54-dfl61-20121301`.

## Interface et moteur

Les vingt fiches N9 sont reliées à une fenêtre de maintenance. Elles exécutent
un diagnostic réel, affichent ses résultats et n’activent la modification que
si ses prérequis sont réunis. Les opérations passent par la connexion SSH
enregistrée, avec vérification de l’empreinte du téléphone. Le mot de passe
administrateur d’usine est essayé en premier ; un mot de passe personnalisé
n’est demandé qu’en cas de refus.

| Fonction | Implémentation et essai | Limite restante |
| --- | --- | --- |
| Identité | Lecture réelle du modèle, type matériel, code régional, système, build, noyau et architecture | Aucune identité déduite du seul nom USB |
| Firmware/noyau | Diagnostic de compatibilité et recherche documentée | Flashage indisponible : aucune image complète compatible et vérifiée embarquée |
| Dépôts | Vérification HTTPS des cinq index, sauvegarde du fichier dédié, actualisation APT via relais USB : réussie sur le N9 | Les anciennes sources protégées restent présentes hors de cette actualisation ; pas de mise à niveau globale |
| Nokia Store | Client installé identifié, dépendance au serveur expliquée | Service distant d’origine non rétabli |
| Boutiques alternatives | Téléchargement vérifié et installation de MeeShop GUI 0.8 réussis | Navigation et installation depuis son écran à essayer avec Internet sur le téléphone ; Warehouse/CLI seulement documentés |
| Sauvegarde DEB | Inventaire, copie hors MyDocs, assemblage sur PC et relecture de l’archive : réussis | Une archive reconstruite ne garantit pas la provenance Aegis ; restauration automatique bloquée |
| Installation DEB | Format, identité, architecture et SHA-256 contrôlés avant transfert ; installation d’une application d’essai réussie | Les dépendances et la provenance du paquet restent contrôlées par Harmattan |
| Internet/TLS | Correctif de 16 paquets installé ; date persistante corrigée ; TLS 1.2 et certificat OpenRepos validés par le N9 | Navigateur et applications à relancer et tester ; aucune garantie TLS 1.3 ou compatibilité Web moderne |
| GPS/Cartes/Drive | Modification SUPL et demandes de compte, vérification, puis restauration : réussies ; empreintes des trois fichiers identiques aux originaux | Acquisition GPS en extérieur et navigation avec cartes locales non essayées |
| Compte Nokia | Modification ciblée des réglages Cartes/Drive, comprise dans le cycle précédent | Aucun compte personnel supprimé ; services Nokia distants non recréés |
| Nettoyage | Sauvegarde puis suppression normale de l’application d’essai : réussies, absence finale confirmée | Mode expert conditionné à un blocage de dépendances et une confirmation ; pas de suppression forcée supplémentaire sur une application personnelle |

Les neuf groupes développeur sont implémentés : débogage, réseau, ressources,
énergie, performances, traçage, automatisation des tests, utilitaires et
journaux. Les identifiants des paquets correspondent à l’inventaire du mode
développeur Harmattan. Les neuf simulations ont été exécutées après
actualisation des dépôts. Elles restent bloquées sur ce téléphone par les
dépendances Facebook/Twitter déjà manquantes et, selon le groupe, des
dépendances supplémentaires à installer. Aucune installation réussie de ces
groupes n’est revendiquée. Le rapport conserve le détail d’APT.

## Contrôles de retour arrière

- GPS/Cartes/Drive : application puis restauration, comparaison des empreintes
  de `Maps.conf`, `Drive.ini` et `location-settings.conf`.
- TLS : restauration réelle des treize paquets Nokia, retrait des trois ajouts,
  contrôle de `dpkg --audit`, disparition du marqueur TLS et des chemins SSL
  ajoutés. Les empreintes de QtNetwork et libcurl correspondent aux fichiers
  extraits des paquets Nokia originaux. Le correctif est ensuite réinstallé
  pour laisser le téléphone dans l’état demandé par son propriétaire.
- USB sans mot de passe : préparation, restauration puis réapplication déjà
  testées ; connexion sans mot de passe vérifiée après redémarrage. Un second
  PC physique n’a pas été utilisé pour ce contrôle.

Le cycle d’application d’essai utilise uniquement
`resurectphone-maintenance-smoke`, un paquet jetable créé pour cette vérification.
Il a été supprimé à la fin. Les essais sont effectués par les méthodes publiques
du même service que l’interface WPF ; ils ne constituent pas une validation
visuelle de tous les boutons Windows et de tous les écrans du téléphone.

## Vérifications du code

- Compilation Release de la solution : aucune erreur, aucun avertissement.
- 56 tests automatisés réussis, comprenant le routage des vingt fiches,
  l’inventaire, la protection des paquets essentiels, les erreurs de dépendances,
  le format des archives, le rejet d’architectures incompatibles, les champs
  de contrôle dupliqués, les limites du relais USB, le manifeste de restauration
  TLS et l’exclusion des secrets de session du rapport TLS.

## Documents associés

- [Boutiques, dépôts et TLS](boutiques-depots-tls.md)
- [Règles de maintenance DEB et limites Aegis](package-maintenance.md)
- [Recherche de noyaux et appairage USB](kernel-et-usb.md)

Le N9 n’a reçu aucun autre noyau et n’a pas été flashé pendant ces essais.
