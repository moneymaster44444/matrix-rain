#!/usr/bin/env bash
# Copy the Steam screensaver into Steam's uioverrides folder (SteamOS / Linux).
# Usage:  bash install.sh [steam-root]
# An existing config.js is kept, so your settings survive a reinstall. The
# current template is always written next to it as config.example.js, and any
# settings your config.js doesn't set yet are listed at the end.
set -euo pipefail

src="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/matrix-rain"

root="${1:-}"
if [ -z "$root" ]; then
    for c in "$HOME/.steam/root" "$HOME/.steam/steam" "$HOME/.local/share/Steam" \
             "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
        if [ -d "$c" ]; then
            root="$(cd "$c" && pwd -P)"
            break
        fi
    done
fi
if [ -z "$root" ] || [ ! -d "$root" ]; then
    echo "Steam not found. Pass its root folder: bash install.sh ~/.local/share/Steam" >&2
    exit 1
fi

dest="$root/config/uioverrides/screensavers/matrix-rain"
echo "Installing to $dest"
mkdir -p "$dest"

(cd "$src" && find . -type f) | while read -r rel; do
    rel="${rel#./}"
    if [ "$rel" = "config.js" ] && [ -e "$dest/config.js" ]; then
        echo "  keeping existing config.js"
        continue
    fi
    mkdir -p "$dest/$(dirname "$rel")"
    cp "$src/$rel" "$dest/$rel"
    echo "  $rel"
done
cp "$src/config.js" "$dest/config.example.js"
echo "  config.example.js (reference copy of every setting)"

# Settings added since your config.js was written fall back to their defaults
# until you add them; list them so upgrades don't hide new options.
missing=""
for key in $(grep -oE '^[[:space:]]*[A-Za-z]+[[:space:]]*:' "$src/config.js" | tr -d ' \t:'); do
    grep -qE "^[[:space:]]*$key[[:space:]]*:" "$dest/config.js" || missing="$missing $key"
done

echo
echo "Installed."
echo "In Steam: Settings > Customization > Screensaver, pick \"matrix-rain (User Created)\"."
echo "Edit settings in: $dest/config.js"
if [ -n "$missing" ]; then
    echo
    echo "Your config.js doesn't set:$missing"
    echo "They use their defaults. Copy them from config.example.js to change them."
fi
