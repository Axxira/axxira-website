# ForeverPilot subscription service

Dedicated Cloudflare Worker + SQLite Durable Object; never shares the RaidPilot relay.
Public plan: CAD 6.99 monthly, no trial or setup fee. The RaidPilot test edition is always
free and never calls this service. The portal and companion support French and English.

## Verification and deployment

- Node 24: `node --test test/*.test.mjs` (SQLite built-in, no database credentials).
- Install the pinned deployment CLI with `npm ci --ignore-scripts`.
- Default `wrangler.jsonc` targets **sandbox**; `wrangler.live.jsonc` targets **live**
  with checkout disabled by default. Keep these environments and secrets separate.
- Provision only the owner's explicitly authorized PayPal app. Private `.secret.json`
  files and `.dev.vars*` are ignored. Never commit them, logs, account screenshots or keys.
- Required secrets: PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_PLAN_ID,
  PAYPAL_WEBHOOK_ID, SIGNING_JWK. The RSA private JWK remains server-side.
- Live also requires a verified release asset in COMPANION_URL, explicitly enabled
  BILLING_ENABLED and LIVE_VERIFIED. Use `wrangler deploy --config wrangler.live.jsonc
  --var BILLING_ENABLED:true --var LIVE_VERIFIED:true` only after sandbox E2E passes and
  the companion artifact has been published and downloaded successfully. Subsequent
  deployments using the default live config disable new checkouts intentionally.
- Signed PayPal webhooks target `/api/webhook`. Hourly reconciliation repairs missed
  activation notifications; refund/reversal events are recorded idempotently.

Before opening checkout: validate the exact plan, complete a real sandbox approval and
payment, verify the signed result with the Windows companion, cancel and verify retained
paid time, verify refund/reversal revocation and renewal scenarios. A simulated webhook
is not evidence of delivery from PayPal. Never charge real money for a test without a
separate instruction. Retain sanitized receipts privately in the project workspace.

## Security and behavior

Personal recovery keys are random and stored only as hashes on the service. Browser
sessions use Secure/HttpOnly/SameSite cookies and exact-Origin checks; native clients
use bearer keys. Rate limits are hashed by IP. No client-supplied subscription ID can
select another account. The server re-reads subscription and completed CAD payments;
approval alone grants no entitlement. Payment-event revocations survive out-of-order
events. Cancelling renewal retains the paid period. Signed RS256 access lasts at most
seven days and never past that period. Expired cancelled subscriptions can be replaced
using a new idempotency key. The companion checks signatures, issuer, environment,
features and validity; test access cannot install into the live game.

There is no password/email recovery database: users must keep their personal key.
Do not promise automatic account recovery if it is lost. Client Lua is open source and
editable, not tamperproof DRM. PayPal handles payment details; no card data is stored.

References: https://developer.paypal.com/api/subscriptions/v1 and
https://developers.cloudflare.com/durable-objects/api/sqlite-storage-api/

## In-addon access (1.12.13)

ADDON_ACCESS_ENABLED=true enables POST /api/addon-license and the portal receipt
UI. It also satisfies the live activation prerequisite without COMPANION_URL;
LIVE_VERIFIED and BILLING_ENABLED remain required. Legacy companion APIs and
their seven-day licences are preserved. Never remove existing credentials or DB.

FPA1 receipts are fixed-format RSA-2048/SHA-256 signed payloads, with a pinned
public key in Access.lua. No secret is shipped. The server rechecks payment,
refunds, account ownership and paid-period expiry before issuing a receipt.
Receipt validity ends at the paid period and is capped at 32 days. The addon
cannot query revocation or renewals: users import a new code after each paid
renewal; refunds stop new issuance but cannot revoke an imported code offline.
Code sharing and modification of GPL client code are not tamperproof DRM.

Publish the matching addon before enabling the portal flow. Keep the current
public signing key until all receipts using it have expired. Do not replace
production keys with test keys. Test keys exist only in isolated test processes.
