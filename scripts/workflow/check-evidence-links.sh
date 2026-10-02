#!/usr/bin/env bash
# Checks that relative links in Markdown files point to files that git tracks.
# A link to a folder (ending in '/') must contain at least one tracked file.
# Web links (http, https, mailto) and anchors in the same file are not checked.
#
#   bash scripts/workflow/check-evidence-links.sh [<path>...]     default paths: runs README.md docs
#
# Exit 0 = all links resolve, 1 = one or more links do not resolve, 2 = usage error.
# "Tracked" means in the git index, so run it after 'git add' and before the commit, or on a commit.

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
paths=("$@")
[ ${#paths[@]} -gt 0 ] || paths=(runs README.md docs)

checked=0
missing=0

while IFS= read -r markdown; do
    dir=$(dirname "$markdown")
    while IFS= read -r target; do
        case "$target" in
            http://* | https://* | mailto:* | '#'*) continue ;;
        esac
        target=${target%%#*}
        [ -n "$target" ] || continue
        resolved=$(realpath -m --relative-to=. "$dir/$target")
        checked=$((checked + 1))
        if [[ "$target" == */ ]]; then
            if [ -z "$(git ls-files -- "$resolved/" | head -n 1)" ]; then
                echo "missing: $markdown -> $target (no tracked file in $resolved/)"
                missing=$((missing + 1))
            fi
        elif ! git ls-files --error-unmatch -- "$resolved" >/dev/null 2>&1; then
            echo "missing: $markdown -> $target ($resolved is not tracked)"
            missing=$((missing + 1))
        fi
    done < <(grep -oE '\]\([^)[:space:]]+\)' "$markdown" | sed -E 's/^\]\((.*)\)$/\1/')
done < <(git ls-files -- "${paths[@]}" | grep -E '\.md$')

echo "evidence links: $checked checked, $missing missing"
[ "$missing" -eq 0 ]
