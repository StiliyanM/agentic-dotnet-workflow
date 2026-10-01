#!/usr/bin/env bash
# Agent boundary checks. Run by the orchestrator, outside the agent that is checked.
#
#   boundary.sh snapshot <spec-id> <step>          Save the repository state before a step.
#   boundary.sh check    <spec-id> <step> <role>   Compare the state with the snapshot. Exit 1 on a violation.
#   boundary.sh diff     <base-ref> <out-file> [<path>...]  Write the diff from <base-ref> to the working tree,
#                                                  including untracked files (input for read-only agents).
#
# The checks detect changes to staged, unstaged and untracked files, to HEAD, and to .git/config and
# .git/hooks. They do not detect changes to ignored files or outside the repository. This is not a
# security sandbox. See docs/workflow.md for the limits.
#
# The checks never change the working tree, the index or HEAD. A temporary index file is used to
# record the working tree.

set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
allowed_paths_file="${BOUNDARY_ALLOWED_PATHS:-$script_dir/allowed-paths.conf}"

die() { echo "boundary: $*" >&2; exit 2; }

state_dir() {
    echo "$(git rev-parse --git-common-dir)/agent-boundary/$1"
}

# Tree object of the working tree, including untracked files that are not ignored.
worktree_tree() {
    local tmp_index
    tmp_index=$(mktemp)
    cp "$(git rev-parse --git-path index)" "$tmp_index" 2>/dev/null || rm -f "$tmp_index"
    GIT_INDEX_FILE="$tmp_index" git add -A >/dev/null 2>&1
    GIT_INDEX_FILE="$tmp_index" git write-tree
    rm -f "$tmp_index"
}

index_tree() {
    git write-tree
}

head_commit() {
    git rev-parse --verify --quiet HEAD || echo none
}

# Hash of files that change git behavior but are not in the working tree.
git_controls_hash() {
    local git_dir hooks_dir f
    git_dir=$(git rev-parse --git-common-dir)
    hooks_dir=$(git rev-parse --git-path hooks)
    {
        git hash-object "$git_dir/config"
        if [ -d "$hooks_dir" ]; then
            for f in "$hooks_dir"/*; do
                [ -f "$f" ] || continue
                case "$f" in *.sample) continue ;; esac
                echo "$(basename "$f") $(git hash-object "$f")"
            done
        fi
    } | git hash-object --stdin
}

write_state() {
    echo "head=$(head_commit)"
    echo "index=$(index_tree)"
    echo "worktree=$(worktree_tree)"
    echo "controls=$(git_controls_hash)"
}

read_value() {
    sed -n "s/^$1=//p" "$2"
}

# Allowed glob patterns for a role, with {spec} replaced by the spec id.
allowed_patterns() {
    local role=$1 spec=$2
    [ -f "$allowed_paths_file" ] || die "allowed paths file not found: $allowed_paths_file"
    awk -v role="$role" '$1 == role && $2 != "" { print $2 }' "$allowed_paths_file" |
        sed "s/{spec}/$spec/g"
}

is_allowed() {
    local path=$1 pattern
    shift
    for pattern in "$@"; do
        # shellcheck disable=SC2254 # The pattern is a glob on purpose. '*' also matches '/'.
        case "$path" in $pattern) return 0 ;; esac
    done
    return 1
}

cmd_snapshot() {
    [ $# -eq 2 ] || die "usage: boundary.sh snapshot <spec-id> <step>"
    local dir
    dir=$(state_dir "$1")
    mkdir -p "$dir"
    write_state >"$dir/$2.state"
    echo "boundary: snapshot $1/$2 $(tr '\n' ' ' <"$dir/$2.state")"
}

cmd_check() {
    [ $# -eq 3 ] || die "usage: boundary.sh check <spec-id> <step> <role>"
    local spec=$1 step=$2 role=$3 dir before after
    dir=$(state_dir "$spec")
    before="$dir/$step.state"
    [ -f "$before" ] || die "no snapshot for $spec/$step"
    after="$dir/$step.after"
    write_state >"$after"

    local old_head new_head old_index new_index old_tree new_tree old_controls new_controls
    old_head=$(read_value head "$before");         new_head=$(read_value head "$after")
    old_index=$(read_value index "$before");       new_index=$(read_value index "$after")
    old_tree=$(read_value worktree "$before");     new_tree=$(read_value worktree "$after")
    old_controls=$(read_value controls "$before"); new_controls=$(read_value controls "$after")

    local -a patterns=() violations=() changed=()
    local line
    while IFS= read -r line; do patterns+=("$line"); done < <(allowed_patterns "$role" "$spec")

    if [ "$old_head" != "$new_head" ]; then
        violations+=("HEAD moved from $old_head to $new_head (agents must not commit)")
    fi
    if [ "$old_controls" != "$new_controls" ]; then
        violations+=(".git/config or .git/hooks changed")
    fi

    while IFS= read -r line; do
        [ -n "$line" ] && changed+=("$line")
    done < <({
        git diff-tree -r --no-commit-id --name-only "$old_tree" "$new_tree"
        git diff-tree -r --no-commit-id --name-only "$old_index" "$new_index"
    } | sort -u)

    local path
    for path in "${changed[@]+"${changed[@]}"}"; do
        if ! is_allowed "$path" "${patterns[@]+"${patterns[@]}"}"; then
            violations+=("path not allowed for $role: $path")
        fi
    done

    local result="$dir/$step.result"
    {
        echo "spec=$spec"
        echo "step=$step"
        echo "role=$role"
        echo "allowed=${patterns[*]:-<none>}"
        for path in "${changed[@]+"${changed[@]}"}"; do echo "changed: $path"; done
        for line in "${violations[@]+"${violations[@]}"}"; do echo "violation: $line"; done
        if [ ${#violations[@]} -eq 0 ]; then echo "outcome=PASS"; else echo "outcome=FAIL"; fi
    } >"$result"

    if [ ${#violations[@]} -gt 0 ]; then
        # Evidence only. Nothing in the working tree, the index or HEAD is changed.
        git diff --binary "$old_tree" "$new_tree" >"$dir/$step.worktree.patch"
        git diff --binary "$old_index" "$new_index" >"$dir/$step.index.patch"
        if [ "$old_head" != "$new_head" ] && [ "$old_head" != none ]; then
            git log --stat "$old_head..$new_head" >"$dir/$step.commits.txt" 2>&1 || true
        fi
        echo "boundary: FAIL $spec/$step ($role). Evidence: $dir/$step.*" >&2
        sed -n 's/^violation: /  - /p' "$result" >&2
        exit 1
    fi

    echo "boundary: PASS $spec/$step ($role), ${#changed[@]} changed path(s)"
}

cmd_diff() {
    [ $# -ge 2 ] || die "usage: boundary.sh diff <base-ref> <out-file> [<path>...]"
    local base=$1 out=$2
    shift 2
    mkdir -p "$(dirname "$out")"
    git diff --binary "$base" "$(worktree_tree)" -- "$@" >"$out"
    echo "boundary: diff $base..working-tree -> $out"
}

[ $# -ge 1 ] || die "usage: boundary.sh snapshot|check|diff ..."
command=$1
shift
case "$command" in
    snapshot) cmd_snapshot "$@" ;;
    check) cmd_check "$@" ;;
    diff) cmd_diff "$@" ;;
    *) die "unknown command: $command" ;;
esac
