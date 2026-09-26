# Audit des fonctions Nokia N9

Essais du 26 septembre 2026 sur le N9 branché au PC : RM-696, code régional
059K114, Harmattan PR1.3 `DFL61_HARMATTAN_40.2012.21-3_PR_005`, noyau
`2.6.32.54-dfl61-20121301`.

## Interface et moteur

Les vingt et une fiches N9 sont reliées à une fenêtre de maintenance. Elles exécutent
un diagnostic réel, affichent ses résultats et n’activent la modification que
si ses prérequis sont réunis. Les opérations passent par la connexion SSH
enregistrée, avec vérification de l’empreinte du téléphone. Le mot de passe
administrateur d’usine est essayé en premier ; un mot de passe personnalisé
n’est demandé qu’en cas de refus.

| Fonction | Implémentation et essai | Limite restante |
| --- | --- | --- |
| Identité | Lecture réelle du modèle, type matériel, code régional, système, build, noyau et architecture | Aucune identité déduite du seul nom USB |
| Firmware/noyau | Diagnostic de compatibilité et recherche documentée | Flashage indisponible : aucune image complète compatible et vérifiée embarquée |
| Dépôts | Cinq index actualisés par USB ; anciennes adresses connues de N9 RepoMirror désactivées avec sauvegarde ; application, restauration et réapplication réussies | Les autres dépôts tiers restent présents et ne sont pas actualisés par ce parcours ; pas de mise à niveau globale |
| Dépendances Nokia | Réinstallation ciblée de `facebookqml=1.3.2+0m8` et `twitter-qml=1.3.50+0m8` ; paquet système conservé ; APT et audit Debian sans erreur | Réparation automatique limitée au build et aux versions Nokia explicitement reconnus ; aucun service Facebook/Twitter recréé |
| Nokia Store | Client installé identifié, dépendance au serveur expliquée | Service distant d’origine non rétabli |
| Boutiques alternatives | Téléchargement vérifié et installation de MeeShop GUI 0.8 réussis | Démarrage et fenêtre contrôlés : message de connexion impossible, le téléphone n’ayant que la route USB. Navigation et installation depuis la boutique à tester avec Internet ; Warehouse/CLI seulement documentés |
| Sauvegarde DEB | Inventaire, copie hors MyDocs, assemblage sur PC et relecture de l’archive : réussis | Une archive reconstruite ne garantit pas la provenance Aegis ; restauration automatique bloquée |
| Installation DEB | Format, identité, architecture et SHA-256 contrôlés avant transfert ; installation d’une application d’essai réussie | Les dépendances et la provenance du paquet restent contrôlées par Harmattan |
| Internet/TLS | Correctif de 16 paquets installé ; date persistante corrigée ; TLS 1.2 et certificat OpenRepos validés par le N9 | Navigateur et applications à relancer et tester ; aucune garantie TLS 1.3 ou compatibilité Web moderne |
| GPS/Cartes/Drive | Modification SUPL et demandes de compte, vérification, restauration puis réapplication : réussies ; les empreintes après restauration correspondent aux originaux | Acquisition GPS en extérieur et navigation avec cartes locales non essayées |
| Compte Nokia | Modification ciblée des réglages Cartes/Drive, comprise dans le cycle précédent | Aucun compte personnel supprimé ; services Nokia distants non recréés |
| Nettoyage | Sauvegarde puis suppression normale de l’application d’essai : réussies, absence finale confirmée | Mode expert conditionné à un blocage de dépendances et une confirmation ; pas de suppression forcée supplémentaire sur une application personnelle |

Les neuf groupes développeur ont été installés ou confirmés déjà installés
sur le téléphone : débogage, réseau, ressources, énergie, performances,
traçage, automatisation des tests, utilitaires et journaux. Chaque parcours
vérifie l’état de tous ses paquets et termine par un contrôle APT. Les
simulations ont prévu uniquement des ajouts, sans suppression ni remplacement
des composants système.

Le paquet `latrace` est fixé à `0.5.11-1~dc115d0+0m6`, disponible dans le
miroir Nokia signé. APT refusait la version plus récente de l’index SDK non
signé. Aucun contrôle d’authenticité n’a été désactivé pour l’installer.

Le verrou conservé par `pkgmgrd` est traité automatiquement : vérifier
l’absence d’opération Nokia, demander l’arrêt normal, attendre au maximum
30 secondes et refuser de poursuivre s’il ne se libère pas. Aucun fichier
verrou n’est supprimé et aucune opération Nokia active n’est interrompue.
La détection utilise `/proc/PID/status`, car `/proc/PID/comm` n’existe pas
sur le noyau 2.6.32 du N9. Ce chemin a été essayé avec le gestionnaire
Nokia effectivement démarré et inactif.

## Contrôles de retour arrière

- Dépôts : application, restauration, comparaison des empreintes des deux
  fichiers avec leurs originaux, puis réapplication. Le fichier historique
  appartenant à `nobody` est écrit sous ce compte, en conservant son propriétaire.
- GPS/Cartes/Drive : application puis restauration, comparaison des empreintes
  de `Maps.conf`, `Drive.ini` et `location-settings.conf`, puis réapplication
  pour laisser Cartes/Drive prêts à essayer.
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
L’interface Windows a confirmé la connexion automatique sans mot de passe et
l’affichage des fiches dans son arbre d’accessibilité. La capture et le
pilotage visuels restent bloqués par l’outil Windows (`window capture timed
out` / `FrameArrived timed out`). Une capture de la fenêtre MeeShop sur le N9
a pu être examinée et montre son message d’absence de connexion au serveur.

## Vérifications du code

- Compilation Release de la solution : aucune erreur, aucun avertissement.
- 72 tests automatisés réussis, comprenant le routage des vingt et une fiches,
  l’inventaire, la protection des paquets essentiels, les erreurs de dépendances,
  le format des archives, le rejet d’architectures incompatibles, les champs
  de contrôle dupliqués, les limites du relais USB, le manifeste de restauration
  TLS, l’exclusion des secrets de session du rapport TLS et les limites de la
  réparation des dépendances : versions inconnues, installations partielles,
  suppressions, ajouts inattendus et changements de version refusés.

## Documents associés

- [Boutiques, dépôts et TLS](boutiques-depots-tls.md)
- [Règles de maintenance DEB et limites Aegis](package-maintenance.md)
- [Recherche de noyaux et appairage USB](kernel-et-usb.md)

Le N9 n’a reçu aucun autre noyau et n’a pas été flashé pendant ces essais.
