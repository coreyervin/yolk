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
xcodebuild -version        # 15 or newer
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
6. assemble `dist/Yolk-X.Y.Z.dmg`
7. notarize the **DMG**, staple it
8. `spctl` assessment, as Gatekeeper would do it

Both the app and the DMG are notarized on purpose. Stapling only the DMG leaves
the app itself without a ticket, so its first launch after being dragged out
needs a network round trip. Expect two Apple round trips, a few minutes each.

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

Paste that into the tap at `~/Code/homebrew-tap/Casks/yolk.rb`, then:

```sh
cd ~/Code/homebrew-tap
git add Casks/yolk.rb && git commit -m "Add yolk cask X.Y.Z" && git push
brew update && brew install --cask coreyervin/tap/yolk     # verify for real
```

**Decide what happens to the formula.** The tap currently ships
`Formula/yolk.rb`, which builds the CLI from source. Once the cask exists, a
formula and a cask of the same name in one tap compete:
`brew install coreyervin/tap/yolk` resolves to the formula, and the cask needs
`--cask` to disambiguate.

The design spec calls for the cask to win, because it installs the app *and*
symlinks the embedded CLI onto `PATH` in one step, already notarized. Removing
`Formula/yolk.rb` makes `brew install --cask coreyervin/tap/yolk` the single
documented path, with `make install` still there for anyone who wants only the
CLI from source.

The alternative is keeping both and living with the `--cask` flag. Worth a
moment's thought rather than a default.

### 5. Point the README at it

Replace the "CLI, via Homebrew" and "The app" sections in `README.md` with the
cask, and delete the "Not yet notarized" warning — it stops being true.

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
during ordinary development. See decision 22 in `HANDOFF.md` for how to
recreate it.
