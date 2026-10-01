#!/usr/bin/env bash
# Tests for boundary.sh. Each case runs in a new temporary repository, never in this repository.
#
#   bash scripts/workflow/tests/boundary.test.sh

set -euo pipefail

workflow_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

failures=0

new_repo() {
    local repo="$tmp/$1"
    mkdir -p "$repo"
    cd "$repo"
    git init -q
    git config user.email boundary-test@example.com
    git config user.name "boundary test"
    git config core.autocrlf false
    mkdir -p scripts/workflow src tests specs plans
    cp "$workflow_dir/boundary.sh" "$workflow_dir/allowed-paths.conf" scripts/workflow/
    echo "class A {}" >src/A.cs
    echo "class T {}" >tests/T.cs
    echo "# spec" >specs/001-x.md
    echo "rules" >CLAUDE.md
    git add -A
    git commit -qm base
}

boundary() {
    bash scripts/workflow/boundary.sh "$@"
}

# expect <case name> <expected exit> <role> <action...>
expect() {
    local name=$1 expected=$2 role=$3
    shift 3
    new_repo "$name"
    boundary snapshot 001-x step >/dev/null
    "$@"
    local status_before status_after actual=0
    status_before=$(git status --porcelain; git rev-parse HEAD)
    boundary check 001-x step "$role" >"$tmp/$name.out" 2>&1 || actual=$?
    status_after=$(git status --porcelain; git rev-parse HEAD)

    local outcome=ok
    if [ "$actual" != "$expected" ]; then
        outcome="FAILED: exit $actual, expected $expected"
    elif [ "$status_before" != "$status_after" ]; then
        outcome="FAILED: the check changed the repository"
    elif [ "$expected" = 1 ] && [ ! -s "$(git rev-parse --git-common-dir)/agent-boundary/001-x/step.result" ]; then
        outcome="FAILED: no evidence was saved"
    fi

    if [ "$outcome" = ok ]; then
        echo "ok   $name"
    else
        echo "FAIL $name: $outcome"
        sed 's/^/     /' "$tmp/$name.out"
        failures=$((failures + 1))
    fi
}

allowed_edit()            { echo "// change" >>src/A.cs; }
allowed_new_file()        { echo "class B {}" >src/B.cs; }
no_change()               { :; }
forbidden_unstaged_edit() { echo "// change" >>tests/T.cs; }
forbidden_staged_edit()   { echo "changed" >>specs/001-x.md; git add specs/001-x.md; }
staged_then_reverted()    { echo "changed" >>specs/001-x.md; git add specs/001-x.md; git show HEAD:specs/001-x.md >specs/001-x.md; }
forbidden_new_file()      { echo "notes" >specs/002-new.md; }
forbidden_delete()        { rm CLAUDE.md; }
unexpected_commit()       { echo "// change" >>src/A.cs; git commit -qam "agent commit"; }
hook_added()              { echo "exit 0" >"$(git rev-parse --git-path hooks)/pre-commit"; }
planner_own_plan()        { echo "# plan" >plans/001-x.md; }
planner_other_plan()      { echo "# plan" >plans/002-y.md; }

expect allowed-edit                0 implementer allowed_edit
expect allowed-new-file            0 implementer allowed_new_file
expect reviewer-no-change          0 reviewer    no_change
expect planner-own-plan            0 planner     planner_own_plan
expect forbidden-unstaged-edit     1 implementer forbidden_unstaged_edit
expect forbidden-staged-edit       1 implementer forbidden_staged_edit
expect staged-then-reverted        1 implementer staged_then_reverted
expect forbidden-new-file          1 implementer forbidden_new_file
expect forbidden-delete            1 test-writer forbidden_delete
expect unexpected-commit           1 implementer unexpected_commit
expect hook-added                  1 implementer hook_added
expect reviewer-edit               1 reviewer    allowed_edit
expect planner-other-plan          1 planner     planner_other_plan

# Evidence is kept and user work is not removed after a violation.
new_repo evidence
boundary snapshot 001-x step >/dev/null
echo "user work" >>tests/T.cs
if boundary check 001-x step implementer >/dev/null 2>&1; then
    echo "FAIL evidence: the check passed"; failures=$((failures + 1))
elif ! grep -q "user work" tests/T.cs; then
    echo "FAIL evidence: the changed file was reverted"; failures=$((failures + 1))
elif ! grep -q "user work" "$(git rev-parse --git-common-dir)/agent-boundary/001-x/step.worktree.patch"; then
    echo "FAIL evidence: the patch does not contain the change"; failures=$((failures + 1))
else
    echo "ok   evidence-kept"
fi

# diff includes untracked files.
new_repo diff
echo "class C {}" >src/C.cs
boundary diff HEAD "$tmp/review.diff" >/dev/null
if grep -q "src/C.cs" "$tmp/review.diff"; then echo "ok   diff-includes-untracked"; else echo "FAIL diff-includes-untracked"; failures=$((failures + 1)); fi

# diff with a path filter contains only that path.
echo "class U {}" >tests/U.cs
boundary diff HEAD "$tmp/tests.diff" tests/ >/dev/null
if grep -q "tests/U.cs" "$tmp/tests.diff" && ! grep -q "src/C.cs" "$tmp/tests.diff"; then
    echo "ok   diff-path-filter"
else
    echo "FAIL diff-path-filter"; failures=$((failures + 1))
fi

if [ "$failures" -gt 0 ]; then
    echo "boundary tests: $failures failure(s)"
    exit 1
fi
echo "boundary tests: all passed"
