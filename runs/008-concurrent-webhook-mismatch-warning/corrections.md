# Corrections: 008-concurrent-webhook-mismatch-warning

## 04-implementer boundary check (orchestrator error)

The orchestrator saved the implementer report to `agents/04-implementer.md` before it ran the boundary check for 04-implementer. The first check therefore failed with one extra path: `runs/008-concurrent-webhook-mismatch-warning/agents/04-implementer.md`. The implementer did not write that file; the orchestrator did (the `orchestrator` role allows `runs/*`). The only other changed path was `src/AgenticPayments.Application/Webhooks/ProcessProviderWebhookUseCase.cs`, which the implementer may change.

- The first result is kept, not changed: [boundary/orchestrator-error-04-implementer/04-implementer.result](boundary/orchestrator-error-04-implementer/04-implementer.result) (with its `.state`, `.after` and patches).
- The orchestrator moved its own report file out of the working tree, ran `boundary.sh check 008-concurrent-webhook-mismatch-warning 04-implementer implementer` again (PASS, 1 path: the use case file, exit 0), and put the report file back. The second result is [boundary/04-implementer.result](boundary/04-implementer.result).
- The patches of the first check were copied to `boundary/` by the copy step. They are identical to the files in `orchestrator-error-04-implementer/`, so the copies in `boundary/` were removed. A passing check writes no patches.
- The run was not stopped, because the only violating path was written by the orchestrator, not by the agent (same case as `runs/003-out-of-order/corrections.md`). From step 05 on, the orchestrator runs the check before it saves the agent report.
