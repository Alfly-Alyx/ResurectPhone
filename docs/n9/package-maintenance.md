# Maintenance des applications DEB du Nokia N9

## Statut

Ces règles proviennent d’essais effectués sur un Nokia N9 réel sous Harmattan.
Elles sont des contraintes de conception de ResurectPhone, pas de simples pistes.
Un cycle complet d’installation, sauvegarde du paquet original, suppression,
réinstallation, contrôle de version puis suppression finale a été validé sur
une application tierce jetable. Les parcours sont désormais reliés à une fenêtre
de maintenance : diagnostic, choix du paquet ou de l’application, exécution,
rapport et sauvegarde avant suppression.

## Suppression d’une application

ResurectPhone doit toujours essayer d’abord la suppression normale avec
`dpkg --no-act --remove`, puis `dpkg --remove`. Ces commandes vérifient
les dépendances de la suppression choisie sans réparer des dépendances cassées
sans rapport avec elle. Un paquet n’est pas bloqué uniquement parce qu’il est
référencé par le méta-paquet Harmattan : ce cas concerne notamment des
applications visibles comme `twitter-qml`.

Si la suppression normale est refusée et qu’une simulation APT confirme
un blocage de dépendances impliquant ce paquet, une seconde action peut être
proposée dans le mode Expert, avec un avertissement et une confirmation
distincts. La commande de repli validée est
`dpkg --force-depends --remove <paquet>`.

Une suppression n’est proposée que pour un paquet associé à une application
réellement visible dans l’inventaire du téléphone. Elle reste interdite pour :

- tout paquet `Essential: yes` ;
- les priorités `required` et `important` ;
- le compagnon `resurectphone-n9` lui-même.

La priorité `standard` ne suffit pas, à elle seule, à classer un paquet comme
protégé. L’appartenance au méta-paquet Harmattan non plus.

Les commandes administrateur passent par `devel-su`. À la demande du
propriétaire, ResurectPhone essaie systématiquement le mot de passe d’usine
`rootme` avant de demander celui personnalisé. Cette règle s’applique même
lorsqu’un mot de passe personnalisé vient d’être fourni. Le secret est envoyé
uniquement sur l’entrée standard, jamais dans la ligne de commande ni les
journaux, puis ses buffers sont effacés en mémoire. La préparation USB utilise
ce parcours, également utilisé par les opérations de maintenance.

## Sauvegarde et réinstallation du paquet original

Chaque installation conserve désormais sur le PC une copie vérifiée du `.deb`
original, indexée par paquet, version et empreinte SHA-256. Lors d’une sauvegarde,
ResurectPhone cherche exactement le paquet installé. Pour MeeShop 0.8 et
Warehouse 0.1.9, il peut aussi récupérer leur paquet officiel à empreinte fixée.
Le bouton **Réinstaller cette sauvegarde** utilise le paquet original conservé.
Après fermeture de la fenêtre, **Réinstaller une sauvegarde…** permet de le
sélectionner dans Documents/ResurectPhone/N9/Backups.

Cette sauvegarde contient le fichier d’installation. Elle ne contient pas les
données personnelles de l’application. Sans original retrouvé, le repli est
l’archive reconstruite ci-dessous, identifiée comme non réinstallable automatiquement.

## Sauvegarde reconstruite en `.deb`

La reconstruction directe par `dpkg-deb --build` ne doit pas être utilisée sur
Harmattan : l’ancien outil réclame à `tar` l’option GNU `--format=gnu`, absente
du BusyBox installé sur le N9. MyDocs est en FAT et ne convient pas non plus à
la reconstruction d’une arborescence Debian avec propriétaires et permissions.

Le parcours retenu est donc :

1. créer une arborescence temporaire sous `/var/tmp/resurectphone-*` ;
2. recopier les chemins listés par `/var/lib/dpkg/info/<paquet>.list` ;
3. recréer `control` avec `dpkg-query` et recopier les scripts de maintenance ;
4. produire séparément `control.tar.gz` et `data.tar.gz` avec BusyBox ;
5. télécharger ces deux archives ;
6. assembler sur le PC un conteneur ar Debian 2.0 contenant, dans cet ordre,
   `debian-binary`, `control.tar.gz` et `data.tar.gz`.

Le script envoyé au téléphone est normalisé en LF. Aucun CRLF ne doit être
transmis, car BusyBox `sh` l’interprète comme une option invalide.

Le moteur PC correspondant est `N9DebianArchiveAssembler`. Le cas réel de
référence est le paquet `calc`, exporté en 52 644 octets puis reconnu sur PC
comme ARMEL, version `1.2.0.2+0m7`.

## Validation avant installation

Le parcours intégré vérifie le conteneur ar et extrait le fichier `control`
sur le PC avant tout transfert. L’ancienne chaîne de diagnostic compatible
avec le N9 reste :

```text
busybox ar -p <paquet.deb> control.tar.gz | busybox tar -xzOf - ./control
```

Les champs `Package`, `Version` et `Architecture` sont obligatoires.
L’architecture doit être `armel` ou `all`, et l’identifiant du paquet doit
respecter la syntaxe Debian avant que `dpkg -i` puisse être envisagé.

## Provenance Aegis

Un `.deb` lisible par Debian n’est pas nécessairement réinstallable sur
Harmattan. Le paquet `calc` reconstruit a été refusé lors d’une installation
par-dessus l’exemplaire Nokia : Aegis a détecté que le paquet installé venait
de `com.nokia.maemo`, tandis que la reconstruction avait une origine inconnue.

Par conséquent, toute sauvegarde reconstruite sans provenance Aegis est
étiquetée `DebianArchiveOnly` et sa restauration automatique est bloquée.
Elle ne deviendra restaurable qu’après l’un des contrôles suivants :

- cycle sauvegarde, suppression et réinstallation réussi sur une application
  tierce non protégée ;
- préservation ou récupération démontrée des métadonnées Aegis d’origine.

Cette limite ne bloque pas l’installation d’un nouveau paquet. Un cycle réel a
été validé avec `androlink-install-smoke`, version `0.8.12`, architecture
`all` : installation réussie, état `install ok installed` et fichier installé
vérifiés, puis désinstallation et absence finale du paquet confirmées. La
capacité d’installer et de supprimer un `.deb` tiers valide est donc acquise ;
le cas restant est le remplacement ou la restauration d’un paquet déjà installé
et protégé par une origine Aegis différente.

## Dépôts du téléphone de test

Les anciennes lignes trouvées dans `n9repomirror.list` pointaient vers
`mirror.thecust.net/harmattan-dev.nokia.com/` et `coderus.openrepos.net/n9mirro/`.
Leur présence ne signifie pas qu’elles fonctionnent : elles ont échoué lors
de la vérification du 26 septembre 2026, et `n9mirro` est un chemin incorrect.

La réparation intégrée écrit un fichier distinct `resurectphone.list` avec les
miroirs WunderWungiel vérifiés. Les anciennes sources protégées par Aegis
restent présentes, mais sont exclues de cette actualisation. Voir
[le bilan des boutiques, dépôts et TLS](boutiques-depots-tls.md).
