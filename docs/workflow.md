# Workflow controls

This file describes the controls of the agent workflow: boundary checks, verification commands and run evidence. `CLAUDE.md` gives the procedure that the orchestrator follows.

## Agent boundary checks

The orchestrator runs `scripts/workflow/boundary.sh` outside the agent that it checks.

| Command | What it does |
|---|---|
| `boundary.sh snapshot <spec-id> <step>` | Saves HEAD, the index tree, the working-tree tree, and a hash of `.git/config` and `.git/hooks` to `.git/agent-boundary/<spec-id>/<step>.state`. |
| `boundary.sh check <spec-id> <step> <role>` | Computes the same values again. Each changed path must match a pattern for the role in `scripts/workflow/allowed-paths.conf`. Exit 0 = pass, 1 = violation, 2 = usage error. |
| `boundary.sh diff <base> <out-file> [<path>...]` | Writes the diff from `<base>` to the working tree, including untracked files. The read-only agents get their input from this file. |

### What the check detects

- Unstaged changes, staged changes and new untracked files, in all paths of the repository. The working-tree tree is made with a temporary index file, so a staged change cannot hide a working-tree change, and the reverse.
- A staged change that the agent then reverts in the working tree (the index tree is compared separately).
- Deleted files.
- A commit by the agent (HEAD moved), also when the commit contains only allowed paths.
- Changes to `.git/config` and to hooks in `.git/hooks`.

### What the check does on a violation

- It exits with 1 and lists each violation.
- It saves evidence in `.git/agent-boundary/<spec-id>/`: `<step>.result`, `<step>.worktree.patch`, `<step>.index.patch` and, when HEAD moved, `<step>.commits.txt`.
- It does not change the working tree, the index or HEAD. The orchestrator stops the run, and the user decides what to keep.

### Limits

These checks find mistakes. They are not a security sandbox. An agent that tries to avoid them can do so.

- **Ignored files are not checked.** `bin/`, `obj/`, `artifacts/`, `.agent-input/` and other paths in `.gitignore` are outside the check.
- **Files outside the repository are not checked.** For example, user-level git config, global tools or the NuGet cache.
- **The snapshot is in `.git/`.** An agent with a shell can change the snapshot file. The orchestrator records the snapshot line in the run evidence, so a later comparison can show a change.
- **Only the state after the step is checked.** A change that an agent makes and then reverts before it finishes is not detected.
- **Side effects are not checked.** For example, network calls, Docker containers or processes that the agent starts.
- **Tool restrictions:** the test-auditor and the reviewer have only Read, Grep and Glob, so they cannot write files. The planner has Write but no shell. The test-writer and the implementer need a shell for `dotnet`, so for them the check is the only control.

### Self-test

`bash scripts/workflow/tests/boundary.test.sh` runs each case in a new temporary repository: an allowed edit, a forbidden unstaged edit, a forbidden staged edit, a staged edit reverted in the working tree, a new forbidden file, a deleted file, an unexpected commit, a new hook, a reviewer edit, a plan for another spec, evidence kept after a violation, and the diff command.

## Controls and maintenance

A spec run must not change its own controls: `specs/`, `CLAUDE.md`, `.claude/`, `docs/architecture.md`, `docs/csharp-style.md`, `docs/workflow.md`, `scripts/`, `.github/`, `Directory.Build.props`, `.editorconfig`, `.gitattributes` and `.gitignore`. The `orchestrator` role in `allowed-paths.conf` does not include them. The orchestrator checks the whole run against this role before it commits.

A change to a control is a maintenance change. It happens only when the user asks for it directly, outside a spec run.
