#!/bin/bash

set -euo pipefail

INSTALL_DIR="$HOME/.local/share/uprooted"
PROFILE_DIR="$HOME/.local/share/Root Communications/Root/profile/default"
PROFILER_GUID="{D1A6F5A0-1234-4567-89AB-CDEF01234567}"
VERSION="0.1.95"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log()   { echo -e "${GREEN}[+]${NC} $1"; }
warn()  { echo -e "${YELLOW}[!]${NC} $1"; }
error() { echo -e "${RED}[-]${NC} $1"; }
die()   { error "$1"; exit 1; }

ROOT_PATH=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --root-path) ROOT_PATH="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: $0 [--root-path /path/to/Root.AppImage]"
            echo ""
            echo "Installs Uprooted client mod framework for Root Communications."
            echo ""
            echo "Options:"
            echo "  --root-path    Path to Root.AppImage (auto-detected if not given)"
            echo "  --help         Show this help"
            exit 0
            ;;
        *) die "Unknown option: $1" ;;
    esac
done

find_root() {
    if [[ -n "$ROOT_PATH" ]]; then
        if [[ -f "$ROOT_PATH" ]]; then
            log "Using Root at: $ROOT_PATH"
            return 0
        else
            die "Root not found at: $ROOT_PATH"
        fi
    fi

    local candidates=(
        "$HOME/Applications/Root.AppImage"
        "$HOME/Downloads/Root.AppImage"
        "$HOME/.local/bin/Root.AppImage"
        "/opt/Root.AppImage"
        "$HOME/.local/bin/Root"
    )

    for c in "${candidates[@]}"; do
        if [[ -f "$c" ]]; then
            ROOT_PATH="$c"
            log "Found Root at: $ROOT_PATH"
            return 0
        fi
    done

    if command -v Root &>/dev/null; then
        ROOT_PATH="$(which Root)"
        log "Found Root in PATH: $ROOT_PATH"
        return 0
    fi

    die "Could not find Root.AppImage. Use --root-path to specify its location."
}

check_prereqs() {
    local missing=()

    if ! command -v gcc &>/dev/null; then
        missing+=("gcc")
    fi
    if ! command -v dotnet &>/dev/null; then
        missing+=("dotnet-sdk-10.0")
    fi
    if ! command -v node &>/dev/null; then
        missing+=("nodejs")
    fi
    if ! command -v pnpm &>/dev/null; then
        missing+=("pnpm")
    fi

    if [[ ${#missing[@]} -gt 0 ]]; then
        error "Missing build dependencies: ${missing[*]}"
        echo "Install them and try again."
        echo ""
        echo "Ubuntu/Debian:"
        echo "  sudo apt install gcc nodejs"
        echo "  # Install .NET 10: https://dotnet.microsoft.com/download"
        echo "  # Install pnpm: npm install -g pnpm"
        exit 1
    fi
}

build_artifacts() {
    local script_dir
    script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

    log "Building artifacts from source..."

    log "Building TypeScript layer..."
    (cd "$script_dir" && pnpm install --frozen-lockfile && pnpm build)

    log "Building UprootedHook.dll..."
    dotnet build "$script_dir/hook" -c Release -o "$script_dir/hook/_out"

    log "Compiling libuprooted_profiler.so..."
    gcc -shared -fPIC -O2 -o "$script_dir/libuprooted_profiler.so" "$script_dir/tools/uprooted_profiler_linux.c"

    mkdir -p "$INSTALL_DIR"

    cp "$script_dir/libuprooted_profiler.so" "$INSTALL_DIR/"
    cp "$script_dir/hook/_out/UprootedHook.dll" "$INSTALL_DIR/"
    cp "$script_dir/hook/_out/UprootedHook.deps.json" "$INSTALL_DIR/"
    cp "$script_dir/dist/uprooted-preload.js" "$INSTALL_DIR/"
    cp "$script_dir/dist/uprooted.css" "$INSTALL_DIR/"

    chmod +x "$INSTALL_DIR/libuprooted_profiler.so"

    log "Artifacts deployed to $INSTALL_DIR"
}

set_env_vars() {
    local env_dir="$HOME/.config/environment.d"
    mkdir -p "$env_dir"

    cat > "$env_dir/uprooted.conf" << ENVCONF
CORECLR_ENABLE_PROFILING=1
CORECLR_PROFILER=$PROFILER_GUID
CORECLR_PROFILER_PATH=$INSTALL_DIR/libuprooted_profiler.so
DOTNET_ReadyToRun=0
ENVCONF
    log "Session env vars written to $env_dir/uprooted.conf"
    warn "Log out and back in (or reboot) for env vars to take effect globally."
    warn "Or use the wrapper script below for immediate use."
}

create_wrapper() {
    local wrapper="$INSTALL_DIR/launch-root.sh"
    cat > "$wrapper" << WRAPPER
#!/bin/bash
export CORECLR_ENABLE_PROFILING=1
export CORECLR_PROFILER='$PROFILER_GUID'
export CORECLR_PROFILER_PATH='$INSTALL_DIR/libuprooted_profiler.so'
export DOTNET_ReadyToRun=0
exec '$ROOT_PATH' "\$@"
WRAPPER
    chmod +x "$wrapper"
    log "Wrapper script created: $wrapper"
}

create_desktop_file() {
    local apps_dir="$HOME/.local/share/applications"
    mkdir -p "$apps_dir"

    cat > "$apps_dir/root-uprooted.desktop" << DESKTOP
[Desktop Entry]
Name=Root (Uprooted)
Comment=Root Communications with Uprooted mods
Exec=$INSTALL_DIR/launch-root.sh
Type=Application
Categories=Network;Chat;
Terminal=false
DESKTOP
    chmod +x "$apps_dir/root-uprooted.desktop"
    log ".desktop file created"
}

patch_html() {
    if [[ ! -d "$PROFILE_DIR" ]]; then
        warn "Profile directory not found: $PROFILE_DIR"
        warn "Launch Root once to generate it, then re-run this script."
        return
    fi

    local patched=0
    local js_path="$INSTALL_DIR/uprooted-preload.js"
    local css_path="$INSTALL_DIR/uprooted.css"

    local html_files=()
    if [[ -f "$PROFILE_DIR/WebRtcBundle/index.html" ]]; then
        html_files+=("$PROFILE_DIR/WebRtcBundle/index.html")
    fi
    for app_dir in "$PROFILE_DIR/RootApps"/*/; do
        if [[ -f "${app_dir}index.html" ]]; then
            html_files+=("${app_dir}index.html")
        fi
    done

    if [[ ${#html_files[@]} -eq 0 ]]; then
        warn "No HTML files found to patch."
        warn "Launch Root once, then re-run this script."
        return
    fi

    local script_tag="<script src=\"file://$js_path\"></script>"
    local css_tag="<link rel=\"stylesheet\" href=\"file://$css_path\">"

    for html in "${html_files[@]}"; do
        if grep -q "uprooted-preload" "$html" 2>/dev/null; then
            log "Already patched: $(basename "$(dirname "$html")")/index.html"
            continue
        fi

        cp "$html" "${html}.uprooted-backup"

        sed -i "s|</head>|${css_tag}\n${script_tag}\n</head>|" "$html"
        patched=$((patched + 1))
        log "Patched: $(basename "$(dirname "$html")")/index.html"
    done

    log "$patched HTML file(s) patched"
}

echo ""
echo "  Uprooted Linux Installer v$VERSION"
echo "  ─────────────────────────────────"
echo ""

find_root
check_prereqs
build_artifacts
set_env_vars
create_wrapper
create_desktop_file
patch_html

echo ""
log "Installation complete!"
log ""
log "To activate Uprooted, either:"
log "  1. Log out and back in, then launch Root normally"
log "  2. Run: $INSTALL_DIR/launch-root.sh"
log "  3. Find 'Root (Uprooted)' in your application menu"
echo ""
