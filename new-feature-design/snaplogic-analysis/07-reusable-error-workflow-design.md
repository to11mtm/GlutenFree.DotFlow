# 07 — Design: Reusable Error Workflows 🛡️

> Closes the "reusable error workflow" gap from [`02`](02-snaplogic-comparison.md) §5/§7.
> SnapLogic parity target: **Error Pipelines** (centralized, reusable error handling).
> Depends on [`05-subworkflow-design.md`](05-subworkflow-design.md). Design-level.

## 1. Goal

Let one workflow own error-handling logic (log, alert, dead-letter, compensate) and be
**attached** to many workflows — instead of copy-pasting trycatch+log sub-graphs everywhere.

## 2. Architecture: composition first, then declaration

**Phase 1 (composition, nearly free once 05 ships):** an error workflow is just a normal
workflow whose contract is "run input `error`"; callers wire `builtin.trycatch` `catch` →
`builtin.workflow.execute`. No engine changes. This already beats SnapLogic on flexibility
(error handling mid-graph, typed, retryable).

**Phase 2 (declaration, the actual parity feature):** a workflow-level setting:

```
WorkflowDefinition.ErrorHandling (existing record) gains:
    ErrorWorkflowId   : Guid?      — the designated error workflow
    ErrorWorkflowMode : enum       — PerError | OnFailureOnly (default)
    Parameters        : map        — extra run inputs (template-enabled)
```

Engine hook: the existing failure path in `WorkflowExecutor` (`HandleNodeFailure` → terminal
`Failed` state) additionally launches the error workflow **fire-and-forget** via the same
supervisor path used by 05, passing a standard **error document** as run inputs. Unlike
SnapLogic (whose error pipeline runs even when nothing fails), the error workflow only runs on
error — cheaper and less surprising.

### The error document (standard contract)

```json
{
  "schemaVersion": 1,
  "error":       { "message", "errorType", "nodeId", "stackTrace?" },   // from WorkflowError
  "execution":   { "executionId", "workflowId", "workflowName", "startedAt", "failedAt" },
  "node":        { "id", "name", "moduleId" },
  "item":        { ...optional — per-item variant... },                  // streaming error envelopes (doc 06 §4.3)
  "offset":      { "token?", "sequence?" },                              // optional SourceOffset of the failing item
  "parameters":  { ...caller-declared extras... }
}
```

Recommend a versioned, documented shape (`schemaVersion: 1`) since error workflows will be
shared assets that many workflows depend on. The optional `item`/`offset` fields form the
**per-item variant** used by streaming error ports ([`06`](06-streaming-data-plane-design.md)
§4.3) — absent for whole-execution errors — so error workflows handle both cases against one
schema.

## 3. Rules & safeguards

1. **No recursive error handling:** an execution launched *as* an error workflow ignores its
   own `ErrorWorkflowId` (flag on the run: `IsErrorHandlerRun`). Its failure is logged +
   surfaced via SignalR/monitoring only.
2. **Isolation:** error workflow failure never changes the original execution's outcome —
   original still records `Failed` with its original error.
3. **Precedence:** node-level `ErrorHandling`/retry and trycatch boundaries run first, as
   today. The error workflow fires only for errors that reach the workflow's terminal failure
   path (`OnFailureOnly`), or for every routed-to-handler node error (`PerError`, later).
4. **Correlation:** error run gets `ParentExecutionId` = failed execution id (from 05) so the
   monitor links them.
5. **Validation:** saving a workflow whose `ErrorWorkflowId` is missing/deleted → designer
   warning; runtime miss → log + proceed (never mask the original error).

## 4. Designer/API surface

- Workflow properties panel: "🛡️ Error workflow" picker (lists workflows tagged or contracted
  with an `error` run input) + parameters editor.
- API: no new endpoints; the setting rides `WorkflowDefinition`. Monitor gains a
  "triggered error handler → executionId" line on failed executions.
- Ship a template/example error workflow (log + webhook alert + dead-letter write) in
  `examples/definitions/`.

## 5. Open questions

- [ ] `PerError` mode (fire per node error routed to it, ≈ SnapLogic per-Snap "route to error
      pipeline") in v1, or `OnFailureOnly` only? (Recommend: OnFailureOnly first.)
- [ ] Should trycatch `catch` blocks optionally *also* forward to the declared error workflow
      (observability without changing control flow)?
- [ ] Global default error workflow (org-wide, like a global setting) — useful for ops teams?
- [ ] Does the error document include the failed node's *inputs* (PII/secret risk — recommend
      no, or redacted per `IsSecret`)?
