#!/usr/bin/env zsh

# Set the source version, then build a distributable release.
emulate -LR zsh
set -euo pipefail

PROJECT_DIR="${0:A:h}"
PROJECT_FILE="$PROJECT_DIR/HarvestLedger.csproj"
PACKAGE_NAME="HarvestLedger"
OUTPUT_DIR="${OUTPUT_DIR:-$PROJECT_DIR/releases}"
DOTNET_COMMAND="${DOTNET_COMMAND:-dotnet}"
HARVEST_TARGETING_PACK_ROOT="${HARVEST_TARGETING_PACK_ROOT:-}"

if [[ -z "$HARVEST_TARGETING_PACK_ROOT" && -d /opt/homebrew/opt/dotnet@6/libexec/packs/Microsoft.NETCore.App.Ref ]]; then
    HARVEST_TARGETING_PACK_ROOT="/opt/homebrew/opt/dotnet@6/libexec/packs"
fi

if [[ ! -f "$PROJECT_FILE" ]]; then
    print -u2 -- "Could not find HarvestLedger.csproj next to this script."
    exit 1
fi

print -n -- "Release version (for example, 0.4.5): "
read -r RELEASE_VERSION

if [[ ! "$RELEASE_VERSION" =~ '^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$' ]]; then
    print -u2 -- "Version must look like 0.4.5 or 0.4.5-beta.1."
    exit 1
fi

for REQUIRED_COMMAND in "$DOTNET_COMMAND" rsync zip unzip perl; do
    if ! command -v "$REQUIRED_COMMAND" >/dev/null 2>&1; then
        print -u2 -- "Required command not found: $REQUIRED_COMMAND"
        exit 1
    fi
done

mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR="${OUTPUT_DIR:A}"
ARCHIVE_PATH="$OUTPUT_DIR/$PACKAGE_NAME $RELEASE_VERSION.zip"

if [[ -e "$ARCHIVE_PATH" ]]; then
    print -u2 -- "Release archive already exists: $ARCHIVE_PATH"
    print -u2 -- "Choose a new version or move the existing archive first."
    exit 1
fi

BUILD_OPTIONS=(-p:Version="$RELEASE_VERSION" -p:EnableModZip=false)
if [[ -n "$HARVEST_TARGETING_PACK_ROOT" ]]; then
    BUILD_OPTIONS+=("-p:NetCoreTargetingPackRoot=$HARVEST_TARGETING_PACK_ROOT")
fi

print -- "Building $PACKAGE_NAME $RELEASE_VERSION..."
"$DOTNET_COMMAND" build "$PROJECT_FILE" -c Release --no-restore "${BUILD_OPTIONS[@]}"

ASSEMBLY_PATH="$PROJECT_DIR/bin/Release/net6.0/$PACKAGE_NAME.dll"
if [[ ! -f "$ASSEMBLY_PATH" ]]; then
    print -u2 -- "Build completed, but the expected DLL was not found: $ASSEMBLY_PATH"
    exit 1
fi

RELEASE_VERSION="$RELEASE_VERSION" perl -0pi -e 's#<Version>[^<]+</Version>#<Version>$ENV{RELEASE_VERSION}</Version>#' "$PROJECT_FILE"
RELEASE_VERSION="$RELEASE_VERSION" perl -0pi -e 's/("Version"\s*:\s*")[^"]+"/$1$ENV{RELEASE_VERSION}"/' "$PROJECT_DIR/manifest.json"
RELEASE_VERSION="$RELEASE_VERSION" perl -0pi -e 's/(Harvest ?Ledger )[0-9]+\.[0-9]+\.[0-9]+(?:[-.][0-9A-Za-z.-]+)?/$1$ENV{RELEASE_VERSION}/g' "$PROJECT_DIR/README.md"

if ! grep -Fq "<Version>$RELEASE_VERSION</Version>" "$PROJECT_FILE" \
    || ! grep -Fq "\"Version\": \"$RELEASE_VERSION\"" "$PROJECT_DIR/manifest.json"; then
    print -u2 -- "Failed to update the source version files."
    exit 1
fi

STAGING_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/harvest-ledger-release.XXXXXX")"
trap 'rm -rf "$STAGING_ROOT"' EXIT
PACKAGE_ROOT="$STAGING_ROOT/$PACKAGE_NAME"
mkdir -p "$PACKAGE_ROOT"

cp "$ASSEMBLY_PATH" "$PACKAGE_ROOT/$PACKAGE_NAME.dll"
cp "$PROJECT_DIR/manifest.json" "$PACKAGE_ROOT/manifest.json"
cp "$PROJECT_DIR/config.json" "$PACKAGE_ROOT/config.json"
cp "$PROJECT_DIR/README.md" "$PACKAGE_ROOT/README.md"
rsync -a --exclude '.DS_Store' "$PROJECT_DIR/i18n/" "$PACKAGE_ROOT/i18n/"
rsync -a --exclude '.DS_Store' "$PROJECT_DIR/icon/" "$PACKAGE_ROOT/icon/"

(
    cd "$STAGING_ROOT"
    zip -qry "$ARCHIVE_PATH" "$PACKAGE_NAME"
)

ARCHIVE_CONTENTS="$STAGING_ROOT/archive-contents.txt"
unzip -Z1 "$ARCHIVE_PATH" > "$ARCHIVE_CONTENTS"

for REQUIRED_FILE in \
    "$PACKAGE_NAME/manifest.json" \
    "$PACKAGE_NAME/$PACKAGE_NAME.dll" \
    "$PACKAGE_NAME/config.json" \
    "$PACKAGE_NAME/README.md" \
    "$PACKAGE_NAME/i18n/default.json" \
    "$PACKAGE_NAME/i18n/zh.json"; do
    if ! grep -Fxq "$REQUIRED_FILE" "$ARCHIVE_CONTENTS"; then
        print -u2 -- "Package verification failed: $REQUIRED_FILE is missing."
        exit 1
    fi
done

print -- "Created: $ARCHIVE_PATH"
print -- "Updated the source version and created the release archive."
