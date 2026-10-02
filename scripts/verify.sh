#!/usr/bin/env bash
# Verification commands. CI and local runs use this script, so the commands are the same.
#
#   bash scripts/verify.sh <step>... [--results <dir>]
#
# Steps: restore, build, format, unit, integration, boundary, evidence, all.
# 'all' runs every step in this sequence and stops at the first failure.
# Output goes to <dir> (default: artifacts/verify): one log for each step, TRX files for the tests,
# and summary.txt with one line for each step: "<step> exit=<code>".
#
# A test step fails when no tests ran or when a test was skipped or not executed. A skipped
# integration test (for example, no Docker) can therefore not count as a successful check.

set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

solution=AgenticPayments.slnx
unit_project=tests/AgenticPayments.UnitTests
integration_project=tests/AgenticPayments.IntegrationTests
results=artifacts/verify
steps=()

while [ $# -gt 0 ]; do
    case "$1" in
        --results) results=$2; shift 2 ;;
        restore|build|format|unit|integration|boundary|evidence) steps+=("$1"); shift ;;
        all) steps+=(restore build format unit integration boundary evidence); shift ;;
        *) echo "verify: unknown argument: $1" >&2; exit 2 ;;
    esac
done
[ ${#steps[@]} -gt 0 ] || { echo "usage: scripts/verify.sh <restore|build|format|unit|integration|boundary|evidence|all>... [--results <dir>]" >&2; exit 2; }
mkdir -p "$results"

# Reads one counter from the Counters element of a TRX file.
trx_counter() {
    sed -n "s/.*<Counters[^>]* $1=\"\([0-9]*\)\".*/\1/p" "$2" | head -n 1
}

# Fails when the TRX file shows no tests, or tests that did not run and pass.
check_trx() {
    local trx=$1 total executed passed
    [ -f "$trx" ] || { echo "verify: test results not found: $trx"; return 1; }
    total=$(trx_counter total "$trx")
    executed=$(trx_counter executed "$trx")
    passed=$(trx_counter passed "$trx")
    echo "verify: $(basename "$trx"): total=$total executed=$executed passed=$passed"
    if [ -z "$total" ] || [ "$total" -eq 0 ]; then
        echo "verify: no tests ran"; return 1
    fi
    if [ "$executed" != "$total" ] || [ "$passed" != "$total" ]; then
        echo "verify: $((total - passed)) test(s) did not run and pass (failed, skipped or not executed)"; return 1
    fi
}

run_tests() {
    local name=$1 project=$2
    rm -f "$results/$name.trx"
    dotnet test "$project" --logger "trx;LogFileName=$name.trx" --results-directory "$results"
    check_trx "$results/$name.trx"
}

step_restore()  { dotnet restore "$solution"; }
step_build()    { dotnet build "$solution" --no-restore; }
step_format()   { dotnet format "$solution" --verify-no-changes --no-restore; }
step_unit()     { run_tests unit "$unit_project"; }
step_boundary() { bash scripts/workflow/tests/boundary.test.sh; }
step_evidence() { bash scripts/workflow/tests/evidence-links.test.sh && bash scripts/workflow/check-evidence-links.sh; }

step_integration() {
    # Testcontainers talks to the Docker API directly. The CLI check only gives a clearer message.
    if command -v docker >/dev/null 2>&1; then
        docker info >/dev/null 2>&1 || { echo "verify: Docker is not running. The integration tests need Docker."; return 1; }
    else
        echo "verify: note: the docker command is not on PATH. The tests still try the Docker API."
    fi
    run_tests integration "$integration_project"
}

for step in "${steps[@]}"; do
    echo "== verify: $step"
    status=0
    "step_$step" >"$results/$step.log" 2>&1 || status=$?
    tail -n 5 "$results/$step.log"
    echo "$step exit=$status" >>"$results/summary.txt"
    if [ "$status" -ne 0 ]; then
        echo "verify: $step FAILED (exit $status). Log: $results/$step.log" >&2
        exit "$status"
    fi
    echo "verify: $step passed"
done
