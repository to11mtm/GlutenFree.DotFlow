# GlutenFree.DotFlow — agent instructions

.NET 8 Akka.NET workflow engine (API, Blazor UI, modules, scripting, persistence providers).

## Start of every session

1. **Search memorizer before you start** (see *Long-term memory* below). Scope to the DotFlow
   workspace and query for the area you're about to touch, e.g. "DotFlow WorkflowExecutor
   invariants", "DotFlow module packaging", "DotFlow testing gotchas".
2. If the task is part of the findings remediation, read `agents/todo/findings-remediation-worklog.md`
   — the newest *Progress log* entry says where things stand, and §2 is the working loop.
3. `git status`: several sessions may share this checkout. Never `git add -A`; stage your files by path.

## Long-term memory (memorizer MCP)

Cross-session knowledge for this repo lives in the memorizer MCP server:

| What | Id |
| --- | --- |
| Workspace **DotFlow** (repo-wide conventions, gotchas) | `8dceac5b-5475-4e5d-854c-68ed25931e14` |
| Project **Findings remediation** (work-specific knowledge) | `f88fa55e-53b0-4a8f-9813-e2cfe6a16221` |

### Retrieve

- `search_memories` with `workspaceId` = DotFlow (covers the workspace and its projects) and a
  natural-language query naming the subsystem. Lower `minSimilarity` to ~0.5 when results are thin.
- `get` the hits that look relevant before acting — titles are summaries, the text has the rules.
- Memories describe the code **as of the commit they cite**. If a memory names a file, class or
  method, check it still exists before relying on it; if it's wrong, fix the memory (below).

### What to store — and where

Store knowledge a future session would **not** get from reading the code or the repo docs quickly:

- **Invariants / rules** that span files ("every construct actor must pass BodyVariables…").
- **Gotchas** that cost real time ("shared-cache `:memory:` is one DB per process").
- **Decisions + rationale** the user made, with the date and commit ("warn on save, block on run").
- **How-tos** for non-obvious workflows (how to prove a test bites, how to build/test here).

Don't store: task status or progress (→ the worklog), designs and plans (→ `docs/architecture/plans`),
anything already stated clearly in code comments, secrets/credentials/keys, or personal data.

| Kind of knowledge | Home |
| --- | --- |
| Status, what was done, what's next | `agents/todo/findings-remediation-worklog.md` progress log |
| Plans, options, decided designs | `docs/architecture/plans/*.md` |
| Cross-session rules, gotchas, decisions-with-why | memorizer |

### How to store

- **Search first; update instead of duplicating.** If a memory covers the topic, `get` it, then
  `edit` (exact find/replace) or `update_metadata` (title/tags). Retitle it if its scope grew.
- **Scope:** work-specific → `projectId` = Findings remediation; repo-wide → `workspaceId` = DotFlow.
  Never leave DotFlow memories in Unfiled (`move_memory` them if you find any).
- **Title** = the specific claim, prefixed "DotFlow …" — it's the main search index.
  Good: "DotFlow module config validation (§9.4): warn on save, block on run …".
  Bad: "Notes on validation".
- **Text:** the rule first, then why, then where (files/classes) and the commit + date it was true as
  of. Name the tests that guard it.
- **Tags:** always `dotflow`, plus the area (`engine`, `modules`, `scripting`, `api`, `testing`,
  `persistence`, `security`…) and the kind (`gotcha`, `decision`, `how-to`, `reference`,
  `coding-standard`).
- **Archetype:** `record` for immutable decision records; `document` (default) for living notes.
- **Link** related memories with `create_reference` (`related-to`, `explains`, `example-of`).
- **Outdated?** Edit it if it's still mostly right; `archive_memory` it if it's superseded (prefer
  archive over delete).

### When to store

- When you hit a gotcha or the user makes a decision — store it then, not only at the end.
- **Before you report a task done**, run this check: *did I learn a rule, gotcha or decision that a
  future session would need and couldn't easily get from the code or the worklog?* If yes, store or
  update it, and mention in your report which memories you added or changed.

### If memorizer is unavailable

Don't silently drop knowledge. Add it under a **"Memories to store"** heading in your progress-log
entry in the worklog (title + text + tags), tell the user the server is unreachable, and store it
once the server is back (then remove the note). Never guess memory contents you couldn't read.

## Working conventions

- Source `.cs` / `.razor` files are **UTF-8 with BOM, CRLF** in the working tree (git stores LF).
  Preserve that when creating or rewriting files.
- Build and test from **PowerShell** (Windows), e.g.
  `dotnet test Workflow.Tests --filter "Category!=Finding"` and `dotnet test Workflow.Tests.UI`.
  Report pass/fail summaries, not Akka console output.
- Tests tagged `[Trait("Category","Finding")]` reproduce known defects and are expected to fail.
- When fixing something, prove the new tests bite: stash only the production change → tests red →
  `git stash pop` → green (worklog §2). Restore with `git stash pop`, not by copying files back
  (a copied file keeps its old mtime and MSBuild reuses the stale build).
- Commits: short, lowercase, with a § reference when it's a finding (`fix §9.4: …`); one finding per
  commit; never weaken a passing test; stop and ask when a plan flags a design decision.
