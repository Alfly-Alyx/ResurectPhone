# Maintenance des applications DEB du Nokia N9

## Statut

Ces règles proviennent d’essais effectués sur un Nokia N9 réel sous Harmattan.
Elles sont des contraintes de conception de ResurectPhone, pas de simples pistes.
Les actions d’installation, de sauvegarde et de suppression restent désactivées
dans l’interface tant que leur cycle complet n’a pas été validé sur une
application tierce supprimable.

## Suppression d’une application

ResurectPhone doit toujours essayer d’abord la suppression normale avec
`apt-get remove`. Un paquet n’est pas bloqué uniquement parce qu’il est
référencé par le méta-paquet Harmattan : ce cas concerne notamment des
applications visibles comme `twitter-qml`.

Si APT refuse seulement à cause des dépendances, une seconde action peut être
proposée dans le mode Expert, avec un avertissement et une confirmation
distincts. La commande de repli validée est
`dpkg --force-depends --remove <paquet>`.

La suppression forcée reste interdite pour :

- tout paquet `Essential: yes` ;
- les priorités `required` et `important` ;
- le compagnon `resurectphone-n9` lui-même.

La priorité `standard` ne suffit pas, à elle seule, à classer un paquet comme
protégé. L’appartenance au méta-paquet Harmattan non plus.

Les commandes administrateur passent par `devel-su`. ResurectPhone demande
d’abord à l’utilisateur l’identifiant administrateur d’origine du N9. S’il est
refusé, une seconde saisie permet d’utiliser celui configuré sur le téléphone.
Aucune valeur par défaut n’est inscrite dans le code ou dans les journaux. Le
secret est envoyé uniquement sur l’entrée standard de la commande, jamais dans
sa ligne de commande, puis les caractères et les octets détenus par le logiciel
sont effacés en mémoire.

## Sauvegarde en `.deb`

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

`dpkg-deb -f` n’est pas utilisé sur le N9. ResurectPhone extrait le fichier
`control` avec la chaîne compatible testée :

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

## Dépôts observés sur le téléphone de test

ResurectPhone doit aussi contrôler
`/etc/apt/sources.list.d/n9repomirror.list`. Les lignes fonctionnelles observées
sont :

```text
deb http://mirror.thecust.net/harmattan-dev.nokia.com/ ./
deb http://coderus.openrepos.net/n9mirro/ ./
```

Avant toute modification, le fichier existant devra être sauvegardé. Ces URLs
seront vérifiées en ligne au moment où la réparation des dépôts sera activée ;
leur simple présence dans ce document ne constitue pas une garantie permanente.
