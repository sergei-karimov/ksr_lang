#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  KSR language toolchain installer  (Linux / macOS)
#
#  Usage:
#    ./install.sh              install everything
#    ./install.sh --no-vscode  skip VS Code extension
#    ./install.sh --uninstall  remove KSR
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# ── colours ───────────────────────────────────────────────────────────────────
CYAN='\033[0;36m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'

step()  { echo -e "${CYAN}  » $*${NC}"; }
ok()    { echo -e "${GREEN}  ✓ $*${NC}"; }
warn()  { echo -e "${YELLOW}  ! $*${NC}"; }
fail()  { echo -e "${RED}  ✗ $*${NC}"; exit 1; }

# ── args ──────────────────────────────────────────────────────────────────────
SKIP_VSCODE=0
UNINSTALL=0

for arg in "$@"; do
    case $arg in
        --no-vscode) SKIP_VSCODE=1 ;;
        --uninstall) UNINSTALL=1   ;;
        *) echo "Unknown argument: $arg"; exit 1 ;;
    esac
done

# ── locate repo root (parent of this script's directory) ─────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS="$REPO_ROOT/artifacts"

# ── uninstall ─────────────────────────────────────────────────────────────────
if [[ $UNINSTALL -eq 1 ]]; then
    echo ""
    echo -e "${CYAN}Uninstalling KSR...${NC}"
    echo ""

    step "Removing ksr global tool"
    dotnet tool uninstall -g KSR 2>/dev/null && ok "ksr tool removed" || warn "ksr tool was not installed"

    step "Removing KSR.Templates"
    dotnet new uninstall KSR.Templates 2>/dev/null && ok "KSR.Templates removed" || warn "KSR.Templates were not installed"

    step "Removing VS Code extension"
    code --uninstall-extension ksr-lang 2>/dev/null && ok "VS Code extension removed" || warn "VS Code extension was not installed (or 'code' not found)"

    echo ""
    echo -e "${GREEN}KSR uninstalled.${NC}"
    exit 0
fi

# ── install ───────────────────────────────────────────────────────────────────
echo ""
echo -e "${CYAN}Installing KSR language toolchain...${NC}"
echo ""

# ── 1. Check .NET SDK ─────────────────────────────────────────────────────────
step "Checking .NET SDK"
if ! command -v dotnet &>/dev/null; then
    fail ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
fi

DOTNET_VERSION=$(dotnet --version)
MAJOR="${DOTNET_VERSION%%.*}"
if [[ $MAJOR -lt 8 ]]; then
    fail ".NET 8 or later is required (found $DOTNET_VERSION). Install from https://dotnet.microsoft.com/download"
fi
ok ".NET $DOTNET_VERSION"

# ── 2. Build NuGet packages ───────────────────────────────────────────────────
mkdir -p "$ARTIFACTS"

step "Building KSR.Core"
dotnet pack "$REPO_ROOT/KSR.Core.csproj" -c Release -o "$ARTIFACTS" -v q --nologo
ok "KSR.Core packed"

step "Building KSR.Build"
dotnet pack "$REPO_ROOT/sdk/KSR.Build/KSR.Build.csproj" -c Release -o "$ARTIFACTS" -v q --nologo
ok "KSR.Build packed"

step "Building KSR.Sdk"
dotnet pack "$REPO_ROOT/sdk/KSR.Sdk/KSR.Sdk.csproj" -c Release -o "$ARTIFACTS" -v q --nologo
ok "KSR.Sdk packed"

step "Building KSR.StdLib"
dotnet pack "$REPO_ROOT/sdk/KSR.StdLib/KSR.StdLib.csproj" -c Release -o "$ARTIFACTS" -v q --nologo
ok "KSR.StdLib packed"

step "Building KSR.Templates"
dotnet pack "$REPO_ROOT/sdk/KSR.Templates/KSR.Templates.csproj" -c Release -o "$ARTIFACTS" -v q --nologo
ok "KSR.Templates packed"

step "Building KSR CLI"
dotnet pack "$REPO_ROOT/KSR.csproj" -c Release -o "$ARTIFACTS" -v q --nologo
ok "KSR CLI packed"

# ── 3. Register local NuGet feed ──────────────────────────────────────────────
step "Registering local NuGet feed"
FEED_NAME="ksr-local"
if dotnet nuget list source | grep -q "$FEED_NAME"; then
    dotnet nuget update source "$FEED_NAME" --source "$ARTIFACTS" >/dev/null
else
    dotnet nuget add source "$ARTIFACTS" --name "$FEED_NAME" >/dev/null
fi
ok "Feed '$FEED_NAME' → $ARTIFACTS"

# ── 4. Install ksr global tool ────────────────────────────────────────────────
step "Installing ksr global tool"
# Remove all KSR packages from NuGet cache so fresh local builds are always used
for pkg in ksr ksr.core ksr.build ksr.sdk ksr.stdlib ksr.templates; do
    rm -rf "$HOME/.nuget/packages/$pkg/0.1.0" 2>/dev/null || true
done
dotnet tool uninstall -g KSR 2>/dev/null || true
dotnet tool install -g KSR --add-source "$ARTIFACTS" --version 0.1.0
ok "ksr tool installed"

# Remind user to add ~/.dotnet/tools to PATH if needed
TOOLS_PATH="$HOME/.dotnet/tools"
if [[ ":$PATH:" != *":$TOOLS_PATH:"* ]]; then
    warn "Add ~/.dotnet/tools to your PATH to use 'ksr' anywhere:"
    warn "  echo 'export PATH=\"\$HOME/.dotnet/tools:\$PATH\"' >> ~/.bashrc"
    warn "  source ~/.bashrc"
fi

# ── 5. Install dotnet new templates ──────────────────────────────────────────
step "Installing dotnet new templates"
dotnet new uninstall KSR.Templates 2>/dev/null || true
dotnet new install "$ARTIFACTS/KSR.Templates.0.1.0.nupkg"
ok "KSR templates installed  (dotnet new ksr-console)"

# ── 6. Build & install VS Code extension (optional) ─────────────────────────
if [[ $SKIP_VSCODE -eq 0 ]]; then
    VSIX_DIR="$REPO_ROOT/vscode-extension"
    VSIX="$VSIX_DIR/ksr-lang-0.1.0.vsix"

    # Build the .vsix from source if npm is available
    if command -v npm &>/dev/null; then
        step "Building VS Code extension"
        pushd "$VSIX_DIR" >/dev/null
        npm install --silent
        npm run bundle -- --minify
        npx vsce package --out ksr-lang-0.1.0.vsix --allow-missing-repository
        popd >/dev/null
        ok "VS Code extension built"
    else
        warn "npm not found — using pre-built .vsix (if present)"
    fi

    step "Installing VS Code extension"
    if command -v code &>/dev/null && [[ -f "$VSIX" ]]; then
        code --install-extension "$VSIX"
        ok "VS Code extension installed"
    elif ! command -v code &>/dev/null; then
        warn "VS Code ('code') not found on PATH — skipping extension install"
        warn "To install manually:  code --install-extension $VSIX"
    else
        warn ".vsix not found at $VSIX — skipping extension install"
    fi
fi

# ── Done ──────────────────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}KSR installed successfully!${NC}"
echo ""
echo "  Get started:"
echo -e "    ${CYAN}dotnet new ksr-console -n MyApp${NC}"
echo -e "    ${CYAN}cd MyApp${NC}"
echo -e "    ${CYAN}dotnet run${NC}"
echo ""
echo "  Single-file mode:"
echo -e "    ${CYAN}ksr hello.ksr${NC}"
echo ""
