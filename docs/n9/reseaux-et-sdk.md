# Réseaux et accès SDK du Nokia N9

## Commandes

Ouvrir **Nokia N9 → Réseaux et SDK → Gérer les réseaux et le SDK** après connexion.
Le bouton **Connexion Wi-Fi…** accepte l’adresse IPv4 locale affichée par SDK Connectivity. La clé du PC est réutilisée et l’empreinte du téléphone est vérifiée. Une première connexion peut demander le mot de passe SDK, puis la clé permet les connexions suivantes. L’adresse enregistrée est réutilisée au prochain lancement ; le branchement USB reprend la liaison USB.

La fenêtre permet de :

- lire les profils enregistrés et rechercher les réseaux à portée ;
- connecter un profil, activer son automatisme Harmattan ou le remettre en connexion manuelle ;
- **forcer la reconnexion** avec un service sur le N9, autonome lorsque le PC est fermé ;
- déconnecter le réseau après désactivation de son automatisme ;
- choisir le SDK **sans mot de passe en USB seul** ou **en USB et en Wi-Fi** ;
- restaurer une sauvegarde depuis l’historique.

Changer de réseau et déconnecter nécessitent une liaison USB pour pouvoir vérifier le résultat. Les réglages d’automatisme et du SDK peuvent être appliqués par Wi-Fi. La création de nouveaux profils reste à effectuer sur le téléphone. Les mots de passe Wi-Fi ne sont pas lus ni exportés.

## Fonctionnement

Le composant Python 2.6 utilise libconnsettings, ICD2 et libconic. Il modifie `autoconnect` pour le seul profil choisi, ajoute le Wi-Fi à la politique globale si nécessaire et limite l’intervalle de recherche à 60 secondes. Les autres types de connexion autorisés sont conservés. Le mode forcé réessaie avec un délai de 30 à 300 secondes. Il respecte le mode avion et le Wi-Fi éteint, ne coupe pas un autre Wi-Fi connecté et ne demande pas de connexion mobile de secours.

Le paquet `resurectphone-n9-network` installe un service Upstart repris au démarrage de la session. Les réglages restent sous `.config/resurectphone` et `.cache/resurectphone` sur le N9. Une sauvegarde des seuls réglages concernés accompagne chaque changement d’automatisme ; les erreurs déclenchent une restauration.

Le SDK utilise le compagnon `resurectphone-n9` version 0.1.2. Son surveillant maintient vide le mot de passe développeur lorsque SDK Connectivity le régénère. Le choix USB/Wi-Fi est conservé lors des préparations USB ultérieures. Le mode USB seul exige une clé hors du sous-réseau USB. Le mode USB et Wi-Fi ouvre l’accès développeur à tout appareil pouvant joindre SSH sur le N9. Le mot de passe du réseau Wi-Fi et celui de devel-su ne changent pas ; root ne devient pas accessible directement par SSH.

Chaque modification SDK sauvegarde la configuration SSH et le mot de passe développeur précédent sur le téléphone. Le logiciel valide la configuration effective, recharge SSH et teste une nouvelle connexion sans mot de passe. Le retour au mode USB seul vérifie également le refus de cette connexion lorsqu’il est appliqué en Wi-Fi. En cas d’erreur, une restauration est tentée. L’historique fournit aussi un retour arrière volontaire.

## Essais du 29 septembre 2026

- Lecture réelle des deux profils et du profil connecté sur le N9 PR1.3 par sa clé existante. Les deux réglages `autoconnect` étaient désactivés, la politique globale automatique autorisée, l’intervalle à 300 secondes et l’économie d’énergie inactive.
- Lecture réussie après passage du composant sur une connexion D-Bus privée.
- Compilation Release ; 93 tests .NET et 6 tests Python réussis. Couverture du retour arrière après erreur, conservation de la politique mobile, mode avion, profils manuels, adresses SDK et protection du compagnon.
- Syntaxe du script SDK contrôlée avec Bash ; lancement de l’application confirmé. L’arbre d’accessibilité de l’accueil est lisible, mais la capture visuelle Windows a expiré et le clic a échoué (`coordinate input geometry is unavailable`). La nouvelle fenêtre réseau n’est pas validée visuellement.

**Les nouvelles modifications réseau et SDK Wi-Fi ne sont pas encore validées sur le téléphone.** Le N9 a cessé de répondre en Wi-Fi avant leur application ; aucun adaptateur USB N9 n’était présent. Restent : installation réelle du compagnon Wi-Fi, application/restauration, coupure/reconnexion, SDK Wi-Fi sans clé ni mot de passe et persistance après redémarrage. Les essais USB antérieurs ne valident pas ces nouveaux essais.

## Sources techniques

- [Documentation réseau Nokia Harmattan, archive](https://katastrophos.net/harmattan-dev/html/guide/html/Developer_Library_Developing_for_Harmattan_Network_connectivity_in_Harmattan_applications.html)
- [Sources ICD2](https://github.com/maemo-leste/icd2)
- [Sources libconic](https://github.com/maemo-leste/libconic)

Les signatures natives ont été confrontées aux en-têtes du SDK Harmattan (`libconnsettings0-dev` et `libconic-dev`) et aux bibliothèques du N9.
