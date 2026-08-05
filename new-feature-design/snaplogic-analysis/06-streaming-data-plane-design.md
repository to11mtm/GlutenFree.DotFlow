# 06 — Design: Streaming Data Plane (Chunked Ports) 🌊

> Closes the "streaming data plane" gap from [`02`](02-snaplogic-comparison.md) §2/§7.
> SnapLogic parity target: per-document streaming between Snaps (bounded memory for large ETL).
> Design-level; not implementation-ready. **Largest and riskiest of the four designs.**

## 1. Goal & explicitly-not-goals

**Goal:** let a chain of nodes process a large dataset (DB result set, big CSV/JSON file, paged
API) with **bounded memory**, without materializing the whole set in the execution context.

**Not goals:** rewriting DotFlow into a document-streaming engine; changing default semantics.
Batch-per-trigger stays the model — streaming is an **opt-in port capability**, preserving
DotFlow's composition philosophy (explicit over implicit).

## 2. Approach comparison

| Option | Sketch | Verdict |
| --- | --- | --- |
| A. Chunked ForEach (no engine change) | producers emit pages; `foreach` over pages | ✅ works today-ish; high friction; keep as guidance |
| B. **Stream-typed ports** | ports of type `IAsyncEnumerable<JsonElement>`-like; engine runs a streaming *region* concurrently | ✅ **recommended** |
| C. Full document streaming (SnapLogic model) | every edge is a stream | ❌ breaks variables/staged writes/trycatch semantics; huge — see full assessment in [`09`](09-full-streaming-rewrite-assessment.md) |

## 3. Recommended architecture (Option B)

### 3.1 Stream ports

- New port capability on `PortDefinition`: `IsStreaming` (or a `DataStream` marker `DataType`).
  Designer draws them distinctly (SnapLogic's circle-vs-diamond precedent).
- **Shape rule** like SnapLogic: streaming outputs connect only to streaming inputs. Bridging
  nodes convert: `builtin.stream.collect` (stream→array, with `maxItems` guard) and
  `builtin.stream.fromitems` (array→stream).

### 3.2 Streaming regions

A maximal connected sub-graph of stream-linked nodes forms a **streaming region**. The engine
(`WorkflowExecutor`) detects the region and hands it to a new `StreamRegionExecutorActor`
(same family as `LoopExecutorActor`/`ParallelExecutionCoordinator`):

- Each node in the region runs as a long-lived stage; items flow via **bounded channels**
  (`System.Threading.Channels`, configurable capacity, default e.g. 64) — real backpressure,
  no Akka mailbox flooding.
- Region completes when the source stage completes and all channels drain; then normal
  port-dispatch resumes downstream (scalar outputs like `itemCount`, plus any collected
  results, feed non-streaming successors).
- Cancellation: region's CTS linked to `_executionCts` as usual. A stage fault cancels the
  region and surfaces as the region's node failure (parent trycatch applies).

### 3.3 Module contract extension

New optional interface (modules keep `IWorkflowModule` for scalar work):

```csharp
public interface IStreamingWorkflowModule : IWorkflowModule
{
    IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,      // empty for sources
        CancellationToken ct);
}
```

`StreamItem` ≈ `JsonElement` payload + lightweight metadata (index, isLast?). Sources ignore
`input`; sinks drain it and emit nothing; transforms do both.

### 3.4 First streaming-capable modules

`builtin.database.query` (reader → items), `builtin.file.csv.read` / `.json.read` (array
streaming), `builtin.transform.map` / `.query` (per-item), `builtin.database.bulkinsert` and
`builtin.file.*.write` (sinks), plus the two bridge nodes.

## 4. Semantics decisions (the hard part)

1. **Variables inside a region:** reads allowed (snapshot at region start); `SetVariable` is
   **not allowed inside a region** (non-deterministic ordering). Enforced at validation.
2. **Templates per item:** streaming transforms get `{{item}}` (analogous to loop semantics)
   rather than `{{input}}`.
3. **Error granularity:** per-item error policy on each stage — `fail` (default), `skip`, or
   route to an optional streaming `error` output port (SnapLogic error-view analog).
4. **History:** per-node record stores item counts + duration, not per-item records.
5. **Joins/aggregates over streams:** v1 restriction — `aggregate` allowed (bounded
   accumulator), stream-stream `join` **not** (SnapLogic's own sorted-stream/hang caveats show
   the cost); collect-then-join instead.

## 5. Open questions

- [ ] Backpressure default channel capacity; per-connection override worth it?
- [ ] Binary streams (file/blob pass-through) in v1, or JSON items only? (Recommend items only.)
- [ ] Does `builtin.parallel` compose with regions (parallel stages) in v1? (Recommend no.)
- [ ] Checkpointing a region for resumability — defer entirely?
- [ ] Interaction with future Reuse-mode sub-workflows (child as a streaming stage).

## 6. Phasing

1. Bridge modules + Option A guidance doc (cheap, immediate).
2. Port capability + validation + designer rendering.
3. `StreamRegionExecutorActor` + channels + 3 modules (db.query → map → bulkinsert) as the
   proving slice.
4. Remaining modules, per-item error routing.
