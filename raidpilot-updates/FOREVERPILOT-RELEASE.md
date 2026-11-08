# ForeverPilot release contract

Owner instruction, 2026-11-08: every ForeverPilot update is delivered through
CurseForge and the launcher. The RaidPilot launcher is the testing edition;
all ForeverPilot features there remain free, including Fast XP and class/spec
guidance. No subscription, online entitlement or trial expiry may block that
edition. Preserve this when implementing future public subscriptions.

1. Run the addon and launcher checks, increment both versions and release notes.
2. Stable CI packages the launcher and a standalone ForeverPilot ZIP from the
   same source commit. The standalone ZIP includes GPL licensing and excludes
   personal data, the Windows launcher, Ding and the legacy telemetry bridge.
3. Publish the launcher to the Stable manifest and verify its downloaded bundle.
4. Upload the same addon version to CurseForge project **1791754**. Supported game
   version: **WoW Forever 1.60.1**; required dependency: **Ding**, project **1739053**;
   optional dependency: **ChangeLayer**, project **1751627**. Keep release notes FR/EN.
5. Record the returned CurseForge file ID and moderation status. A prepared ZIP
   or successful launcher publication does not mean the CurseForge upload is done.
   Moderation approval is separate from uploading. Do not claim both channels
   complete until the launcher is published and the CurseForge upload is verified.

The CurseForge upload is currently performed through the signed-in author portal.
No upload token has been configured in GitHub. Never describe this step as
automatic until a securely stored token and a verified upload workflow exist.

The public CAD 9.99/month subscription is still in preparation. No payment gate
is active in v1.8.1. Do not activate billing before payment verification and
activation work end to end. The RaidPilot test edition remains free afterward.
