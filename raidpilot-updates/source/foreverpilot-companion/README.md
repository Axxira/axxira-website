# ForeverPilot Companion 1.0.0

## Français

Ce compagnon sert à l'édition publique de ForeverPilot. **Le launcher RaidPilot est
gratuit et n'en a pas besoin.** Windows 10/11 x64, WoW Forever et Ding sont requis.

1. Extrais entièrement le ZIP et ouvre `ForeverPilot.Companion.exe`.
2. Choisis ta langue, puis **Ouvrir mon abonnement**. Le portail officiel est
   https://foreverpilot-subscriptions.tyronixstudio.workers.dev.
3. Crée ton accès, **enregistre ta clé personnelle**, puis confirme l'abonnement PayPal.
   Le service doit confirmer un paiement de 9,99 CAD pour activer la période mensuelle.
4. Dans le compagnon, **Importer…** ouvre le fichier `ForeverPilot-personal-key.json`.
   Tu peux aussi coller la clé. Ne partage jamais cette clé.
5. Vérifie le dossier `_forever_`, ferme WoW pour la première installation, puis clique
   **Activer / actualiser mon accès**. Relance WoW par Battle.net.

Laisse le compagnon ouvert pendant tes sessions pour une vérification horaire, ou
ouvre-le régulièrement. L'accès hors ligne est limité à sept jours et à la période
payée. Reconnecte-toi à WoW pour charger une actualisation. Le programme ne s'ajoute
pas automatiquement au démarrage de Windows. Tu peux annuler les prochains paiements
dans le portail ou PayPal sans perdre le temps déjà payé.

Seul `Interface/AddOns/ForeverPilot_Access` est écrit, avec copie du fichier précédent.
La clé est chiffrée pour ton compte Windows, jamais écrite dans les Lua. Ding, le moteur
ForeverPilot_Leveling, l'ancien bridge et les sauvegardes WTF restent séparés.
Une panne réseau conserve l'accès existant jusqu'à son échéance; une révocation confirmée
retire l'accès actif, pris en compte au prochain chargement de WoW.

## English

The public edition uses this Windows 10/11 x64 companion. **RaidPilot users do not need
it: their edition is fully free.** Extract the ZIP and run `ForeverPilot.Companion.exe`.
Choose English, open the subscription portal, create access and save your personal key.
Confirm the CAD 6.99 monthly subscription on PayPal, then use **Import…** to load the key
file in the companion. Check the `_forever_` folder, close WoW for initial installation,
click **Activate / refresh my access**, then launch WoW through Battle.net.

Keep the companion open for hourly checks or open it regularly. Offline access lasts
at most seven days and never beyond the paid period. Log back into WoW after refreshing.
Cancel future renewals through the portal or PayPal; already paid time remains available.
The key is encrypted for your Windows user; no PayPal credentials enter the game.

## Development / limitations

`dotnet run -- --self-test` validates signed licenses, expiry, issuer and environment.
`--sandbox` uses a separate test service and cannot install game access. No background
startup registration and no payment collection inside the executable. The addon is open
Lua code; local entitlement flags are not tamperproof DRM.
