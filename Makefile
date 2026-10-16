PREFIX ?= $(HOME)/.local
APPDIR ?= /Applications
CLI := .build/release/yolk
APP_PROJECT := App/Yolk.xcodeproj
SCHEME := Yolk
DERIVED := .build/xcode
BUILT_APP := $(DERIVED)/Build/Products/Release/Yolk.app
DIST := dist

VERSION := $(shell sed -n 's/^[[:space:]]*public static let version = "\(.*\)"/\1/p' \
	Sources/YolkKit/Version.swift)

# Signing identity for the app, resolved in this order:
#
#   1. SIGN_IDENTITY= on the command line
#   2. a Developer ID Application certificate, if the keychain holds one
#   3. the self-signed "Yolk Local Development" certificate, for local work
#   4. ad-hoc, so a fresh clone still builds on a machine with neither
#
# The project itself defaults to ad-hoc, so opening it in Xcode works on any
# machine. Hardcoding a certificate name there makes every other Mac fail the
# build with "no signing certificate found".
SIGN_IDENTITY ?= $(shell \
	ids=`security find-identity -v -p codesigning 2>/dev/null`; \
	devid=`printf '%s\n' "$$ids" \
		| sed -n 's/.*"\(Developer ID Application: [^"]*\)".*/\1/p' | head -1`; \
	if [ -n "$$devid" ]; then printf '%s' "$$devid"; \
	elif printf '%s\n' "$$ids" | grep -q "Yolk Local Development"; then \
		printf 'Yolk Local Development'; \
	else printf -- '-'; fi)

# Pulled out of the identity's trailing "(TEAMID)". Override if it is wrong.
TEAM_ID ?= $(shell printf '%s' "$(SIGN_IDENTITY)" | sed -n 's/.*(\([A-Z0-9]*\))$$/\1/p')
# Keychain profile created by `xcrun notarytool store-credentials`.
NOTARY_PROFILE ?= yolk-notary

XCODEBUILD := xcodebuild -project $(APP_PROJECT) -scheme $(SCHEME) \
	-configuration Release -derivedDataPath $(DERIVED) \
	CODE_SIGN_IDENTITY="$(SIGN_IDENTITY)"

.PHONY: all test cli install uninstall app install-app uninstall-app \
	check-version check-signing signing release cask clean

all: test cli

test:
	swift test

cli:
	swift build -c release --product yolk

install: cli
	install -d $(PREFIX)/bin
	install -m 755 $(CLI) $(PREFIX)/bin/yolk
	@echo "installed $(PREFIX)/bin/yolk"

uninstall:
	rm -f $(PREFIX)/bin/yolk

# Reports what a build on this machine would sign with.
signing:
	@echo "identity: $(SIGN_IDENTITY)"
	@echo "team:     $(if $(TEAM_ID),$(TEAM_ID),(none — not a Developer ID identity))"
	@echo "version:  $(VERSION)"

# Builds Yolk.app, embedding and signing the CLI inside it. A derived-data path
# is required: without one the package products and the app land in separate
# build roots and the app cannot find YolkAppKit.
app:
	$(XCODEBUILD) build

# Installs the built app into /Applications, replacing any previous copy.
# ditto rather than cp -R: it preserves the bundle's extended attributes and
# code signature, which cp can quietly damage.
install-app: app
	@rm -rf "$(APPDIR)/Yolk.app"
	ditto "$(BUILT_APP)" "$(APPDIR)/Yolk.app"
	@codesign --verify --deep --strict "$(APPDIR)/Yolk.app" \
		&& echo "installed $(APPDIR)/Yolk.app (signature intact)"

uninstall-app:
	rm -rf "$(APPDIR)/Yolk.app"

# Runs first in the release pipeline so a version mismatch fails before
# anything is built or signed. Cannot be a SwiftPM unit test: the test target
# has no access to project.pbxproj.
check-version:
	@kit=`sed -n 's/^[[:space:]]*public static let version = "\(.*\)"/\1/p' \
		Sources/YolkKit/Version.swift`; \
	proj=`sed -n 's/^[[:space:]]*MARKETING_VERSION = \(.*\);/\1/p' \
		$(APP_PROJECT)/project.pbxproj | sort -u`; \
	if [ -z "$$kit" ]; then \
		echo "check-version: could not read YolkKit.version" >&2; exit 1; \
	fi; \
	if [ -z "$$proj" ]; then \
		echo "check-version: could not read MARKETING_VERSION" >&2; exit 1; \
	fi; \
	if [ `printf '%s\n' "$$proj" | wc -l` -ne 1 ]; then \
		echo "check-version: build configurations disagree:" >&2; \
		printf '  MARKETING_VERSION = %s\n' $$proj >&2; exit 1; \
	fi; \
	if [ "$$kit" != "$$proj" ]; then \
		echo "check-version: version mismatch" >&2; \
		echo "  YolkKit.version   = $$kit" >&2; \
		echo "  MARKETING_VERSION = $$proj" >&2; exit 1; \
	fi; \
	echo "check-version: $$kit"

# Fails before anything is built, rather than after an archive.
check-signing:
	@case "$(SIGN_IDENTITY)" in \
		"Developer ID"*) echo "check-signing: $(SIGN_IDENTITY)" ;; \
		*) echo "check-signing: a release needs a Developer ID Application certificate." >&2; \
		   echo "  resolved identity: $(SIGN_IDENTITY)" >&2; \
		   echo "  create one in Xcode > Settings > Accounts > Manage Certificates," >&2; \
		   echo "  or import a .p12 exported from the Mac that has it." >&2; \
		   exit 1 ;; \
	esac
	@if [ -z "$(TEAM_ID)" ]; then \
		echo "check-signing: could not read a team id out of the identity." >&2; \
		echo "  pass it explicitly: make release TEAM_ID=XXXXXXXXXX" >&2; exit 1; \
	fi
	@xcrun notarytool history --keychain-profile "$(NOTARY_PROFILE)" >/dev/null 2>&1 || { \
		echo "check-signing: no notary credentials for profile '$(NOTARY_PROFILE)'." >&2; \
		echo "  xcrun notarytool store-credentials \"$(NOTARY_PROFILE)\" \\" >&2; \
		echo "    --key AuthKey_XXXX.p8 --key-id KEYID --issuer ISSUER-UUID" >&2; \
		exit 1; }

# The full release: verify, build, sign, notarize, staple.
#
# The app is notarized and stapled *before* the DMG is built, then the DMG is
# notarized and stapled too. Stapling only the DMG leaves the app itself
# without a ticket, so it needs a network round trip the first time someone
# opens it after dragging it out.
release: check-version check-signing test
	@rm -rf $(DIST) && mkdir -p $(DIST)
	$(XCODEBUILD) -archivePath $(DIST)/Yolk.xcarchive archive
	@printf '%s\n' \
		'<?xml version="1.0" encoding="UTF-8"?>' \
		'<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">' \
		'<plist version="1.0"><dict>' \
		'  <key>method</key><string>developer-id</string>' \
		'  <key>teamID</key><string>$(TEAM_ID)</string>' \
		'  <key>signingStyle</key><string>manual</string>' \
		'  <key>signingCertificate</key><string>Developer ID Application</string>' \
		'</dict></plist>' > $(DIST)/ExportOptions.plist
	xcodebuild -exportArchive -archivePath $(DIST)/Yolk.xcarchive \
		-exportOptionsPlist $(DIST)/ExportOptions.plist \
		-exportPath $(DIST)/export
	codesign --verify --deep --strict --verbose=2 $(DIST)/export/Yolk.app
	@echo "==> notarizing the app"
	ditto -c -k --keepParent $(DIST)/export/Yolk.app $(DIST)/Yolk.zip
	xcrun notarytool submit $(DIST)/Yolk.zip \
		--keychain-profile "$(NOTARY_PROFILE)" --wait
	xcrun stapler staple $(DIST)/export/Yolk.app
	@echo "==> assembling the disk image"
	@mkdir -p $(DIST)/dmg
	ditto $(DIST)/export/Yolk.app $(DIST)/dmg/Yolk.app
	@ln -s /Applications $(DIST)/dmg/Applications
	hdiutil create -volname "Yolk $(VERSION)" -srcfolder $(DIST)/dmg \
		-ov -format UDZO $(DIST)/Yolk-$(VERSION).dmg
	@echo "==> notarizing the disk image"
	xcrun notarytool submit $(DIST)/Yolk-$(VERSION).dmg \
		--keychain-profile "$(NOTARY_PROFILE)" --wait
	xcrun stapler staple $(DIST)/Yolk-$(VERSION).dmg
	@echo "==> verifying as Gatekeeper would"
	spctl -a -t open --context context:primary-signature -v $(DIST)/Yolk-$(VERSION).dmg
	@echo
	@echo "released $(DIST)/Yolk-$(VERSION).dmg"
	@echo "next: attach it to the v$(VERSION) GitHub release, then \`make cask\`"

# Emits the Homebrew cask for the built DMG, with its real checksum. Paste the
# output into the tap as Casks/yolk.rb once the DMG is attached to the release.
cask:
	@test -f $(DIST)/Yolk-$(VERSION).dmg \
		|| { echo "no $(DIST)/Yolk-$(VERSION).dmg — run 'make release' first" >&2; exit 1; }
	@sha=`shasum -a 256 $(DIST)/Yolk-$(VERSION).dmg | awk '{print $$1}'`; \
	printf '%s\n' \
		'cask "yolk" do' \
		'  version "$(VERSION)"' \
		"  sha256 \"$$sha\"" \
		'' \
		'  url "https://github.com/coreyervin/yolk/releases/download/v#{version}/Yolk-#{version}.dmg"' \
		'  name "Yolk"' \
		'  desc "Keep your Mac awake and Slack active while AI agents run"' \
		'  homepage "https://github.com/coreyervin/yolk"' \
		'' \
		'  depends_on macos: ">= :sonoma"' \
		'' \
		'  app "Yolk.app"' \
		'  # The app embeds the CLI; link it onto PATH rather than shipping it twice.' \
		'  binary "#{appdir}/Yolk.app/Contents/Resources/yolk"' \
		'' \
		'  zap trash: [' \
		'    "~/Library/Preferences/io.github.coreyervin.yolk.plist",' \
		'  ]' \
		'end'

clean:
	rm -rf .build $(DIST)
