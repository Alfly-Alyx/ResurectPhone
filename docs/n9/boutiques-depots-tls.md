# Boutiques, dépôts et TLS du Nokia N9

Vérifications et essais du 26 septembre 2026, sur Harmattan PR1.3 RM-696.

## Boutiques proposées

| Solution | État trouvé | Proposition dans ResurectPhone |
| --- | --- | --- |
| [MeeShop GUI](https://openrepos.net/content/iarchep/meeshop-gui) | 0.8, juillet 2026 ; interface OpenRepos, installation, suppression et mises à jour ; recherche encore incomplète | Installation intégrée. Paquet officiel vérifié et installé sur le N9. Navigation et installation depuis son écran restent à essayer avec Internet sur le téléphone. |
| [MeeShop CLI](https://github.com/WunderWungiel/MeeShop) | 0.2.0, 2023 ; dépôt archivé en avril 2025 | Mentionné comme alternative historique. Même identifiant `meeshop` que la GUI : les deux versions se remplacent. |
| [Warehouse](https://openrepos.net/content/basil/warehouse) | 0.1.9, juillet 2014 | Piste ancienne à retester après correction TLS ; pas d’installation automatique proposée sans validation actuelle. |

MeeShop GUI est annoncé par son auteur comme utilisable sans le correctif TLS
système. Son paquet utilise `hack-installer`, déjà présent sur le téléphone
de test. ResurectPhone vérifie ce prérequis et refuse une installation si
le paquet téléchargé ne correspond plus à l’empreinte validée.

Paquet retenu :
[meeshop_0.8_armel.deb](https://openrepos.net/sites/default/files/packages/19569/meeshop_0.8_armel.deb).
SHA-256 : `0D44B74CDEE588FA73DDAAAFB2D922D167D8C21947F6630DFCB0E3806D34EA35`.

La boutique Nokia d’origine dépend de services distants qui ne sont pas
rétablis par la présence du client. Sa fiche permet un diagnostic et décrit
ce blocage ; elle ne simule pas une réparation réussie.

## Dépôts système et SDK

Le [FIXED N9 RepoMirror](https://openrepos.net/content/wunderwungiel/fixed-n9-repomirror)
0.7.5 de décembre 2025 fournit la piste actuelle vérifiée. ResurectPhone
configure directement un fichier distinct, sauvegardé avant modification :

```text
deb http://wunderwungiel.pl/MeeGo/n9mirror/001 ./
deb http://wunderwungiel.pl/MeeGo/n9mirror/apps ./
deb http://wunderwungiel.pl/MeeGo/n9mirror/tools ./
deb http://wunderwungiel.pl/MeeGo/harmattan-dev.nokia.com/ harmattan/sdk free non-free
```

Les cinq index ARMEL correspondants ont été téléchargés par APT sur le N9
via un relais USB temporaire. Le PC contacte le miroir en HTTPS et valide
son certificat. Le relais accepte uniquement les chemins des miroirs connus
et disparaît à la fin de l’opération. Le téléphone n’a pas besoin de DNS ni
d’accès Internet propre pour cette actualisation.

Les anciens fichiers Nokia/RepoMirror, dont certains sont protégés par Aegis,
restent présents. L’actualisation ResurectPhone les exclut explicitement.
Un `apt-get update` ordinaire lancé ailleurs pourra donc encore rencontrer
leurs erreurs. Aucune mise à niveau globale de Harmattan n’est déclenchée.

Sur le téléphone de test, APT signale aussi des dépendances préexistantes
manquantes pour Facebook et Twitter. L’installation des groupes d’outils
développeur doit réussir sa simulation avant toute modification ; elle peut
rester bloquée par cet état. ResurectPhone ne réinstalle pas automatiquement
ces anciennes applications pour faire disparaître l’erreur.

Le [dépôt WunderN9](https://wunderwungiel.pl/MeeGo/wundern9/) contient aussi
des adaptations communautaires plus récentes. Il reste une piste séparée,
sans activation générale ni mise à niveau automatique dans cette version.

## Correctif TLS 1.2

Le [correctif WunderWungiel 0.0.2](https://wunderwungiel.pl/MeeGo/content/?id=4)
est intégré au parcours Internet. Il installe OpenSSL 1.0.2u et des adaptations
de QtNetwork, libcurl et de composants Harmattan, avec actualisation des
certificats. Les 16 paquets ont été installés sur le N9 connecté ; une
connexion réelle en TLS 1.2 avec vérification du certificat et du nom
`openrepos.net` a réussi depuis son OpenSSL. Le PC transportait les octets
par USB ; la négociation et la validation étaient effectuées par le N9.

### Préparation automatique

1. Vérifier RM-696, PR1.3, les versions Nokia attendues et l’espace disponible.
2. Télécharger sur le PC l’archive officielle et les 13 paquets Nokia
   remplacés, destinés au retour arrière ; vérifier leurs empreintes et identités.
3. Synchroniser l’heure avec le PC si nécessaire, via le service `timed`.
4. Transférer les paquets, conserver une sauvegarde dédiée sur le N9,
   puis installer dans l’ordre prévu par l’auteur via `aegis-dpkg`.
5. Vérifier les 16 états installés et `dpkg --audit` ; proposer la vérification
   TLS et la restauration dans la même fenêtre.

La synchronisation utilise temporairement une règle D-Bus autorisant
uniquement root à appeler le réglage de l’heure de `timed`. La règle est
retirée à la fin, y compris après une erreur normale ; une coupure électrique
brutale peut nécessiter sa suppression au prochain diagnostic. Aucun contrôle
des certificats TLS n’est désactivé pour contourner une mauvaise date.

L’archive est conservée dans
`%LOCALAPPDATA%\ResurectPhone\N9\TLS-1.2-0.0.2`.
SHA-256 : `6881BD4E1B899D8FBF37407E234DC1479C81DB908D6A8381EDE46EFD298358A6`.
Les originaux sont conservés dans son sous-dossier `original` et dans un
dossier `/var/lib/resurectphone/maintenance/<identifiant>` sur le N9.
Aucun paquet tiers binaire n’est ajouté au dépôt GitHub.

### Limites

- Relancer les applications pour qu’elles chargent les bibliothèques modifiées.
- La vérification OpenSSL ne remplace pas un essai du navigateur et de chaque
  application à l’écran, avec une connexion Wi-Fi ou mobile fonctionnelle.
- OpenSSL 1.0.2u reste ancien. Le correctif ne modernise pas le moteur Web,
  n’ajoute pas TLS 1.3 et ne garantit pas l’accès à tous les sites actuels.
- Une installation communautaire SSL préexistante bloque ce parcours, qui ne
  dispose pas de son plan de restauration.
- Le retour arrière restaure les 13 paquets Nokia et retire les trois ajouts.
  Il conserve l’heure corrigée et les autorités ajoutées. Les fichiers
  résiduels du répertoire SSL créé par cette installation sont déplacés dans
  la sauvegarde au lieu d’être effacés.
