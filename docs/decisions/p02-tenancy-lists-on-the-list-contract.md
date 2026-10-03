# p02 — Tenancy lists on the shared list contract; a list served by another module's rows

Date: 2026-10-03. Piece: p02-tenancy. Status: accepted.

## Decision

The three tenancy lists use p05's list framework end to end:

- `tenancy.companies` and `tenancy.branches` register `ListBinding<Company>` / `ListBinding<Branch>`
  (`Companies/CompaniesList.cs`, `Branches/BranchesList.cs`). Their GET endpoints take
  `[AsParameters] ListRequest` and return `ListPage<CompanyRow>` / `ListPage<BranchRow>`: word search,
  the filter language, sort, keyset and offset paging and grouping. Row-level security (tenant and
  company scope) decides which rows exist; the binding never sees a tenant or a company.
- The old ad-hoc parameters (`isActive=`, `companyId=`) are gone; the same questions are filters
  (`filter=isActive eq true`, `filter=companyId eq '…'`), which saved views and the API share.
- A branch's company is a reference column (`companyId`), filterable and groupable. The branch
  count of a company is shown but not sortable: it is computed, and the index gate only accepts
  sortable columns backed by a `(tenant_id, column, …)` index.
- Base currency is a text column (filterable, groupable): any ISO 4217 code is valid, so it has no
  fixed choice list. Emirate is a choice column over the seven emirates.
- The companies list queries a projection of the row columns (`CompanyEndpoints.ListRows`), not
  the whole entity: a company's logo (up to 512 KB in `logo`) is never read for a list page. Search,
  filters, sort, keyset paging and grouping compose over the projection (EF Core binds the member
  initialiser), and a test checks the list SQL never names the logo columns.
- Indexes: GIN `gin_trgm_ops` over the searched columns (code and both names) and
  `(tenant_id, column, id)` B-trees for each sortable column (migration `ListIndexes`).

`tenancy.access` lists users, and users belong to identity. Tenancy may not read identity's tables,
so the kernel gained an additive way for one module's list to be served by another module's list:

- `ModuleBuilder.List(definition, servedBy: "identity.users")` registers tenancy's definition (its
  own key, permission, endpoint, columns) and names the list whose binding serves it. When every
  module is registered, the catalogue gives the served list a binding with `IListBinding.ServeAs`:
  the same rows and bound values, but only the columns the served definition names, and checked
  against it (a column it needs that the serving list does not bind refuses start-up).
- Identity runs the query through its public contract, `IUserDirectory.QueryListAsync(listKey, …)`,
  returning `ListResult<UserSummary>` (`ListResult.Map` keeps the page or the problem). Identity only
  answers for lists bound to its users (`catalog.ListBinding<User>` throws otherwise).
- Tenancy adds the `companies` column for the rows of the page from its own access tables.

On the screens, companies, branches and access are `<ListView listKey=…>` screens with the record's
form in the list's panel (`?open=id`, `?open=new`). The list screen gained an additive
`references` property: labels for a reference column's ids (cells, group headings, filter chips) and,
for a short set, the choices its filter offers. The branches screen uses it to show and filter by
company code and name instead of ids.

## Why

One query contract for every list (p05) means saved views, the API, export and the gates treat the
tenancy lists like any other. Serving the access list from identity's binding keeps the module
boundary (no cross-module table reads, no copied user table that could go stale) while keeping a
user list of any size fast (identity's trigram and keyset indexes serve it).

Alternatives rejected: an in-memory access list (users can number in the tens of thousands); a
tenancy copy of user names (stale without an event stream, which the platform does not have yet);
a database view over `identity.users` from tenancy (reads another module's tables); dropping the
access list in favour of the users screen (the access screen needs its own permission and its own
companies column).
