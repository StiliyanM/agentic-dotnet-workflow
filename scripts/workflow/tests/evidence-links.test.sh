#!/usr/bin/env bash
# Tests for check-evidence-links.sh. Each case runs in a new temporary repository.
#
#   bash scripts/workflow/tests/evidence-links.test.sh

set -euo pipefail

workflow_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
failures=0

# expect <case name> <expected exit> <markdown content>
expect() {
    local name=$1 expected=$2 content=$3 repo="$tmp/$1" actual=0
    mkdir -p "$repo/runs/001-x/verify/04-verify"
    cd "$repo"
    git init -q
    git config core.autocrlf false
    echo "build output" >runs/001-x/verify/04-verify/build.log
    echo "summary" >runs/001-x/verify/04-verify/summary.txt
    echo "not tracked" >runs/001-x/untracked.log
    printf '%s\n' "$content" >runs/001-x/evidence.md
    git add runs/001-x/evidence.md runs/001-x/verify
    bash "$workflow_dir/check-evidence-links.sh" runs >"$tmp/$name.out" 2>&1 || actual=$?
    if [ "$actual" = "$expected" ]; then
        echo "ok   $name"
    else
        echo "FAIL $name: exit $actual, expected $expected"
        sed 's/^/     /' "$tmp/$name.out"
        failures=$((failures + 1))
    fi
}

expect tracked-file       0 '[log](verify/04-verify/build.log)'
expect tracked-folder     0 '[folder](verify/04-verify/)'
expect anchor-and-web     0 '[a](#section) [b](https://example.com/x) [c](verify/04-verify/summary.txt#top)'
expect untracked-file     1 '[log](untracked.log)'
expect missing-file       1 '[log](verify/04-verify/integration.log)'
expect empty-folder       1 '[folder](verify/07-verify/)'
expect parent-path        0 '[self](../001-x/evidence.md)'

if [ "$failures" -gt 0 ]; then
    echo "evidence link tests: $failures failure(s)"
    exit 1
fi
echo "evidence link tests: all passed"
