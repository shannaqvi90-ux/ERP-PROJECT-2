# p00 round 14: permissions from every client address; path-taking file APIs; ISO 4217; shared memory

Date: 2026-10-22. Piece: p00-foundation. Status: accepted.

## 1. G2 from every client address (critic p00 round 13, biggest gap)

The critic planted P11 in the kernel's `PermissionAuthorizationHandler`: any signed-in request
whose connection address is loopback passes every permission ("jobs and the reverse proxy on this
machine are trusted"). With it a user with no roles read every user and role, and every gate passed,
because every test request reached the in-memory test server with no client address at all. Behind
nginx or Caddy on the same host every browser request reaches the app from 127.0.0.1, so such a rule
would hand every clerk the owner's rights.

### Choice: two layers, both in G2

**Behaviour: the matrices run from every client origin** (`tests/Erp.Gates.Tests/G2/G2ClientOriginTests.cs`).

- `ClientOrigin` (`tests/Erp.Testing/ClientOrigin.cs`) names where a request comes from: the TCP
  peer, an optional `X-Forwarded-For`, and the client address the product should end up with. Every
  test host (`ErpAppFactory`) has a startup filter, first in the pipeline, that sets
  `HttpContext.Connection.RemoteIpAddress` from a test-only header and removes the header before
  anything else reads the request. The product's own forwarded-header middleware then runs exactly
  as deployed. The header is honoured by test hosts only (the filter lives in `Erp.Testing`).
- The origins: the in-memory server's own (none), ordinary clients (IPv4, IPv6, an office LAN
  address), loopback in four spellings (127.0.0.1, 127.0.0.53, ::1, ::ffff:127.0.0.1), the compose
  network's gateway (172.18.0.1) and every gateway in this machine's routing table, the configured
  proxy without a forwarded address, the proxy forwarding an ordinary client, the proxy forwarding a
  loopback caller, an address of a declared shared network, and an ordinary client that claims
  loopback in its own `X-Forwarded-For` (which the product must ignore). That is 14 fixed origins
  plus the machine's gateways (15 on the owner's PC).
- The matrices run over a second host of the G2 environment's database, configured the way a
  deployment behind a proxy and an office NAT is (`Erp:Http:KnownProxies`, `Erp:Auth:SharedAddresses`):
  - a check that each origin reaches the product as its address: a session signed in from it
    records exactly that address (the proxy's forwarded client, not the proxy; the spoofer's own
    address, not the loopback it claims). Without it a host that ignored the origin header would
    make every matrix below a silent repeat of the in-memory one;
  - deny-everything: a user with no roles signs in from each origin in turn and every permissioned
    endpoint answers 403 through every carrier from that origin (15 x 110 x 2 = 3,300 requests);
    the requests naming every id of the caller's own come from the origins in turn (1,872);
  - anonymous callers get 401 from every endpoint from every origin, and an unknown token through
    every carrier with the origins in turn;
  - grant-exactly-one: one user per permission, the sign-in from the next origin and each request
    from the origin after that (so every permission meets every origin and every endpoint meets every
    origin across the permissions), plus the own-id requests at the module's other endpoints
    (5,500 and 5,072 requests).
- Self-tests (`AddressTrustModule`, one planted host whose mode is switched between runs, so one
  host start serves all five): `loopback` (the critic's P11), `private` (loopback and private
  networks: the office LAN, the compose gateway, the proxy), `shared` (the declared shared network),
  `forwarded` (anything the proxy forwarded) and `held-loopback` (loopback callers who hold any
  permission: only the grant matrix can see it). The deny matrix from every origin must report each
  of the first four from exactly the origins whose address (after the product's proxy handling) the
  plant trusts, never from the spoofer and never from the in-memory origin; the grant matrix must
  report `held-loopback` from loopback origins only, the deny matrix staying clean.
- The critic's P11 itself, put back into the kernel handler by hand on this branch: the deny matrix
  from every origin and the grant matrix both fail (`[cookie@loopback] GET /api/identity/companies: 200`
  and the like); with the handler restored they pass.

**Compiled code: no permission decision reads where a request comes from**
(`tests/Erp.Gates.Tests/G2/G2ClientAddressCodeTests.cs`). The scan reads the product's IL: every
method that reads the connection's addresses, ports or client certificate, the connection feature
(directly or through `Features.Get<IHttpConnectionFeature>()`), the forwarded-header machinery, or
names a forwarding header (`X-Forwarded-For`, `X-Real-IP`, `Forwarded`, `CF-Connecting-IP` and the
like) in a string, is a reader. Readers are reviewed one by one in
`tests/Gates/client-address-readers.txt` (five today: the sign-in rate limit's partition and client,
the sign-in history's client, and the forwarded-header set-up). Separately, and not reviewable, no
reader may be reachable from the authentication and authorization code: every product type that is
an authorization handler, requirement, policy provider or evaluator, an authentication handler or a
claims transformation, following calls into product code, interface and virtual dispatch to every
product implementation, lambdas, local functions and async state machines (110 or more methods on
that path today; ratchet `g2.authorizationPathMethods`). Its self-test plants six handlers that read
the address directly, through a helper, through the connection feature, through an interface, in an
async method, and through the forwarded-header defaults; each must be reported. P11 put into the
kernel by hand fails it: "PermissionAuthorizationHandler.HandleRequirementAsync decides on where a
request comes from".

Alternatives not taken: running only the critic's loopback client (one plant shape; a rule that
trusts the office LAN or the proxy would pass); multiplying every G2 matrix by every origin (the
grant matrix alone would grow fifteen-fold; rotating origins per request covers every pairing of
permission and origin and of endpoint and origin at no extra requests).

Ratchet keys added: `g2.clientOrigins` 14, `g2.clientOriginDenyRequests` 3,080,
`g2.clientOriginSelfRequests` 1,800, `g2.clientOriginUnauthenticatedRequests` 2,200,
`g2.clientOriginGrantRequests` 5,500, `g2.clientOriginGrantSelfRequests` 4,900,
`g2.clientOriginPlantsCaught` 5, `g2.authorizationPathMethods` 110,
`g2.clientAddressCodePlantsCaught` 6.

## 2. State outside the process through path-taking overloads (critic p00 round 13, finding 2)

Plant L10 kept list totals in `/tmp/erp-warm-totals.xml` through `XDocument.Load(string)` and
`XDocument.Save(string)`. The live G1 gates caught it; both static gates missed it, because they knew
outside-process APIs by declaring type.

- Compiled code (`G1OutsideProcessStateTests`): any framework or package method (not the product's
  own, not `System.IO.Path`'s arithmetic) that takes a string parameter named as a path (`path`,
  `fileName`, `filePath`, `outputFileName`, `inputUri` and `uri` of the XML APIs, `subpath`,
  `physicalPath` and the like) is a use: `XDocument`/`XElement` `Load` and `Save`, `XmlReader` and
  `XmlWriter.Create`, `XmlDocument` `Load` and `Save`, a PDF or image library's file open, a
  configuration file. New HTTP clients (constructors of `HttpClient` and its handlers,
  `IHttpClientFactory.CreateClient`, `WebClient`, `WebRequest`, `SmtpClient`, `Ping`) are uses too: a
  client of one's own to another server keeps state there. A client the caller is handed (the
  isolation probes' test clients) is not. The self-test plants nine more shapes (33 in all; ratchet
  `g1.outsideProcessPlantsCaught` 24 to 33) and keeps the in-process ones unflagged
  (`XmlReader.Create(Stream)`, `XDocument.Parse`, `XDocument.Load(Stream)`, `Path.GetFileName`).
  The one new product use, `UseErpPlatform`'s `MapFallbackToFile("index.html")` (the app shell from
  the read-only web root), is reviewed in `tests/Gates/outside-process-methods.txt`.
- Source (`TenantBypassScanner`, rule `outside-process`): `XDocument|XElement|XmlDocument.Load(`,
  `XmlReader|XmlWriter.Create(`, `new Xml(Text)Reader|Writer(`, `XslCompiledTransform`, `.Save(` on a
  string or a `Path` expression, `new HttpClient(` and its handlers, `IHttpClientFactory`,
  `AddHttpClient`, `WebClient`, `WebRequest`, `SmtpClient`. Nine planted lines, the critic's two
  among them.

Following each called framework method's IL down to the file APIs it uses was tried on paper and
not taken: `XDocument.Load(string)` reaches the file system three calls down
(`XmlReader.Create` -> `XmlReaderSettings` -> `XmlUrlResolver` -> `FileStream`), and following that
deep flags framework internals that read their own configuration. The parameter-name rule catches
the overloads by what they take.

## 3. Currency codes from ISO 4217 as published (still open from round 12)

`CurrencyCode` accepted any three capital letters. It now accepts exactly the codes of ISO 4217
List One that have minor units, and takes `MinorUnits` (and the numeric code) from that list
instead of a hand-written table. `src/Kernel/Erp.Kernel/Money/Iso4217.g.cs` is generated by
`tools/iso4217/generate.mjs` from the list published by the standard's maintenance agency (SIX
Group, <https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml>),
publication date 2026-09-17, downloaded and checked 2026-10-22: 165 codes. Codes without minor units
(gold, silver, platinum, palladium, SDR, test and no-currency codes) are refused: no company invoices
or keeps accounts in them. Tenancy's base-currency check uses the same table, so a company's base
currency is always one the money type accepts. ISO 4217 is a standard, not a tax, payroll or
statutory rule; the owner-signed rounding rule (docs/compliance/vat-rounding-and-currency.md: AED to
the fils, 2 decimals) is unchanged and agrees with the list.

## 4. Shared memory for the demo database (critic p00 round 13, finding 1)

Docker gives a container 64 MB of `/dev/shm`; PostgreSQL's parallel hash joins ran out of it when
three or more people printed the 100,000-user report at once (HTTP 500, "could not resize shared
memory segment ... No space left on device"). `compose.yaml` gives the db service `shm_size: 1gb`
(a limit, not an allocation: tmpfs pages exist only while such queries run). `DeploymentTests` in
the kernel tests requires at least 512 MB. Tuning parallel workers down instead was not taken: it
would slow every large list and report for everyone to avoid a container default.

## 5. Not taken this round

- Sign-in ceiling behind the compose gateway (routed to p03-identity by the critic).
- The sign-in comparison with Odoo was won in round 13 (1 step against 2; 0.122 s against 1.603 s);
  nothing in this round changes the sign-in path.

## 6. Processor time this round adds

Measured on this machine outside the verify (same build, one test process): the client-origin class
(9 tests) took 1 m 58 s wall and 75 s of user processor time including its host starts and the
environment copy; the compiled-code scan under 1 s; the new kernel tests under 1 s.

This branch's `./erp verify` (commit a7beb228, slot taken 2026-10-22T05:44:32Z, log
verify-builder-p00-foundation-r14.log): passed in 4,296 s with "Ratchet check passed"; processor
time 6,661 s against the 10,500 s maximum (dotnet 4,449, web 1,838, e2e 340, timing 35). Wall time
was not judged (load up to 31 on 16 CPUs: other pieces' verifies ran beside it). Tests: dotnet 764
(736 before: 28 new), web unit 532, e2e 113, compare 486, build 6, all passing, none skipped. Round
13's passing run on this branch took 7,890 processor seconds; the difference is mostly the machine's
other load, not this round (its new tests measure about 75 processor seconds alone). The ratchet's
suite.dotnetTests minimum goes from 736 to 764.

## 7. Shared memory, checked live

`./erp up` (project b-p00-foundation-demo, 100,005 users in alnoor) on this branch: `/dev/shm` in the
db container is 1.0 GB. The critic's probe (`identity.usersByRole`, one administrator, all at once):
CSV 1, 2, 3, 4, 8 and 12 concurrent all 200; JSON 12 concurrent all 200; CSV 24 concurrent all 200
(8.5 s); PDF 4 concurrent all 200 (4.9 s). The database log has no "could not resize shared memory
segment" and the app log no 500. Round 13's figures for the same probe with 64 MB: 3 concurrent, one
500; 12 concurrent, nine 500s.
