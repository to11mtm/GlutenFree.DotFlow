# Test Flakiness — Diagnosis & Fixes (Aug 2026) 🧪

> Recorded because "it's just flaky" was costing real time on every run, and two of the three
> causes turned out to be **actual bugs in the test setup**, not noise.

## Before

Every full `Workflow.Tests` run failed with ~4–9 tests red, and the failing set **rotated** — the
same test passed in isolation and failed in the suite. That's the most expensive kind of failure:
it trains people to ignore red.

## Cause 1 — a bare `:memory:` SQLite connection string gives each connection its own database 🔴

Nine API test fixtures configured `["Persistence:ConnectionString"] = ":memory:"`.

With Microsoft.Data.Sqlite, `Data Source=:memory:` is **per-connection private**. The provider
opens a keep-alive connection, then the migration runner opens a *different* connection to create
the schema — into a database that is discarded when it closes. A standalone probe reproduced it:

```text
--- bare :memory:            → SQLite Error 1: 'no such table: t'
--- named + Cache=Shared     → rows visible to a later connection: 1
--- two different names      → isolated from each other: True
```

Whether any given operation saw a schema depended on connection pooling and timing, which is
exactly why it looked like flakiness rather than a hard failure.

**Fix:** `Workflow.Tests/TestSqlite.InMemory()` — a unique **named** database with `Cache=Shared`,
so all connections in one fixture share a database while fixtures stay isolated from each other.

## Cause 2 — terminal execution state ≠ writes flushed ⏳

Tests asserted persisted records immediately after `WaitForTerminalState(...)`. But the engine
pipes persistence writes **asynchronously** (`PipeTo`, Phase 2.1.5), so a completed execution does
not imply its node records or variables are readable yet.

**Fix:** an `Eventually(read, condition)` helper in the affected fixtures
(`PersistenceIntegrationTests`, `HttpPersistenceTests`) that polls the read instead of assuming.
This is a *correct* description of the engine's contract, not a sleep-and-hope.

## Cause 3 — 32-way parallelism starving heavyweight fixtures 🧵

No `xunit.runner.json` existed, so xUnit defaulted to one parallel collection per logical core —
**32** on this machine. Many collections spin up a full ASP.NET host (`WebApplicationFactory`) or
an Akka `ActorSystem`; under that contention, timing-sensitive waits (`ExpectMsg` timeouts, actor
scheduling) began missing deadlines.

**Fix:** `Workflow.Tests/xunit.runner.json` with `maxParallelThreads: 8`. Cost is a few seconds of
wall clock; benefit is a suite you can believe.

## Cause 4 — a rate-limit test racing its own window 🚦

`RateLimit_WhenEnabled_Returns429OverLimit` used a 60-second fixed window and 3 requests. A window
boundary landing between requests 2 and 3 lets the third through **legitimately**.

**Fix:** widen the window to an hour. The assertion is unchanged; the race is gone.

## After

Six consecutive full runs: **5 × 1700/1700 green**, one run with a single unrelated failure
(`WebhookSignatureTests.Stripe_ExpiredTimestamp_Rejected` — a genuine wall-clock-dependent test,
left for a separate fix). `Workflow.Tests.UI` is 706/706 throughout.

## If it comes back

1. Run the failing class **in isolation** — if it passes, suspect contention, not the test.
2. Run the suite with the new tests **excluded** to check whether recent work is implicated.
3. Prefer polling (`Eventually`, `AwaitAssert`) over sleeps; prefer unique named resources over
   shared ones.
