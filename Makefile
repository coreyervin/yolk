PREFIX ?= $(HOME)/.local
APPDIR ?= /Applications
CLI := .build/release/yolk
BUILT_APP := .build/xcode/Build/Products/Release/Yolk.app
APP_PROJECT := App/Yolk.xcodeproj
SCHEME := Yolk
DERIVED := .build/xcode

.PHONY: all test cli install uninstall app install-app uninstall-app check-version clean

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

# Builds Yolk.app, embedding and signing the CLI inside it. A derived-data path
# is required: without one the package products and the app land in separate
# build roots and the app cannot find YolkAppKit.
app:
	xcodebuild -project $(APP_PROJECT) -scheme $(SCHEME) \
		-configuration Release -derivedDataPath $(DERIVED) build

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

clean:
	rm -rf .build
