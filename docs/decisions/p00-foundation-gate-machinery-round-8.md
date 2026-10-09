# p00: shared gate machinery, round 8 (what the write oracles change, file-local types, documented values, row versions)

Date: 2026-10-09 (round 8)

Findings routed to p00 by the lead from other pieces' critics, each closed in the shared machinery
with one mechanism.

## 1. The write oracles put back what they change (critic p03 round 7, G3)

`G1WriteOracle.RunAsync` wrote tenant B's seeded addresses into tenant A (user creates under the
gate password) and renamed both workspaces ("Name oracle…x"). The id oracle that ran next on the
same environment signed in as tenant B's administrator and was asked to choose a workspace, so
`./erp verify` passed or failed by test order.

- Nothing the oracles send is left out. What a send changed that another test relies on is put
  back through the product's own edit endpoints, so the audit trail records the undo
  (`G1WriteOracleRestore.cs`):
  - a record of tenant A's that a send gave an address tenant B holds (seeded, or just written by
    B through the endpoint) gets a fresh address of tenant A's right after the send
    (`UndoValueAsync`: the record's address comes from the send, the edit endpoint is found by
    matching it against the routes);
  - every record of its own with no id in the route (a workspace's settings), read for both
    tenants before the run, gets its values back after it (`Singletons`), in both oracles.
- An undo the product refuses is a problem of the run, never ignored.
- The text oracle's test asserts afterwards that no address of tenant B's users is left in tenant
  A and that tenant B's administrator signs in without naming a workspace.
- Isolation (one environment per oracle test) was not taken: a gate environment and its
  preparation cost about 60 processor seconds each.
- **Ceremonies:** a write a valid body alone cannot make (adding a passkey answers creation options
  with a device's attestation) is completed for the caller before it is sent
  (`Infrastructure/Ceremonies.cs`), so the oracle judges the passkey's name like any other name.
  `GET /api/identity/me/passkeys/{id}` was added so an edit carries the passkey's version.

## 2. One test for compiler-generated types; file-local types judged (critic p05 round 7, plant L11)

The process-state gates skipped every type whose name holds '<'. A C# file-local class compiles to
`<File>F<hash>__Name`, so its statics were never roots. `Infrastructure/CompilerGenerated.cs` is the
one test, used by `G1ProcessStateTests`, `ReachableState` and `EndpointClosures`: generated means
`[CompilerGenerated]`, `[GeneratedCode]`, the regular-expression generator's namespace, or a
compiler name starting with '<' that is not a file-local name. Framework-singleton holders stay as
p05 built them (`ProductContext.FrameworkHolders`, `ReachableState.FrameworkHolder`); nothing
second was added. Self-test plant: a file-local static class in the leaky module keeping list totals
by search, filter and sort.

## 3. The answer-shape victim uses every documented parameter (critic p06 round 4, plant L6)

Tenant B asked for every enumerated value (format, language, digits, grouping, disposition) and one
free-text query, so a store keyed on columns, filter, sort or time zone held nothing of B's. Now
every documented query parameter that is neither enumerated nor a record id gets values other than
its default: each example the API document gives (`OpenApiDocument` now reads `example` and
`examples` of parameters), a part of a list example (the first item; the first half), the other
direction of a sort example, both answers of a yes/no parameter, two dates, and UTC for a time
zone. Each such value is asked with every value of every enumerated parameter at least once (rows
in which each value occurs once, not the full product: about the size of the largest enumeration
per value). Tenant A then asks for the same shapes. Ratchet `g1.shapeDocumentedValues`. Self-test
plant L6: a print spool keyed on the columns and format, with no tenant, that never spools the
default columns.

## 4. Row versions say nothing about other tenants' writes (critic p03 round 6)

Versions were PostgreSQL's `xmin`, a database-wide transaction counter. They are now a keyed
32-bit permutation of it (`Erp.Kernel.Data.RowVersions`: an eight-round Feistel network whose
round functions are tables drawn from AES with the key), applied by an EF Core value converter on
`TenantEntity.Version`. It is a bijection, so every module's optimistic concurrency works unchanged,
with no migration. The key is `ERP_ROW_VERSION_KEY` when set, else random per process (like the
device key). Gate `G1RowVersionTests`: no version tenant A is shown is a transaction id of any
tenant row, and two of A's records written around three of B's are not as close as the
transactions between them.

## Processor time this round adds (estimates from targeted runs on this machine)

- Write-oracle undo and restore: one GET and one PUT per accepted address write of tenant A and per
  workspace record, under 1 s.
- Documented values: about 60 extra rendered shapes per print route for tenant B and the same for
  tenant A; measured in the HTTP attack's run (see the round's verify).
- Row versions: tables drawn once per process (1 MB of AES, about 2 ms); two table lookups per
  version read or written.
- G1RowVersionTests: one pass over the list endpoints and the tenant tables, about 2 s.
