RaidPilot LocalFirst - fonds de carte
=====================================

Ce dossier est volontairement livré sans les SVG de tarkov.dev.

Le dépôt tarkov-dev-svg-maps possède des restrictions spécifiques contre l'utilisation
de ces dessins dans certains overlays/radars. Le patch LocalFirst ne télécharge donc
pas ces œuvres et ne les redistribue pas.

Pour un test local, placez ici uniquement des fonds de carte que vous êtes autorisé
à utiliser, puis mettez à jour manifest.json, par exemple:

{
  "maps": {
    "customs": "assets/customs.png",
    "woods": "assets/woods.jpg"
  }
}

L'outil Tools/Register-ApprovedMapAssets.ps1 peut enregistrer automatiquement des
images depuis un dossier que vous lui fournissez explicitement.
