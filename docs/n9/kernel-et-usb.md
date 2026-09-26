# Nokia N9 : noyau et appairage USB

État du N9 et dépôts GitHub vérifiés le 26 septembre 2026. Le téléphone est un
N9 RM-696 sous `DFL61_HARMATTAN_40.2012.21-3_PR_005`, avec le noyau
`2.6.32.54-dfl61-20121301` (ARMv7), relevés par SSH. L’accès USB sans mot de
passe et la préparation depuis le moteur ResurectPhone ont été testés sur ce
téléphone. Aucun noyau n’a été installé.

## Résultat de la recherche du noyau

- La base Harmattan PR1.3 du N9 utilise la famille de noyaux `2.6.32.54`.
  [Documentation Ubiboot de maemo.org](https://wiki.maemo.org/Ubiboot).
- [caio2k/kernel-n9](https://github.com/caio2k/kernel-n9) contient une base
  Nokia `2.6.32.39`, antérieure à la base PR1.3. Son dernier commit date du
  3 mars 2017. La seule ressource jointe à une publication GitHub est
  `linux_2.6.32.39.tar.bz2`, une archive de sources ; elle ne démontre pas la
  disponibilité d’un noyau plus récent ou d’une image à installer.
- [nemomobile/kernel-adaptation-n950-n9](https://github.com/nemomobile/kernel-adaptation-n950-n9)
  a une branche principale `3.5.3` adaptée à Nemo/Mer, dont le dernier commit
  date du 21 janvier 2016. Ses publications GitHub ne contiennent pas de
  binaire joint. La branche historique
  [`mer-n9-2.6.32-20121301`](https://github.com/nemomobile/kernel-adaptation-n950-n9/tree/mer-n9-2.6.32-20121301)
  est en `2.6.32.54` ; elle ne constitue pas une mise à jour de la base PR1.3.
  La compatibilité de la branche `3.5.3` avec Harmattan PR1.3 n’a pas été
  validée dans ResurectPhone. Le seul nom du dépôt ne permet pas de conclure
  à la compatibilité ou à l’incompatibilité de toutes ses branches.
- [harmattan/kernel-plus-harmattan](https://github.com/harmattan/kernel-plus-harmattan/tree/harmattan-2632)
  contient une branche Harmattan `2.6.32.61`, avec notamment un changement
  intitulé « defconfig update: kernel-plus 2.6.32.61 ». Son dernier commit
  date du 19 novembre 2013. C’est une source communautaire plus récente que
  `2.6.32.54` dans la même série, mais le dépôt ne publie aucune release GitHub
  avec image installable.
- Le fork [hurrian/kernel-plus-harmattan](https://github.com/hurrian/kernel-plus-harmattan/tree/kernel-plus-r7)
  prolonge cette piste avec une configuration explicite
  [`rm696_plus_defconfig`](https://github.com/hurrian/kernel-plus-harmattan/blob/kernel-plus-r7/arch/arm/configs/rm696_plus_defconfig)
  pour le N9 RM-696. Il reste en `2.6.32.61` ; son dernier commit date du
  1er avril 2014 et il n’a pas de release GitHub. Une archive communautaire
  `linux_2.6.32.61-plus-20131128.tar.gz` est décrite dans les anciennes
  [instructions Ubiboot](https://talk.maemo.org/archive/index.php/t-89345-p-5.html).
  Cette archive antérieure au fork `r7` n’a été ni récupérée, ni vérifiée, ni
  testée sur le N9 connecté.
- L’ancien noyau N9 de postmarketOS est `4.17-rc4`, dans
  [`device/archived/linux-nokia-n9`](https://github.com/external-mirrors/pmaports/blob/main/device/archived/linux-nokia-n9/APKBUILD).
  Le paquet appareil N9 est lui aussi dans
  [`device/archived/device-nokia-n9`](https://github.com/external-mirrors/pmaports/tree/main/device/archived/device-nokia-n9).
  Les ports archivés ne sont pas distribués comme des mises à jour normales
  selon la [politique de postmarketOS](https://docs.postmarketos.org/pmaports/main/device-categorization.html).
- Le noyau OMAP `7.1.5` du
  [paquet communautaire actuel](https://github.com/external-mirrors/pmaports/blob/main/device/community/linux-postmarketos-omap/APKBUILD)
  vise `armv7` et plusieurs appareils OMAP. Aucun paquet N9 actif n’est associé
  à cette version dans l’arbre actuel. Ce numéro ne signifie donc pas que
  `7.1.5` peut remplacer le noyau de Harmattan sur ce N9.
- La branche Linux générique `2.6.32` a continué jusqu’à
  [`2.6.32.71` en mars 2016](https://www.kernel.org/pub/linux/kernel/v2.6/longterm/v2.6.32/).
  Cette publication amont ne contient pas, à elle seule, les adaptations
  Harmattan et RM-696 nécessaires à une mise à jour du N9.
- [Nitdroid-Reborn/kernel-n9](https://github.com/Nitdroid-Reborn/kernel-n9)
  contient un noyau `2.6.35.9` pour un port Android, pas pour Harmattan.
  Le dépôt a été poussé en 2021, mais les derniers commits de sa branche
  principale sont datés de 2011 : la date de dépôt n’indique pas un noyau N9
  récemment développé. La [page N9 de Maemo Leste](https://leste.maemo.org/Nokia_N9)
  parle aussi d’un port Linux principal, mais affiche « TODO » pour le statut
  du N9 et « WORK IN PROGRESS » pour son installation.
- [opptimizer-n9](https://github.com/CreamyG31337/opptimizer-n9) a reçu un
  correctif en janvier 2026, mais c’est un module d’ajustement de fréquence,
  pas un noyau complet. Son
  [empaquetage](https://github.com/CreamyG31337/opptimizer-n9/blob/master/debian/control)
  et son [Makefile](https://github.com/CreamyG31337/opptimizer-n9/blob/master/opptimizer/Makefile)
  ciblent des versions anciennes de Harmattan. Sa compatibilité avec PR1.3 et
  avec le noyau du N9 connecté n’est pas établie.

## Ce que kernel-plus peut apporter

Dans le [code du fork `r7`](https://github.com/hurrian/kernel-plus-harmattan/compare/mer-n9-2.6.32-20121301...kernel-plus-r7),
on trouve les correctifs de la série `2.6.32.55` à `2.6.32.61`, un pilote
exFAT, Tiny RCU et des changements du système de fichiers. La configuration
RM-696 active notamment exFAT, NTFS en lecture/écriture, NFS et NAT. Ces
fonctions pourraient servir à lire certains supports externes et à configurer
des services réseau ; leur présence dans le noyau ne les active pas
automatiquement dans Harmattan. Tiny RCU vise à réduire l’empreinte mémoire,
sans gain de vitesse ou d’autonomie mesuré sur ce N9. Les correctifs `.61`
datent de 2013 et ne mettent pas le téléphone au niveau de sécurité actuel.

Ce même fork contient un
[changement qui contourne plusieurs contrôles Aegis](https://github.com/hurrian/kernel-plus-harmattan/commit/abe509b0d5).
Installer ce noyau peut donc modifier la protection et le mode de démarrage
du téléphone. La [documentation Ubiboot](https://wiki.maemo.org/Ubiboot)
signale aussi qu’un passage en Open Mode peut nécessiter de reconfigurer les
comptes. Un noyau différent exige ses modules correspondants ; une image qui
ne démarre pas ou des modules absents peuvent rendre le téléphone inutilisable
jusqu’à restauration. Enfin, cette mise à jour ne change pas en soi le mot de
passe de SDK Connectivity ni l’appairage USB : ceux-ci dépendent de la
configuration du service SSH.

Conclusion : `kernel-plus-harmattan` est un noyau communautaire conçu pour
**Harmattan**, en version `2.6.32.61`. Le fork `r7` contient une configuration
RM-696. Cela identifie sa cible ; son installation sur le téléphone connecté
reste à valider. Je n’ai pas trouvé de branche
Harmattan plus récente et maintenue parmi les sources examinées. L’ancienne
archive communautaire et le fork `r7` ne sont pas des mises à jour validées
pour ce téléphone. Un port Linux alternatif est un changement de système
expérimental, pas une mise à jour du noyau Harmattan. Avant d’envisager une
installation, relever `uname -r` sur le N9, vérifier son modèle et son mode
de démarrage, puis préparer une image et ses modules avec une procédure de
restauration. ResurectPhone ne doit pas proposer un flashage à partir du seul
numéro de version.

## Accès USB automatique depuis tout PC

### Parcours utilisateur

1. Brancher le N9 en mode USB SDK. ResurectPhone essaie sa clé enregistrée,
   puis l’accès sans authentification sur un téléphone encore inconnu.
2. Sur un N9 d’origine qui refuse cet accès, saisir une seule fois le mot de
   passe temporaire affiché par SDK Connectivity. Une clé propre au PC est
   installée et protégée par le compte Windows ; le mot de passe SDK n’est
   pas conservé. Le branchement d’un N9 reconnu en USB autorise cet appairage.
3. ResurectPhone essaie automatiquement le mot de passe administrateur
   d’origine `rootme` avec `devel-su`. Il ne demande un mot de passe
   personnalisé que si celui d’origine est refusé. Cette priorité est conservée
   à chaque tentative. Les secrets passent par l’entrée standard et leurs
   buffers sont effacés ; aucun mot de passe personnalisé n’est enregistré.
4. L’application transfère et lance la préparation par SSH, installe le
   compagnon `resurectphone-n9`, puis ouvre une autre connexion sans clé ni
   mot de passe pour vérifier le résultat. Elle enregistre aussi le mode
   `windows_network` dans `/Meego/System/UsbMode`.
5. Les prochains PC peuvent s’appairer sans saisie sur cette liaison USB.
   Le bouton **Préparer USB** relance la vérification et la préparation.

Si aucun premier accès SSH n’est possible, le repli par stockage USB reste
disponible : copie dans `MyDocs/ResurectPhone`, puis lancement unique depuis
Terminal avec `devel-su`. Ce repli n’est pas nécessaire quand SSH fonctionne.
Le script autonome est
[`tools/n9/enable-usb-passwordless.sh`](../../tools/n9/enable-usb-passwordless.sh).

### Persistance et Aegis

SDK Connectivity génère des mots de passe et son option de suppression
verrouille le compte avec `*` au lieu de lui donner un mot de passe vide.
Le compagnon surveille les changements de `/etc/passwd` et `/etc/shadow`
avec `inotifywait`, puis rétablit le mot de passe vide de `developer`. Il ne
fait pas de scrutation périodique. Le service Upstart démarre avec SSH.
L’écran Nokia peut encore afficher un mot de passe : le réglage porte sur
l’authentification réelle du compte, pas sur le texte de cette application.

Modifier directement le script Nokia `password.sh` empêche son exécution
par Aegis ; écrire directement le service dans `/etc/init` est aussi refusé.
Le compagnon utilise donc un paquet Debian dédié, avec ses empreintes
`digsigsums`, installé par `dpkg`. Le script Nokia conserve son empreinte
d’origine. Le format des empreintes suit les outils SDK ; il ne constitue
pas une signature Nokia. La
[documentation Aegis](https://katastrophos.net/harmattan-dev/html/guide/html/Developer_Library_Developing_for_Harmattan_Harmattan_security_Security_guide_Harmattan_security_FAQ.html)
décrit la protection de ces fichiers et l’installation par paquet.

### Portée et restauration

La préparation sauvegarde la configuration SSH et les comptes sous
`/var/lib/resurectphone/backups/usb-<date>-<pid>`, en accès root uniquement.
Le N9 testé stocke les mots de passe dans `/etc/passwd`, sans fichier shadow.
Les noms de sauvegarde utilisent l’horloge du N9, qui affichait août 2013.

OpenSSH 5.1 accepte `PermitEmptyPasswords` uniquement au niveau global.
Le script vérifie d’abord qu’aucun autre compte n’a un mot de passe vide,
interdit SSH root et n’autorise le mot de passe de `developer` que depuis
`192.168.2.0/24`. Hors de ce sous-réseau, les méthodes password et
keyboard-interactive sont désactivées pour ce compte. La configuration est
validée sur le téléphone avec `sshd -t` et `sshd -T` avant application.
Cette restriction porte sur l’adresse source : un autre réseau utilisant
également `192.168.2.x` pourrait en bénéficier. Tout PC sur la liaison USB
obtient une session `developer`. Le mot de passe de `devel-su` reste inchangé.

Pour revenir au réglage sauvegardé, lancer en administrateur
`sh <dossier-de-sauvegarde>/restore.sh`. Le service est arrêté, la configuration
SSH et le mot de passe de `developer` sont rétablis ; les autres comptes sont
préservés. Le compagnon reste installé mais inactif après une restauration de
la configuration initiale. L’installation tente cette restauration si elle
échoue après le début des modifications. La sauvegarde contient des secrets
et reste exclusivement sur le téléphone.

### Essais matériels du 26 septembre 2026

- Première connexion SDK puis reconnexion par clé enregistrée : réussies.
- Installation et vérification depuis le moteur ResurectPhone, avec le mot
  de passe administrateur d’origine essayé automatiquement : réussies.
- Nouvelle connexion sans clé ni mot de passe : réussie.
- Verrouillage du compte par `usermod -p '*' developer`, comme le fait SDK
  Connectivity, puis retour automatique à un mot de passe vide : réussi.
- Redémarrage du service, restauration puis réinstallation : réussis.
- Redémarrage complet du N9 : reconnexion sans mot de passe réussie, mode
  USB conservé et service démarré automatiquement. Un nouveau verrouillage
  du compte après démarrage est corrigé, puis la connexion anonyme réussit.
- Reconnaissance de la configuration existante sans réinstallation : réussie.
- Compilation WPF : aucune erreur ni avertissement ; 32 tests réussis.

Le test avec un second PC physique reste à effectuer ; la connexion neuve
utilisée pour la vérification n’avait aucune clé ni aucun mot de passe.
