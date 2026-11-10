# DungeonMapPilot commercial release

Separate product: dungeon_maps, CAD 5.99 every calendar month, no setup fee or trial.
PayPal catalog and signing keys are distinct from ForeverPilot leveling. Separate
Cloudflare Workers, Durable Object namespaces, sessions and recovery keys prevent
one purchase from granting the other product. Existing leveling is unchanged.

Production checkout is disabled until Maps-specific Sandbox payment, signed
activation, cancellation and refund evidence is recorded, the companion download
is verified, and in-instance coverage is assessed. Approval/ACTIVE alone never
grants access: a completed matching CAD 5.99 transaction is required.

## Components

- Addon: DungeonMapPilot; optional dependency DungeonMapPilot_Access.
- Companion: DungeonMapPilot.Companion, Windows x64, .NET 10 self-contained.
- Live: https://dungeonmappilot-subscriptions.tyronixstudio.workers.dev
- Sandbox: https://dungeonmappilot-subscriptions-sandbox.tyronixstudio.workers.dev
- JWT: RS256; audience dungeonmappilot; product dungeon_maps; exact feature dungeon_maps.
- Companion verifies issuer/environment/subject/expiry and writes only its dedicated
  access addon. Revocation does not touch ForeverPilot_Access, Ding, leveling or bridge.
- Offline leases last at most seven days and never exceed the paid period. Refund
  revocation applies on next refresh; an offline lease cannot be remotely erased
  from a closed game/companion. Lua is editable, not tamperproof DRM.
- RaidPilot test addon stays fully free/offline. Sandbox tokens cannot unlock live Lua.

## Validation and deployment

npm test runs an in-memory SQLite billing suite, not real PayPal payments. Build
the companion and run --self-test; fixture writes stay under its build folder.
Run addon frame-double scenarios separately. In-game validation is a separate check.

Wrangler defaults production BILLING_ENABLED=false and LIVE_VERIFIED=false.
Supply PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_PLAN_ID, PAYPAL_WEBHOOK_ID and
SIGNING_JWK as secrets. Never commit .dev.vars*, *.secret*, recovery keys, fixtures,
build outputs or customer data. COMPANION_URL must be an exact verified release ZIP
on the owner repository; live checkout stays closed without it.

Both webhook signatures and transaction reconciliation are required. Return URLs
are status pages, never proof of payment. Cancellation retains paid time;
refund/reversal and suspension stop renewal of the offline lease.

The owner's tests use fictitious money only. No live purchase is authorized for
testing. Published Blizzard addon policy prohibits paid addon features; technical
implementation does not constitute distribution approval.
