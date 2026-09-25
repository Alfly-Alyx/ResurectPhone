# Nokia N9 : noyau et appairage USB

État vérifié le 25 septembre 2026. Le N9 connecté répond sur l’interface USB
`192.168.2.15`, mais refuse actuellement l’authentification SSH sans clé ou mot
de passe. ResurectPhone n’a donc pas encore pu lire sa version exacte avec
`uname -r`.

## Résultat de la recherche du noyau

- La base Harmattan PR1.3 du N9 utilise la famille de noyaux `2.6.32.54`.
  [Documentation Ubiboot de maemo.org](https://wiki.maemo.org/Ubiboot).
- Les arbres communautaires Harmattan repérés sur GitHub sont anciens :
  [nemomobile/kernel-adaptation-n950-n9](https://github.com/nemomobile/kernel-adaptation-n950-n9)
  n’a pas reçu de code depuis janvier 2016 ;
  [hurrian/kernel-plus-harmattan](https://github.com/hurrian/kernel-plus-harmattan)
  depuis avril 2014. Leur existence ne constitue pas une mise à jour validée
  pour le N9 branché.
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

Conclusion : aucune mise à jour récente du noyau **Harmattan PR1.3** prête à
installer n’a été confirmée. Un port Linux alternatif est un changement de
système expérimental, pas une mise à jour du noyau Harmattan. ResurectPhone ne
doit pas proposer un flashage à partir du seul numéro de version.

## Accès USB automatique depuis tout PC

Le serveur SSH d’origine annonce `publickey,password` et refuse `none`.
ResurectPhone ne peut donc pas modifier cet état depuis un PC encore non
autorisé. Une préparation unique depuis le Terminal du N9 est nécessaire.

1. Dans ResurectPhone, ouvrir Nokia N9 puis choisir **Préparer USB** pour
   enregistrer le script. Copier ce fichier dans `MyDocs` du N9, par exemple
   en utilisant temporairement le mode USB stockage de masse. Le script source
   se trouve dans [`tools/n9/enable-usb-passwordless.sh`](../tools/n9/enable-usb-passwordless.sh).
2. Ouvrir Terminal sur le N9, saisir `devel-su`, puis exécuter
   `sh /home/user/MyDocs/enable-usb-passwordless.sh`.
3. Revenir au mode USB SDK, laisser SDK Connectivity actif, puis ouvrir
   ResurectPhone sur un PC connecté par câble. Le logiciel détecte le N9,
   vérifie Harmattan, installe une clé propre à ce PC et lit son identité.

Le script sauvegarde `/etc/ssh/sshd_config` et `/etc/shadow` sous
`/var/tmp/resurectphone-ssh-*`, vérifie la configuration avec `sshd -t`, puis
autorise le compte `developer` sans mot de passe lorsque le PC a l’adresse
`192.168.2.14`, attribuée par la liaison USB. Les autres adresses gardent
`PermitEmptyPasswords no`. Le serveur SSH ancien du N9 ne permet pas de
restreindre cette règle par interface ; un autre hôte utilisant la même adresse
pourrait aussi en bénéficier. Tout PC ayant physiquement accès à cette liaison
USB peut obtenir une session `developer`. Le mot de passe administrateur
utilisé par `devel-su` n’est pas modifié.

La procédure n’a pas encore été exécutée sur le N9 branché. Après son exécution,
il faudra vérifier l’appairage automatique, relever `uname -r` et confirmer
que SDK Connectivity ne rétablit pas le mot de passe `developer` au redémarrage.
