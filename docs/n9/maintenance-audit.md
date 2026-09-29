# Audit des fonctions Nokia N9

Essais du 26 septembre 2026 sur le N9 branché au PC : RM-696, code régional
059K114, Harmattan PR1.3 `DFL61_HARMATTAN_40.2012.21-3_PR_005`, noyau
`2.6.32.54-dfl61-20121301`.

## Interface et moteur

Les vingt et une fiches N9 sont reliées à une fenêtre de maintenance. Elles exécutent
les opérations réelles depuis des boutons dédiés. L’installation des boutiques
et des outils prépare automatiquement les dépôts et les dépendances ; Warehouse
prépare aussi TLS. La fenêtre conserve les retours arrière dans un historique
propre au téléphone, accessible après sa fermeture. Les opérations passent par la connexion SSH
enregistrée, avec vérification de l’empreinte du téléphone. Le mot de passe
administrateur d’usine est essayé en premier ; un mot de passe personnalisé
n’est demandé qu’en cas de refus.

| Fonction | Implémentation et essai | Limite restante |
| --- | --- | --- |
| Identité | Lecture réelle du modèle, type matériel, code régional, système, build, noyau et architecture | Aucune identité déduite du seul nom USB |
| Firmware/noyau | Préparation réelle réussie : archive kernel-plus, image ARM et 99 modules contrôlés, ROM PR1.3 variante 005 vérifiée contre le code régional, sauvegarde de la partition noyau et des modules, transferts contrôlés sur le N9 | Aucun flashage ; le noyau actif reste celui de Nokia. Parcours limité à la variante 005 reconnue |
| Dépôts | Cinq index actualisés par USB ; anciennes adresses connues de N9 RepoMirror désactivées avec sauvegarde ; application, restauration et réapplication réussies | Les autres dépôts tiers restent présents et ne sont pas actualisés par ce parcours ; pas de mise à niveau globale |
| Dépendances Nokia | Réinstallation ciblée de `facebookqml=1.3.2+0m8` et `twitter-qml=1.3.50+0m8` ; paquet système conservé ; APT et audit Debian sans erreur | Réparation automatique limitée au build et aux versions Nokia explicitement reconnus ; aucun service Facebook/Twitter recréé |
| Nokia Store | Action pour installer MeeShop dans la rubrique existante | Service distant Nokia d’origine non rétabli |
| Boutiques alternatives | MeeShop GUI 0.8 et Warehouse 0.1.9 installés. Lancement depuis le moteur réussi ; capture MeeShop avec catalogue en ligne rempli, capture Warehouse sur son écran de profil | Installation d’une application depuis leurs propres écrans non essayée. MeeShop exige hack-installer, déjà présent sur ce N9 |
| Sauvegarde DEB | Conservation du paquet original, sauvegarde, suppression puis réinstallation réelle de l’application de test : réussies, version et inventaire vérifiés. Recherche des applications et bouton de réinstallation dans la fenêtre | Sans paquet original, export reconstruit conservé mais réinstallation automatique bloquée. Les données personnelles ne sont pas dans le paquet original |
| Installation DEB | Format, identité, architecture et SHA-256 contrôlés avant transfert ; installation d’une application d’essai réussie | Les dépendances et la provenance du paquet restent contrôlées par Harmattan |
| Internet/TLS | Correctif de 16 paquets installé ; date persistante corrigée ; TLS 1.2 et certificat OpenRepos validés par le N9. Une installation neuve enchaîne désormais la vérification. Bouton de lancement du navigateur essayé | Navigation sur les sites depuis le navigateur non validée ; aucune garantie TLS 1.3 ou compatibilité Web moderne |
| GPS/Cartes/Drive | Modification SUPL et demandes de compte, vérification, restauration puis réapplication : réussies ; les empreintes après restauration correspondent aux originaux | Lancement de Cartes et Drive réussi ; acquisition GPS en extérieur et navigation avec cartes locales non essayées |
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
Le nouveau cycle inclut la réinstallation du paquet original sauvegardé. Il a été supprimé à la fin. Les essais sont effectués par les méthodes publiques
du même service que l’interface WPF ; ils ne constituent pas une validation
visuelle de tous les boutons Windows et de tous les écrans du téléphone.
L’ancienne interface Windows avait confirmé la connexion automatique et
l’affichage des fiches dans son arbre d’accessibilité. La validation visuelle
de la nouvelle fenêtre reste impossible : l’outil Windows échoue au démarrage
avec `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`.
La capture MeeShop effectuée maintenant sur le N9 montre le catalogue rempli
avec le Wi-Fi connecté. La capture Warehouse montre son profil et sa version.
Les actions de lancement des deux boutiques, de Cartes, Drive et du navigateur
ont confirmé leurs processus sur le N9.

L’historique des réglages a été vérifié avec une nouvelle instance du service :
application GPS, lecture de la sauvegarde persistante, restauration, retrait
de l’entrée restaurée, puis réapplication des réglages demandés.

## Vérifications du code

- Compilation Release de la solution : aucune erreur, aucun avertissement.
- 80 tests automatisés réussis, comprenant le routage des vingt et une fiches,
  l’inventaire, la protection des paquets essentiels, les erreurs de dépendances,
  le format des archives, le rejet d’architectures incompatibles, les champs
  de contrôle dupliqués, les limites du relais USB, le manifeste de restauration
  TLS, l’exclusion des secrets de session du rapport TLS et les limites de la
  réparation des dépendances : versions inconnues, installations partielles,
  suppressions, ajouts inattendus et changements de version refusés. Les nouveaux
  tests couvrent la correspondance ROM/code produit, les images ARM tronquées,
  l’annulation et l’effacement du mot de passe, l’historique par téléphone,
  les chemins invalides, le cache du paquet original et sa falsification.

## Documents associés

- [Boutiques, dépôts et TLS](boutiques-depots-tls.md)
- [Règles de maintenance DEB et limites Aegis](package-maintenance.md)
- [Recherche de noyaux et appairage USB](kernel-et-usb.md)

Le N9 n’a reçu aucun autre noyau et n’a pas été flashé pendant ces essais.

## Ajout réseau du 29 septembre 2026

Une vingt-deuxième fiche, **Réseaux et SDK**, permet les réglages des profils Wi-Fi, la reconnexion forcée autonome et l’accès SDK sans mot de passe en USB ou USB et Wi-Fi. La connexion SSH par Wi-Fi est intégrée. Voir [les commandes, l’architecture et les essais restant sur le téléphone](reseaux-et-sdk.md).

Les 93 tests .NET et 6 tests Python passent. L’état réseau a été lu sur le N9 ; les modifications n’ont pas encore été appliquées, car sa liaison Wi-Fi est devenue indisponible avant les essais et aucun USB N9 n’était présent.
