# ResurectPhone

ResurectPhone est un laboratoire autonome de remise en service de téléphones. Il est développé séparément d’AndroLink, avec une interface compatible avec ses codes visuels pour permettre une intégration ultérieure.

## Premier socle

- détection générique d’un téléphone connecté, sans inventer son identité ;
- détection et appairage automatiques du Nokia N9 par le mode USB de SDK Connectivity ; préparation par SSH avec essai automatique du mot de passe administrateur d’origine, puis accès sans saisie pour les prochains PC ;
- lecture réelle et sans modification du modèle, d’Harmattan/PR1.3, du code produit et du noyau ;
- empreinte SSH épinglée et clé privée d’appairage protégée par le compte Windows ;
- maintenance N9 intégrée : inventaire des applications, sauvegarde du paquet original et réinstallation, export DEB reconstruit, installation et suppression avec contrôle des dépendances ;
- réparation des index Harmattan/SDK via le PC et le câble USB, installation de MeeShop GUI et Warehouse avec préparation automatique des prérequis ;
- installation contrôlée du correctif TLS 1.2, synchronisation de l’heure et vérification du certificat depuis le N9 ;
- réglages Cartes/Drive et assistance GPS avec historique des sauvegardes et retour arrière accessible après fermeture ;
- boutons pour lancer les boutiques, Cartes, Drive et le navigateur sur le N9 ;
- préparation de kernel-plus : téléchargement vérifié, ROM de récupération PR1.3 variante 005, sauvegarde du noyau Nokia et transfert dans un dossier dédié ;
- espace Android distinct, détection ADB explicite et lecture de l’identité du téléphone ;
- gestionnaire des tâches Android en temps réel : processus, PID, utilisateur, état, CPU, mémoire, lectures/écritures, threads et commande ;
- filtre et tri des processus, avec maintien à l’écran des lignes dont Android masque certains compteurs ;
- libération prudente de la mémoire vive, après confirmation, limitée aux applications d’arrière-plan autorisées par Android et suivie d’une mesure avant/après ;
- catalogue indépendant des fonctions de restauration ;
- séparation entre interface, règles métier et accès Windows ;
- préparation USB du N9 avec sauvegarde et restauration ; aucune opération de flashage dans ce premier socle ;
- aucune dépendance directe vers les projets AndroLink.

## Périmètre prévu

- Nokia N9 : ROM Harmattan PR1.3, dépôts, applications, Nokia Store, compte Nokia, navigation Internet, certificats, GPS, Cartes et Drive ;
- Windows Phone : diagnostic, Windows Internals, installation expérimentale des projets Android compatibles et paquets de boutiques alternatives installables hors connexion ;
- Android : diagnostic ADB, suivi des processus et entretien de la mémoire sans accès root ;
- ressources de réparation disponibles hors connexion lorsque leur redistribution est autorisée ;
- vérification en ligne après chaque réparation qui dépend d’un service Internet.

Les opérations sensibles devront toujours vérifier l’appareil, la compatibilité, l’intégrité des fichiers et la présence d’une sauvegarde avant de devenir accessibles.

Le gestionnaire Android recherche ADB dans une installation existante de Platform Tools et dans le futur dossier embarqué `tools/platform-tools`. Aucun binaire Google n’est actuellement redistribué dans le dépôt. La quantité de détails disponible dépend des restrictions de la version d’Android et du constructeur.

La procédure et les limites Aegis des paquets Harmattan sont détaillées dans
[`docs/n9/package-maintenance.md`](docs/n9/package-maintenance.md).

La recherche sur les noyaux du N9 et la préparation de l’appairage USB
automatique sont détaillées dans
[`docs/n9/kernel-et-usb.md`](docs/n9/kernel-et-usb.md).

L’état de chaque fonction N9 et les essais matériels sont consignés dans
[`docs/n9/maintenance-audit.md`](docs/n9/maintenance-audit.md). Les boutiques,
miroirs et limites TLS sont décrits dans
[`docs/n9/boutiques-depots-tls.md`](docs/n9/boutiques-depots-tls.md).
