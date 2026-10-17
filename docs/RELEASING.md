# Releasing Yolk

How to cut a signed, notarized release. Everything here runs on **one machine**
— the one holding the Developer ID certificate. Nothing needs to be copied
between Macs.

**Use the same machine every time.** Apps signed by different certificates have
different designated requirements, so shipping release *n* from one Mac and
release *n+1* from another silently resets every user's Accessibility grant.
One certificate, one release machine.

---

## One-time setup

### 1. Prerequisites

```sh
xcode-select -p            # full Xcode, not just Command Line Tools
xcodebuild -version        # 16 or newer (Package.swift is swift-tools 6.0)
git clone https://github.com/coreyervin/yolk.git
cd yolk
make test                  # 129 tests — confirms the clone is sound
```

`make test` and `make cli` work without any certificate. Only releasing needs one.

### 2. Developer ID Application certificate

Xcode → Settings → Accounts → select the team → **Manage Certificates…** → **+**
→ **Developer ID Application**.

- Not "Apple Development" and not "Developer ID Installer" — neither can sign
  an app for distribution.
- On an organization account this is often restricted to the Account Holder.
- Apple allows only a small number of these per account. Don't create one
  casually on each machine; see the warning at the top.

Confirm it landed, and that the build system can see it:

```sh
security find-identity -v -p codesigning     # should list "Developer ID Application: …"
make signing                                 # should report that identity, plus a team id
```

`make signing` is the real check. It prints exactly what a build here would use.
If it still says `Yolk Local Development` or `-`, the certificate isn't usable
yet and nothing below will work.

### 3. Notarization credentials

Apple notarizes via an App Store Connect API key.

1. appstoreconnect.apple.com → **Users and Access** → **Integrations** → **+**
2. Give it the *Developer* role, create it, and **download the `.p8`** — it is
   downloadable exactly once and cannot be recovered.
3. Note the **Key ID** and the **Issuer UUID** from that page.

Store them in the keychain under the profile name the Makefile expects:

```sh
xcrun notarytool store-credentials "yolk-notary" \
  --key ~/path/to/AuthKey_XXXXXXXX.p8 \
  --key-id XXXXXXXXXX \
  --issuer xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

Verify, then put the `.p8` somewhere safe — a password manager, not the repo:

```sh
xcrun notarytool history --keychain-profile "yolk-notary"
```

Treat the `.p8` like a password. It can act on your developer account, not just
sign builds.

---

## Cutting a release

### 1. Bump the version

Nine places carry it. `make check-version` catches the two that matter most and
the test suite catches the goldens, so a miss fails loudly rather than shipping.

- `Sources/YolkKit/Version.swift`
- `Tests/YolkKitTests/VersionTests.swift`
- `MARKETING_VERSION`, twice, in `App/Yolk.xcodeproj/project.pbxproj`
- The first line of `Goldens/cli-help.txt`, `cli-version.txt`,
  `cli-banner-bare.txt`, `cli-banner-notip.txt`, `cli-banner-flagged.txt`

Leave `Goldens/originals/` alone — those record what the August 2026
pre-refactor binary actually printed and are historical evidence, not goldens.

```sh
make check-version && make test
git commit -am "Bump to X.Y.Z"
git tag -a vX.Y.Z -m "Yolk X.Y.Z — <what changed>"
git push origin main && git push origin vX.Y.Z
```

### 2. Build and notarize

```sh
make release
```

That runs, failing early rather than late:

1. `check-version` — version sources agree
2. `check-signing` — a Developer ID identity exists, a team id can be read from
   it, and notary credentials are stored
3. `make test`
4. `xcodebuild archive`, then export with Developer ID signing
5. notarize the **app**, staple it
6. assemble `dist/Yolk-X.Y.Z.dmg` and sign it with the Developer ID
7. notarize the **DMG**, staple it
8. `spctl` assessment, as Gatekeeper would do it

Both the app and the DMG are notarized on purpose. Stapling only the DMG leaves
the app itself without a ticket, so its first launch after being dragged out
needs a network round trip. Expect two Apple round trips, a few minutes each.

The DMG is signed too, not just the app inside it. The `spctl` check in step 8
assesses the disk image's own signature, and an unsigned DMG fails it with
`source=no usable signature` even when it is notarized and stapled.

**A team's first notarization can take hours**, not minutes — 1.0.1's app
submission sat at `In Progress` overnight. The DMG submissions after it came
back in minutes. If `make release` gets interrupted while waiting, don't rerun it:
that wipes `dist/` and joins the back of the queue. The submission keeps going
on Apple's side, so wait on it and finish by hand:

```sh
xcrun notarytool wait <submission-id> --keychain-profile "yolk-notary"
```

then carry on from the `stapler staple` line in the `release` target. The 1.0.1
wait failed overnight with a network timeout and then "No Keychain password
item found", most likely the Mac sleeping or locking; the credentials worked
again the next morning. Keep the Mac awake and unlocked while it waits.

**If notarization is rejected**, get the reason — the log is specific and
usually names the offending binary:

```sh
xcrun notarytool log <submission-id> --keychain-profile "yolk-notary"
```

The usual causes are a missing hardened runtime or an unsigned nested binary.
Both are already handled by the build, so a failure here means something
changed in the embed phase.

### 3. Publish the DMG

```sh
gh release create vX.Y.Z dist/Yolk-X.Y.Z.dmg \
  --title "Yolk X.Y.Z" --notes "…"
```

Or attach it by hand at github.com/coreyervin/yolk/releases.

### 4. Update the tap

```sh
make cask          # prints Casks/yolk.rb with the DMG's real checksum
```

Write it into the tap, then commit, push, and install it for real:

```sh
make cask > ~/Code/homebrew-tap/Casks/yolk.rb
cd ~/Code/homebrew-tap
brew style Casks/yolk.rb                                   # should be clean
git add Casks/yolk.rb && git commit -m "Update yolk to X.Y.Z" && git push
brew update && brew upgrade --cask coreyervin/tap/yolk
yolk --version                                             # should print X.Y.Z
```

The tap ships only the cask. The source-built formula was removed at 1.0.1 so
`brew install coreyervin/tap/yolk` has one meaning; anyone who wants only the
CLI builds it from a clone with `make install`.

### 5. Update the release notes

If the release's notes or the README mention a version-specific download, make
sure they still point at the new DMG. `releases/latest` links keep working on
their own.

---

## Afterwards

**Accessibility resets once.** The app's signing identity changes from the
local self-signed certificate to the Developer ID, so macOS treats it as a new
app. Remove Yolk under System Settings → Privacy & Security → Accessibility and
add it back. This is the last time: every subsequent release shares the
Developer ID's designated requirement, so grants survive updates from here on.

**Local builds change too.** `make signing` will now prefer the Developer ID
over `Yolk Local Development` on this machine. That is fine, and it means
locally built apps match released ones.

**The self-signed certificate stays useful** on any machine that lacks the
Developer ID — it is what keeps Accessibility from resetting on every rebuild
during ordinary development. Ad-hoc signing ties the grant to the binary's
cdhash, which changes on every build; a self-signed certificate makes the
designated requirement `identifier + certificate root`, which doesn't.

To recreate it if the keychain item is ever lost:

```sh
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
  -keyout k.key -out c.crt -subj "/CN=Yolk Local Development/O=Yolk" \
  -addext "basicConstraints=critical,CA:false" \
  -addext "keyUsage=critical,digitalSignature" \
  -addext "extendedKeyUsage=critical,codeSigning"
# -certpbe/-keypbe/-macalg are required: OpenSSL 3's defaults produce a
# PKCS12 that macOS refuses with "MAC verification failed".
openssl pkcs12 -export -inkey k.key -in c.crt -out c.p12 -passout pass:yolk \
  -name "Yolk Local Development" \
  -certpbe PBE-SHA1-3DES -keypbe PBE-SHA1-3DES -macalg sha1
security import c.p12 -k ~/Library/Keychains/login.keychain-db -P yolk \
  -T /usr/bin/codesign
# User-level trust is enough; no admin password needed.
security add-trusted-cert -r trustRoot -p codeSign \
  -k ~/Library/Keychains/login.keychain-db c.crt
```
