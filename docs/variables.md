# Workflow Variables 💾

> Phase 3.5 — declaring variables, where their values come from, and how to reference them.
> Made with 💖 by Ami-Chan~ ✨

Variables are the shared state of a workflow: named values that nodes read at run time and can
write back to. They're what turns a hard-coded workflow into a configurable one — the difference
between `https://api.prod.example.com/orders` baked into a node and `{{Variable.apiBaseUrl}}/orders`
that follows the environment.

- [The three scopes](#the-three-scopes)
- [Where a value comes from](#where-a-value-comes-from-precedence)
- [Declaring variables](#declaring-variables)
- [Supplying values when you run](#supplying-values-when-you-run)
- [Referencing variables](#referencing-variables)
- [Which fields expand templates](#which-fields-expand-templates)
- [Design-time checks](#design-time-checks)
- [Worked example](#worked-example)
- [Secrets — current limits](#secrets--current-limits)

---

## The three scopes

| Scope | Lives for | Set from | Use it for |
| --- | --- | --- | --- |
| **Global** | Forever, shared by **every** workflow | Settings → Global variables, or `PUT /api/v1/variables/{name}?scope=global` | Environment-wide configuration: an API base URL, a tenant id, a feature flag |
| **Workflow** | Across runs of **one** workflow | A run using variable-write mode `workflow` or `dual`, or the API with `?scope=workflow&scopeId=` | State that should survive between runs: a high-water mark, `lastRunAt` |
| **Execution** | One run only | Run inputs, `SetVariable` nodes | Everything else — per-run values, intermediate results |

Every scope is versioned: each write creates a new version, and the full history is available
(`GET /api/v1/variables/{name}/history`, or the 🕘 button in Settings).

> 🔐 Writing a **global** requires the `Admin` role, because it changes behaviour for every
> workflow at once. Reading is available to any role that can read workflows, so the designer's
> binding picker can list them.

## Where a value comes from (precedence)

When a run starts, DotFlow layers every source into one variable map. Later layers win:

```
1. Global store values             (lowest)
2. Workflow store values
3. Declared initial values          ← only when the declaration says "always reset"
4. Run inputs                      (highest)
```

Step 3 is the subtle one. Each declared variable chooses how its initial value behaves:

- **Keep the value saved by previous runs** (`SeedOnly`, the default) — the initial value only
  fills a gap. If a previous run persisted something, that wins.
- **Always reset to this value** (`AlwaysOverride`) — the initial value applies on every run,
  overwriting whatever was stored. Use it for counters and flags that must start from a known state.

Run inputs always win, so an ad-hoc run stays easy to steer.

## Declaring variables

In the designer, click empty canvas to select the workflow, then use the **💾 Variables** section
of the properties panel.

Each declaration carries:

| Field | Meaning |
| --- | --- |
| **Name** | Must start with a letter or underscore, then letters, numbers, underscores or dots. Names are **case-insensitive at run time**, so `count` and `Count` are the same variable — the designer refuses to declare both. |
| **Type** | Drives the editor and produces a warning if the initial value contradicts it |
| **Initial value** | Optional. See the precedence rules above |
| **Description** | Shown as a tooltip in the binding picker |
| **Seed mode** | "Keep the value saved by previous runs" vs "Always reset to this value" |
| **🔒 Secret** | Marks a credential. See [Secrets](#secrets--current-limits) |

The panel shows how many nodes reference each variable, and renaming one offers to rewrite every
reference in the same undoable step — so undo can never leave tokens pointing at a variable that no
longer exists.

### Declaring is a contract

A declared variable **exists for the whole run**, even when nothing supplies a value — it simply
starts as `null`. A reference to it resolves rather than failing.

An **undeclared** name is different: referencing it fails the node. So declaring a variable is how
you opt out of hard failure, and the designer warns when a declaration has no initial value and
nothing sets it (the usual cause is a forgotten run input).

## Supplying values when you run

**▶ Run** opens a form generated from the workflow's declarations: one field per variable, typed,
pre-filled with the declared initial value, with the description as help text.

- **Leave a field blank** to use the value the workflow already has — blank means "don't send it",
  not "set it to empty".
- Switch to **JSON** for ad-hoc inputs the workflow doesn't declare.
- **Variable writes** chooses what happens to variables this run writes:
  - *This run only* (default) — they vanish with the run
  - *Save for future runs of this workflow* — they persist to workflow scope
  - *Both*

## Referencing variables

```text
{{Variable.apiBaseUrl}}             a workflow, global or run-supplied variable
{{Variable.user.name}}              a path into an object value (the root name is the variable)
{{nodeId.portName}}                 an upstream node's output
{{input}}                           the value arriving on this node's own 'input' port
{{input.Thing.Id}}                  a path into that incoming value
{{item}}                            the current item, inside a streaming region only
Order {{Variable.id}} shipped       tokens embed inside plain text
{{Variable.count > 5}}              expressions evaluate (sandboxed JavaScript)
\{\{not a token}}                   an escaped literal — renders as {{not a token}}
```

A token that is the **entire** value keeps its resolved type (a number stays a number). A token
embedded in longer text interpolates to a string.

`input` is a **reserved root**: it always means *this node's own incoming value*, so you can wire
any step into (say) an HTTP Request and use `{{input.orderId}}` in the URL or body without knowing
the upstream node's id. (`{{nodeId.port}}` still works when you want to be explicit, and it's the
way to reach nodes further upstream.)

**Which nodes `{{nodeId.port}}` can reach:** any node *upstream* of this one, meaning any node with
a connection path into it, however many steps back, once that node has run. This includes reaching
out of a loop / branch / try body. A node inside a For Each body can read `{{auth.token}}` from a
step that ran before the loop. A node that isn't upstream (a parallel sibling branch, or anything
downstream) can't be read. Its output isn't guaranteed to exist yet, so the reference fails the
node, and the designer says so. The designer offers to **wire it in** (connect it to a free input
port) or **pass it via a variable** (add a Set Variable after it). A reference to an upstream node
that was skipped in this run also fails, with a message saying so. `{{input}}` requires a connection into the node's
`input` port — without one it fails the node, per the rule below.

**Values arriving on a node's ports are not variables.** `{{Variable.count}}` always means the
variable `count` — even when the node also has an input port called `count`. Read the value on a
port with `{{input…}}` (the `input` port) or `{{nodeId.port}}` (the upstream output wired into it).
The designer flags a `{{Variable.x}}` in a node whose port `x` is wired and offers to **use the
incoming value**, **keep the variable**, or **rename the variable**; saving warns about it too.

**Inside a construct body** the construct hands its body a few values as *body variables* — visible
only inside that body, never saved, and shadowing a workflow variable of the same name there:

| Body | Variables |
| --- | --- |
| For Each `loopBody`, Fan Out `branch` | `{{Variable.item}}` (the current item), `{{Variable.index}}` (from 0) |
| Try/Catch `catch` | `{{Variable.error}}` — e.g. `{{Variable.error.message}}`, `{{Variable.error.errorType}}` |

The picker lists them under **This body**.

`item` is the other reserved root: inside a **streaming region** it means *the item currently
flowing through*. It's offered by the picker only there, and using it outside a region is a
design-time error — see [streaming regions](designer.md#streaming-regions-) in the designer guide.

The designer's `{{x}}` button lists everything referenceable from the selected node — this node's
own input (`{{input}}`, first in the list when wired), workflow variables, globals, and upstream
node outputs — with each entry's type and description. **Merged** upstream nodes expand one level
deeper (`{{http-1.output.statusCode}}`…), and a `named`-mode FanIn expands into its computed
branch keys. The **Incoming data** section of the properties panel shows the same shape at a
glance — click any key to insert its token into the field you're editing (it lands on the
clipboard when no field has focus). The **ƒx builder** composes comparisons for you.

### Failure is not silent

A reference that can't resolve **fails the node**. That's deliberate: passing `{{Variable.typo}}`
through as literal text produced bugs that surfaced much later and much less legibly. If you want
literal braces, escape them as `\{\{`.

## Which fields expand templates

Only fields whose module schema sets `SupportsTemplates` are expanded. Everything else is passed
through byte-for-byte.

| Surface | Expands? | Why |
| --- | --- | --- |
| Node **properties** that opt in (HTTP url, file paths, log messages, connection ids…) | ✅ | The authored configuration surface |
| SQL `query` / `command` | ❌ | Values bind through parameters, never string concatenation |
| Script bodies, Linq user code | ❌ | Code is code; scripts read variables through their own API |
| Module **input ports** (default) | ❌ | An input's value is upstream data, not authored text |
| SQL parameter **values** | ✅ | Resolved, then bound as parameters — never concatenated |

The input-port default is a security boundary, not a convenience: if inputs expanded, an HTTP
response or database row containing `{{Variable.apiKey}}` could make untrusted data read your
workflow's variables. A module author can opt an individual port in, but the default is off.

Because the designer drives its `{{x}}` picker and ƒx button from the *same* flag the engine uses,
the UI can never invite a binding into a field the engine leaves literal.

## Design-time checks

The designer lints bindings as you edit:

| Situation | Result |
| --- | --- |
| Declared, or a known global | clean |
| Written by an **upstream** `SetVariable` node | clean |
| Written by a `SetVariable` that **isn't upstream** | ⚠️ warning — it may not have run yet |
| Nothing declares or writes it | ❌ error — this node will fail at run time |
| Declared, but no initial value and nothing sets it | ⚠️ warning — starts as `null` |
| `{{…}}` naming a node that doesn't exist | ❌ error, with an offer to escape it as `\{\{` |
| `{{nodeId.port}}` naming a node that isn't upstream of this one | ❌ error, with optional fixes: wire it in / pass it via a variable |
| `{{Variable.x}}` in a node whose input port `x` is wired | ⚠️ warning if `x` is a variable, ❌ error if not — with fixes: use the incoming value / keep the variable / rename it |
| `{{Variable.item}}` / `index` / `error` inside the matching body | clean |

Bound values show a 🔗 badge; unresolvable ones show ⚠️ **unresolved** instead, so a broken binding
never looks wired up. When *every* reference in a value is unresolvable — a Mustache template
pasted into a templated field, say — the editor offers a one-click **escape as literal**.

## Worked example

An order-sync workflow that runs nightly.

**1. A global for the environment.** In *Settings → Global variables* (Admin only), add
`apiBaseUrl` = `"https://api.example.com"`. Every workflow can now reference it.

**2. A workflow variable that remembers.** Declare `lastRunAt`:

- Type `String`, no initial value
- Seed mode: **Keep the value saved by previous runs**

**3. A per-run input.** Declare `orderId`, type `String`, description "which order to sync".

**4. Wire it up.** On the HTTP node set the URL to:

```text
{{Variable.apiBaseUrl}}/orders/{{Variable.orderId}}?since={{Variable.lastRunAt}}
```

**5. Record the run.** Add a `SetVariable` node writing `lastRunAt`, downstream of the HTTP node.

**6. Run it.** In the run dialog, fill `orderId`, and set **Variable writes** to *Save for future
runs of this workflow*. On the next run, `lastRunAt` is already populated — the `SeedOnly`
declaration steps aside for the stored value, exactly as intended.

## Secrets — current limits

A variable can be marked 🔒 **secret**. In the designer, secrets are masked and cannot carry an
initial value (a workflow definition is exported and version-controlled, so it must never contain a
credential). The run dialog renders them as password fields.

**At run time, a secret's value is hidden (shown as `••••••`) before it reaches anything people can
read:**
- execution and node history (inputs, outputs, error text);
- the run's own variable-write records;
- debug logs;
- the failure messages sent to live clients.

It's hidden **by name and by value**:
- any key named like a secret variable is masked;
- the secret's actual value is replaced wherever it appears inside other text, e.g. a URL or header
  a module echoes back;
- a new value written to a secret variable mid-run is hidden from then on;
- values shorter than 4 characters are only hidden by name.

**Not covered yet:**
- values saved with **Save for future runs** (the workflow/global variable store);
- the engine's internal resume snapshots;
- anything a module logs by itself.

These are stored as-is; see below.

> 🔒 **Globals are not for credentials yet.** Global values are stored and returned in **plaintext**,
> and anyone who can read workflows can read them. Encryption at rest, redaction from execution
> history, and audited reveal are tracked separately — see the secrets plan in
> `use-testing-feedback/Designer-Workflow-Variables-Secrets-Plan.md`. This warning will be removed
> when that work lands.

## See also

- [Visual Designer](designer.md) — the properties panel, binding picker and ƒx builder
- [REST API › Variables](rest-api.md#variables--apiv1variables) — the scoped endpoints
- [Scripting Engine](scripting.md#inline-expressions-in-property-bindings) — expression semantics
- [Module Author Guide](module-author-guide.md) — opting a property into template expansion
