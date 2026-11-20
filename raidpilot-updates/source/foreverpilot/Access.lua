-- Offline verification only. Public RSA key; no PayPal or signing secrets in Lua.
-- GPL-3.0-or-later. FPA1 is a fixed-format RSASSA-PKCS1-v1_5/SHA-256 receipt.
local B=ForeverPilotLeveling
local A={};B.Access=A
local floor,char,byte=math.floor,string.char,string.byte
local alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
local function decode(s)
    if type(s)~="string" or s:find("[^%w_-]") or #s%4==1 then return end
    local out,acc,bits={},0,0
    for i=1,#s do
        local pos=alphabet:find(s:sub(i,i),1,true);if not pos then return end
        acc=acc*64+pos-1;bits=bits+6
        if bits>=8 then bits=bits-8;out[#out+1]=char(floor(acc/2^bits));acc=acc%2^bits end
    end
    if acc~=0 then return end -- reject noncanonical trailing bits
    return table.concat(out)
end
local function u32be(n)
    return char(floor(n/16777216)%256,floor(n/65536)%256,floor(n/256)%256,n%256)
end
local K={0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2}
local function sha256(s)
    local bitlib=bit or bit32;if not bitlib then return end
    local band,bxor,bnot,rshift=bitlib.band,bitlib.bxor,bitlib.bnot,bitlib.rshift
    local rotate=bitlib.ror or bitlib.rrotate;if not rotate then return end
    local mod=4294967296.0
    local h={0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19}
    local length=#s*8;s=s..char(128)..string.rep(char(0),(55-#s)%64)..u32be(0)..u32be(length)
    for offset=1,#s,64 do
        local w={}
        for i=0,15 do local p=offset+i*4;w[i]=byte(s,p)*16777216.0+byte(s,p+1)*65536+byte(s,p+2)*256+byte(s,p+3) end
        for i=16,63 do
            local x,y=w[i-15],w[i-2]
            w[i]=(w[i-16]+bxor(rotate(x,7),rotate(x,18),rshift(x,3))+w[i-7]+bxor(rotate(y,17),rotate(y,19),rshift(y,10)))%mod
        end
        local a,b,c,d,e,f,g,z=unpack(h)
        for i=0,63 do
            local t1=(z+bxor(rotate(e,6),rotate(e,11),rotate(e,25))+bxor(band(e,f),band(bnot(e),g))+K[i+1]+w[i])%mod
            local t2=(bxor(rotate(a,2),rotate(a,13),rotate(a,22))+bxor(band(a,b),band(a,c),band(b,c)))%mod
            z,g,f,e,d,c,b,a=g,f,e,(d+t1)%mod,c,b,a,(t1+t2)%mod
        end
        local v={a,b,c,d,e,f,g,z};for i=1,8 do h[i]=(h[i]+v[i])%mod end
    end
    local out={};for i=1,8 do out[i]=u32be(h[i]) end;return table.concat(out)
end
-- Base 2^16 Montgomery arithmetic: intermediates remain below 2^34, exact in
-- Lua 5.1 doubles. Only the fixed public exponent 65537 is supported.
local BASE,N=65536.0,128
local function limbs(s)
    local a={};for i=1,N do local p=#s-2*i+1;a[i]=byte(s,p)*256.0+byte(s,p+1) end;return a
end
local function ge(a,b)
    if (a[N+1] or 0)>0 then return true end
    for i=N,1,-1 do if a[i]~=b[i] then return a[i]>b[i] end end;return true
end
local function subtract(a,b)
    local borrow=0
    for i=1,N do local v=a[i]-b[i]-borrow;borrow=v<0 and 1 or 0;a[i]=v%BASE end
    a[N+1]=(a[N+1] or 0)-borrow
end
local function double(a,n)
    local carry=0
    for i=1,N do local v=a[i]*2+carry;a[i]=v%BASE;carry=floor(v/BASE) end
    a[N+1]=carry;if ge(a,n) then subtract(a,n) end
end
local function mont(a,b,key)
    local n,np=key.n,key.np;local t={};for i=1,N+2 do t[i]=0.0 end
    for i=1,N do
        local carry=0.0
        for j=1,N do local v=t[j]+a[j]*b[i]+carry;t[j]=v%BASE;carry=floor(v/BASE) end
        local v=t[N+1]+carry;t[N+1]=v%BASE;t[N+2]=floor(v/BASE)
        local m=t[1]*np%BASE;carry=0.0
        for j=1,N do v=t[j]+m*n[j]+carry;if j>1 then t[j-1]=v%BASE end;carry=floor(v/BASE) end
        v=t[N+1]+carry;t[N]=v%BASE;t[N+1]=t[N+2]+floor(v/BASE);t[N+2]=0
    end
    if ge(t,n) then subtract(t,n) end;return t
end
local keyCache={}
local function publicKey(encoded)
    if keyCache[encoded] then return keyCache[encoded] end
    local s=decode(encoded);if not s or #s~=256 or byte(s,1)<128 or byte(s,256)%2~=1 then return end
    local n=limbs(s);local t,newt,r,newr=0,1,BASE,n[1]
    while newr~=0 do local q=floor(r/newr);t,newt=newt,t-q*newt;r,newr=newr,r-q*newr end
    if r~=1 then return end
    local key={n=n,np=(-t)%BASE,r2={}}
    for i=1,N do key.r2[i]=i==1 and 1.0 or 0.0 end
    for i=1,N*32 do double(key.r2,n) end
    keyCache[encoded]=key;return key
end
local digestPrefix=char(48,49,48,13,6,9,96,134,72,1,101,3,4,2,1,5,0,4,32)
local function verifySignature(message,signature,encoded)
    local key=publicKey(encoded);local digest=sha256(message)
    if not key or not digest or #signature~=256 then return false end
    local s=limbs(signature);if ge(s,key.n) then return false end
    local original=mont(s,key.r2,key);local result=original
    for i=1,16 do result=mont(result,result,key) end
    result=mont(result,original,key)
    local one={};for i=1,N do one[i]=i==1 and 1.0 or 0.0 end
    result=mont(result,one,key)
    local bytes={};for i=N,1,-1 do bytes[#bytes+1]=char(floor(result[i]/256),result[i]%256) end
    local expected=char(0,1)..string.rep(char(255),202)..char(0)..digestPrefix..digest
    return table.concat(bytes)==expected
end
local publicKeys={
    ["fp-live-v1"]="2gfk1P7eqywG72XDqUlWsB9iq5_Vrbw5ao6sRGQNeXMSiZIiCJcmis5C7QswyLGw86nw00zCTRpKU2B1mgrgQSN08eTLlNy5l1wzVbNXapyKg1t6TeIVigM0UikOb3LAjb-zq_4XScuoJGctane9r7xEpzdco-qw2f9qKNYcn996k5uabEqJQ8m5vNUGxg-rKwWMh6-4Nkw-ebAd1TFJaI308Q-0DQ47lf5w9CoB-r-TUu7ZWWCyIAi2LIsELDKoF-xttN3KfEb_G8TgtZxGCoy7CdoDvLRwvkcGGCUu9W9P2ZiTVc19u9w1kBSSfRS3W3P-FNrwGrr-jE9jr1j7rQ",
}
function A:Now() return B.Call(GetServerTime) or B.Call(time) end
function A:Verify(code)
    if type(code)~="string" or #code>2048 then return nil,"invalid" end
    code=code:gsub("%s","")
    local data,sig=code:match("^FPA1%.([%w_-]+)%.([%w_-]+)$")
    if not data then return nil,"invalid" end
    local raw,signature=decode(data),decode(sig);if not raw or not signature then return nil,"invalid" end
    local kid,env,issuer,id,issued,expiry,features=raw:match("^([^|]+)|([^|]+)|([^|]+)|([^|]+)|(%d+)|(%d+)|([^|]+)$")
    if env~="live" or issuer~="https://foreverpilot-subscriptions.tyronixstudio.workers.dev" or not publicKeys[kid]
        or not id or #id~=36 or id:find("[^%x%-]") or features~="fast_xp,class_guidance" then return nil,"invalid" end
    issued,expiry=tonumber(issued),tonumber(expiry);local now=self:Now()
    if not issued or not expiry or not now or expiry<=issued or expiry-issued>32*86400 or issued>now+300 then return nil,"invalid" end
    if expiry<=now then return nil,"expired" end
    if not verifySignature("FPA1."..data,signature,publicKeys[kid]) then return nil,"invalid" end
    return {environment="live",expires=expiry,fastXP=true,classGuidance=true},nil,code
end
function A:Current()
    local code=B.db and B.db.addonAccessCode;if type(code)~="string" then return end
    if code~=self.checked then
        if B.Call(InCombatLockdown)==true then return end
        self.checked=code;self.access=self:Verify(code)
    end
    local now=self:Now();if self.access and now and self.access.expires>now then return self.access end
end
function A:Import(code)
    if B.Call(InCombatLockdown)==true then return false,"combat" end
    local access,reason,clean=self:Verify(code)
    if not access then return false,reason end
    local existing=self:Current()
    if existing and existing.expires>access.expires then return false,"older" end
    if not B.db then return false,"invalid" end
    B.db.addonAccessCode=clean;self.checked=clean;self.access=access
    B.dirty=true
    if B.SetEnabled then B.SetEnabled(B.settings.enabled~=false) end
    if B.Update then B.Guard(B.Update) end
    return true
end
