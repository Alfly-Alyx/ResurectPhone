# Nokia N9 : noyau et appairage USB

État du N9 vérifié le 25 septembre 2026 ; dépôts GitHub revérifiés le
26 septembre 2026. Le N9 connecté répond sur l’interface USB
`192.168.2.15`, mais refuse actuellement l’authentification SSH sans clé ou mot
de passe. ResurectPhone n’a donc pas encore pu lire sa version exacte avec
`uname -r`.

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
  Le `3.5.3` de Nemo n’est pas interchangeable avec le noyau Harmattan.
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

Conclusion : `kernel-plus-harmattan` est une piste historique de mise à niveau
du noyau **Harmattan PR1.3** vers `2.6.32.61`. Je n’ai pas trouvé de branche
Harmattan plus récente et maintenue parmi les sources examinées. L’ancienne
archive communautaire et le fork `r7` ne sont pas des mises à jour validées
pour ce téléphone. Un port Linux alternatif est un changement de système
expérimental, pas une mise à jour du noyau Harmattan. Avant d’envisager une
installation, relever `uname -r` sur le N9, vérifier son modèle et son mode
de démarrage, puis préparer une image et ses modules avec une procédure de
restauration. ResurectPhone ne doit pas proposer un flashage à partir du seul
numéro de version.

## Accès USB automatique depuis tout PC

Le serveur SSH d’origine annonce `publickey,password` et refuse `none`.
ResurectPhone ne peut donc pas modifier cet état depuis un PC encore non
autorisé. Une préparation unique depuis le Terminal du N9 est nécessaire.

1. Dans ResurectPhone, ouvrir Nokia N9 puis choisir **Préparer USB** pour
   enregistrer le script. Copier ce fichier dans `MyDocs` du N9, par exemple
   en utilisant temporairement le mode USB stockage de masse. Le script source
   se trouve dans [`tools/n9/enable-usb-passwordless.sh`](../../tools/n9/enable-usb-passwordless.sh).
2. Ouvrir Terminal sur le N9, saisir `devel-su`, puis exécuter
   `sh /home/user/MyDocs/enable-usb-passwordless.sh`.
3. Revenir au mode USB SDK, laisser SDK Connectivity actif, puis ouvrir
   ResurectPhone sur un PC connecté par câble. Le logiciel détecte le N9,
   vérifie Harmattan, installe une clé propre à ce PC et lit son identité.

Le script sauvegarde `/etc/ssh/sshd_config` et `/etc/shadow` sous
`/var/tmp/resurectphone-ssh-*`, vérifie la configuration avec `sshd -t`, puis
autorise le compte `developer` sans mot de passe pour une adresse du sous-réseau
USB `192.168.2.0/24`. L’adresse habituelle du PC est `192.168.2.14`, mais la
[documentation Nokia du SDK](https://n9.dy.fi/meego/html/guide/html/Developer_Library_Getting_started_with_Harmattan_using_Qt_SDK_Connecting_the_device_to_Qt_SDK.html)
permet d’en choisir une autre dans ce sous-réseau. Les adresses extérieures
gardent `PermitEmptyPasswords no`. Le serveur SSH ancien du N9 ne permet pas de
restreindre cette règle par interface : un hôte sur un autre réseau utilisant
aussi une adresse `192.168.2.x` pourrait en bénéficier. Tout PC ayant accès à
cette liaison USB peut obtenir une session `developer`. Le mot de passe administrateur
utilisé par `devel-su` n’est pas modifié.

Pour annuler la préparation, depuis `devel-su` sur le N9, restaurer les deux
fichiers conservés dans le dossier de sauvegarde affiché par le script, puis
redémarrer le serveur SSH ou le téléphone. Cette sauvegarde contient
`/etc/shadow` : elle doit rester privée.

Le 26 septembre 2026, le N9 branché répondait sur SSH (`192.168.2.15:22`),
mais refusait encore une connexion `developer` sans authentification. La
syntaxe du script a été vérifiée localement pour la règle `192.168.2.0/24` ;
ResurectPhone se compile avec ce script intégré et ses 25 tests passent.

La préparation n’a pas encore été exécutée sur le N9. Après son exécution,
tester l’appairage automatique depuis ce PC puis depuis un second PC en mode
USB SDK, lire `uname -r`, et confirmer après fermeture de SDK Connectivity et
redémarrage du téléphone que le compte `developer` reste sans mot de passe.
Si SDK Connectivity le rétablit, la préparation devra être adaptée avant de
présenter l’appairage universel comme validé.
