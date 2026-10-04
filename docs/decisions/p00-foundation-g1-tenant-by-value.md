# p00 — G1 judges the tenant each request's SQL runs under, by value

Date: 2026-10-04. Piece: p00-foundation (round 5). Status: accepted.

## Context

Round 4's critic planted T1d: the kernel's one reviewed `set_config('app.tenant_id', …)` moved into
a private `ApplyAsync` helper of `ErpDbSession`, called by `BeginAsync` and by a new
`SupportWorkspaceAsync` that looped over the request's headers for `Erp-Support-Workspace` and
rebound the transaction to that tenant. Tenant A's viewer read tenant B's workspace and users, yet
every gate passed:

- the source gate counted `set_config` literals per file, and the refactor kept the count;
- the SQL trace judged which class sent a setting statement (the kernel's session did), never the
  tenant the statement set;
- the request input recorder logged reads by name (indexer, `TryGetValue`, `ContainsKey`) but not
  enumeration, so the attack never learnt the header's name and never sent it.

Its other findings: process-wide state kept outside fields (plant T2, an answer cache in
`AppContext` data) passed the process-state inventory, and the home screen showed the print stamp
on screen.

## Decision

Four additions, each catching a different part of that plant:

1. **The value of every tenant setting is judged** (`TenantBindingRules.TenantValueViolations`).
   Every pool the platform builds takes registered `IDataSourceObserver`s (a new, additive kernel
   extension point; production registers none). The test hosts register `StatementCapture`, which
   uses Npgsql's command, batch and COPY enrichment callbacks to attach each statement's parameter
   values (for statements that name a setting) and backend process to its activity. `SqlTrace`
   parses every `set_config(name, value, is_local)` call and every `SET [LOCAL] name = value` in
   every statement sent inside a request (`SqlSettings`), resolves the value from the statement's
   own parameters, and rules:
   - on an endpoint that requires a permission, `app.tenant_id` may only be set to the signed-in
     principal's tenant (or emptied, which fails closed);
   - anywhere else (sign-in, the session lookup) only to a tenant the kernel's session declared
     binding in the same request (`TracedBind`);
   - no `app.*` setting may be set for the whole connection;
   - a setting whose name, or a tenant whose value, the statement computes is refused, not trusted.
   A statement can run under a tenant only if a statement of its transaction set it (row-level
   security ignores a tenant left by an earlier transaction or set for the connection: the
   `app.tenant_tx` stamp), so judging every setting statement judges the tenant every statement
   runs under. A statement inside a request that carries no capture went through a pool the
   platform did not build, where the values cannot be read: reported (`UnobservedStatements`). The
   trace must have read a signed-in binding's value and an anonymous one's, or it reports itself
   blind. Ratchet minimums: `g1.tenantValuesJudged`, `g1.statementsObserved`.
2. **Enumeration of request inputs is reported.** `RequestInputRecorder` now also records
   enumeration of the headers, query and cookies (`GetEnumerator`, `Keys`, `Values`, `CopyTo`) and
   reads of the raw query string or request target, with the innermost product type that asked
   (frames of the base class library and ASP.NET Core's request accessors are skipped; framework
   code such as the query parser does not count). The HTTP attack fails on any such read by a type
   not reviewed in `tests/Gates/request-input-enumeration.txt` (empty today). The source gate has a
   matching rule, `request-enumeration`.
3. **Tenant-setting code is reviewed method by method.** `G1TenantSettingCodeTests` reads the
   string constants every method body of the product's assemblies loads (IL `ldstr`; async state
   machines, lambdas and local functions count for the method they are written in) and requires the
   methods naming `app.tenant_id` to be exactly those in `tests/Gates/tenant-setting-methods.txt`:
   `ErpDbSession.BeginAsync` and six migrations. T1c's and T1d's `ApplyAsync` is a new method naming
   the setting, so the gate fails until someone reviews it, whatever the per-file counts say.
4. **Process-wide stores outside fields.** The source gate's new `process-global` rule flags
   `AppContext.SetData/SetSwitch`, `AppDomain … SetData`, `Environment.SetEnvironmentVariable`,
   `[ThreadStatic]`, `ThreadLocal<>` and `MemoryCache.Default` in `src/` (plant T2's shape); the
   HTTP attack also fails if any environment variable of the process changed while it ran.

Self-tests: `LeakyModule` gains `leaky.support` (T1d's shape: a header with a name that changes
every run, found by enumerating the headers, rebinding with `set_config` on the request's own
connection) and `leaky.ownPool` (a pool built outside the platform). The HTTP attack's self-test
requires the enumeration by `LeakyModule`, the unobserved statements of `/api/leaky/own-pool` and
value violations ("SQL ran under tenant …") on `leaky.acting`, `leaky.byHeader`, `leaky.byRoute` and
`leaky.report`, and nothing outside `/api/leaky/`. The scanners' self-tests plant T1d's loop over
the headers, `AppContext.SetData` and `ThreadLocal`; the method gate's self-test plants an async
helper, a lambda and a plain method naming the setting.

The print stamp: `.print-footer` (a later rule of the same specificity) overrode `.print-only`, so
the "printed at / by" footer of every wrapped screen showed on screen. `.print-footer.print-only`
is now hidden on screen and shown in print; an end-to-end test checks both media.

## Checked against the critic's plant

T1d re-created on this round's kernel (the binding statement moved into a private
`ErpDbSession.ApplyAsync`, `UnitOfWorkFilter` calling `SupportWorkspaceAsync`, which loops over the
request headers for `Erp-Support-Workspace` and rebinds): the compiled-code gate fails
("`Erp.Kernel.Data.ErpDbSession.ApplyAsync` names the tenant setting … only reviewed methods may",
and `BeginAsync`'s entry is stale), and the source gate fails ("`ErpDbSession.cs:146` enumerates a
request's headers … [request-enumeration]"). At run time the same shape is the planted
`leaky.support` endpoint, which the HTTP attack's self-test requires to be reported as an
enumeration by `LeakyModule`; a rebind the attack does trigger (T1c's shape, `leaky.acting`, and
`leaky.silent`, which shows no tenant B data at all) is reported as "SQL ran under tenant B but the
signed-in principal's tenant is A".

Re-checked after the round was resumed (commit `ace941a`, T1d rebuilt on the current kernel in a
scratch worktree): `G1TenantSettingCodeTests` and `G1TenantSourceTests` fail with exactly those
two messages; the real kernel passes both.

With the real product, the HTTP attack judged about 240,000 tenant values among about 2.3 million
statements observed with their parameters, and found none set to another tenant.

## Why not

- **Reading the bound tenant back from the connection after each statement**: Npgsql does not let
  another command run on a connection while one is executing, and the enrichment callback runs
  inside the command; reading it at commit would miss a rebind that is undone before commit.
- **Npgsql's global logging with parameter values**: data sources built with
  `NpgsqlDataSourceBuilder` do not use the global logging configuration (measured: nothing was
  logged), so it would see none of the platform's statements.
- **Forbidding enumeration outright** in the recorder: a reviewed list keeps the door open for a
  later module that has a legitimate reason (and makes it say why).
- **Running the non-interference "fresh process" in a separate OS process** (critic's other
  suggestion for T2): the source rule and the environment check catch the stores T2 used; a
  separate process remains a possible further step.
