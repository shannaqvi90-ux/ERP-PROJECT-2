# p00 — Dependencies and their licences

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

Every direct and transitive package is checked by `tests/Erp.Gates.Tests/Rules/LicenceGateTests.cs`
(NuGet: nuspec licence expression of every package in `project.assets.json`; npm: `license` of every
package in both lockfiles). Allowed: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, PostgreSQL
(CLAUDE.md rule 6); copyleft and unknown licences always fail. Reviewed exceptions sit in `tests/Gates/licence-exceptions.txt`.

## Direct dependencies

| Package | Licence | Why |
|---|---|---|
| Microsoft.EntityFrameworkCore (+Relational, Design) 10.0 | MIT | ORM (stack) |
| Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL 10.0 | PostgreSQL | PostgreSQL driver and EF provider |
| EFCore.NamingConventions 10.0 | Apache-2.0 | snake_case tables and columns |
| Microsoft.AspNetCore.OpenApi 10.0 | MIT | OpenAPI document from the running app |
| Microsoft.AspNetCore.Mvc.Testing 10.0 | MIT | in-memory host for integration tests |
| xunit.v3 3.x, xunit.runner.visualstudio | Apache-2.0 | tests (stack) |
| Microsoft.NET.Test.Sdk | MIT | test runner |
| Testcontainers.PostgreSql 4.x | MIT | real PostgreSQL in tests (stack) |
| react, react-dom 19 | MIT | UI (stack) |
| typescript | Apache-2.0 | types (stack) |
| vite | MIT | build (stack) |
| vitest | MIT | web unit tests |
| happy-dom | MIT | DOM for unit tests |
| @playwright/test | Apache-2.0 | end-to-end tests (stack) |

## Deliberately avoided

MediatR 13+, AutoMapper 15+, FluentAssertions 8+ (commercial licences); Hangfire (LGPL);
`@vitejs/plugin-react` (pulls Babel/caniuse-lite, CC-BY-4.0); ASP.NET Core Identity (schema
does not fit rule 1). Assertions use xUnit's own `Assert`.

## Pending owner approval

Two transitive npm packages are ISC-licensed (permissive, OSI-approved, functionally MIT-like but
not on the rule 6 list): `picocolors` (terminal colours inside postcss, used by Vite at build time)
and `siginfo` (inside vitest). Neither ships to the browser or server. They are listed in
`tests/Gates/licence-exceptions.txt` and reported as a human gate.
