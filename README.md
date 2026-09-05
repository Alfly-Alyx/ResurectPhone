# ResurectPhone

ResurectPhone est un laboratoire autonome de remise en service de téléphones. Il est développé séparément d’AndroLink, avec une interface compatible avec ses codes visuels pour permettre une intégration ultérieure.

## Premier socle

- détection générique d’un téléphone connecté, sans inventer son identité ;
- catalogue indépendant des fonctions de restauration ;
- séparation entre interface, règles métier et accès Windows ;
- aucune opération de flashage ou de modification du téléphone dans ce premier socle ;
- aucune dépendance directe vers les projets AndroLink.

## Périmètre prévu

- Nokia N9 : ROM Harmattan PR1.3, dépôts, applications, Nokia Store, compte Nokia, navigation Internet, certificats, GPS, Cartes et Drive ;
- Lumia : diagnostic, Windows Internals et installation expérimentale des projets Android compatibles ;
- ressources de réparation disponibles hors connexion lorsque leur redistribution est autorisée ;
- vérification en ligne après chaque réparation qui dépend d’un service Internet.

Les opérations sensibles devront toujours vérifier l’appareil, la compatibilité, l’intégrité des fichiers et la présence d’une sauvegarde avant de devenir accessibles.
