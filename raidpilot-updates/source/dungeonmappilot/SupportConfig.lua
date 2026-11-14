-- This PayPal.Me creator destination was explicitly provided by the addon owner.
local addonName,namespace=...
local A=addonName=="ForeverPilot_Leveling" and _G.ForeverPilotLeveling
    or addonName=="DungeonMapPilot" and _G.DungeonMapPilot or namespace
if type(A)=="table" then A.SupportConfig={creatorURL="https://paypal.me/AddonWow",confirmed=true} end
