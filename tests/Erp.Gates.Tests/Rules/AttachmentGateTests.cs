using System.Reflection;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// Attachments (p11): a file is as private as its record, its bytes are stored under names the
/// server makes, and the registration must be complete or files silently reach the wrong people.
/// (1) Every kind of record that takes files (<c>ModuleBuilder.AttachmentRecord</c>) has exactly the
/// four endpoints (list, upload, content, delete) under <c>/api/attachments/{table}/</c>, reads guarded by
/// the kind's read permission and writes by its write permission, which differ, and each file endpoint
/// is marked as a file surface so the isolation gate's file probe covers it; the kind is a registered
/// kind of audited record's schema's neighbour: <c>attachments.files</c> is audited, so every upload and
/// delete is in the trail. (2) The file system is touched only by the store (source scan): no other file
/// of the module names a path API, and the store builds a path only from the tenant identifier and the
/// identifier it made. (3) The database refuses a row whose key belongs to another tenant. (4) The
/// size, count and type limits exist where the endpoints read them.
/// </summary>
public sealed class AttachmentGateTests(GateFixture fixture)
{
    private static readonly string[] PathApis = [@"\bPath\.(Combine|Join|GetFullPath)\b", @"(?<![\w.])File\.", @"(?<![\w.])Directory\.", @"\bnew FileStream\b", @"\bFileInfo\b", @"\bDirectoryInfo\b", @"\bnew StreamWriter\b", @"\bnew StreamReader\(", @"\bProcess\.Start\b"];

    [Fact]
    public void Every_kind_of_record_with_files_has_its_four_endpoints_under_its_own_permissions()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var endpoints = EndpointInventory.From(fixture.Env.Factory.Services);
        var problems = new List<string>();
        var checks = 0;
        Assert.NotEmpty(catalog.AttachmentRecordTypes);
        foreach (var kind in catalog.AttachmentRecordTypes)
        {
            var root = $"/api/attachments/{kind.Table}";
            var expected = new (string Method, string Pattern, string Permission)[]
            {
                ("GET", root + "/{id:guid}/files", kind.ReadPermission),
                ("POST", root + "/{id:guid}/files", kind.WritePermission),
                ("GET", root + "/files/{id:guid}/content", kind.ReadPermission),
                ("DELETE", root + "/files/{id:guid}", kind.WritePermission),
            };
            if (kind.ReadPermission == kind.WritePermission)
            {
                problems.Add($"{kind.Table}: reading and changing files share {kind.ReadPermission}; a reader must not be able to add or delete");
            }
            foreach (var (method, pattern, permission) in expected)
            {
                checks++;
                var found = endpoints.SingleOrDefault(e => e.Method == method && e.Pattern == pattern);
                if (found is null)
                {
                    problems.Add($"{kind.Table}: no {method} {pattern}");
                    continue;
                }
                if (found.Permission != permission)
                {
                    problems.Add($"{found.Key} declares {found.Permission}, the kind names {permission}");
                }
                if (method != "GET" || pattern.EndsWith("/content", StringComparison.Ordinal))
                {
                    if (found.Surface != SurfaceKind.File)
                    {
                        problems.Add($"{found.Key} is a {found.Surface} surface; files are reached through the file probe");
                    }
                }
            }
            var all = endpoints.Where(e => e.Pattern.StartsWith($"/api/attachments/{kind.Table}/", StringComparison.Ordinal)).Select(e => e.Key).Except(expected.Select(x => $"{x.Method} {x.Pattern}")).ToList();
            if (all.Count > 0)
            {
                problems.Add($"{kind.Table}: endpoints beyond the four reviewed ones: {string.Join(", ", all)}");
            }
            if (catalog.FindAuditRecordType("attachments.files") is null)
            {
                problems.Add("attachments.files is not a registered kind of audited record: uploads and deletes would show to no one");
            }
        }
        var strays = endpoints.Where(e => e.Pattern.StartsWith("/api/attachments/", StringComparison.Ordinal) && !catalog.AttachmentRecordTypes.Any(k => e.Pattern.StartsWith($"/api/attachments/{k.Table}/", StringComparison.Ordinal))).Select(e => e.Key).ToList();
        if (strays.Count > 0)
        {
            problems.Add($"attachment endpoints of no registered kind of record: {string.Join(", ", strays)}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checks >= Ratchet.Min("rules.attachmentChecks"), $"{checks} attachment checks; ratchet minimum {Ratchet.Min("rules.attachmentChecks")}");
    }

    [Fact]
    public void Only_the_store_touches_the_file_system_and_it_builds_paths_only_from_names_it_made()
    {
        var problems = new List<string>();
        var files = MoneyGateTests.SourceFiles("src/Modules/Attachments", "*.cs").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)).ToList();
        Assert.True(files.Count >= 5, $"{files.Count} source files of the attachments module");
        foreach (var file in files.Where(f => Path.GetFileName(f) != "LocalAttachmentStore.cs"))
        {
            problems.AddRange(PathApiUses(Path.GetRelativePath(Repo.Root, file), File.ReadAllLines(file)));
        }
        problems.AddRange(StoreProblems(File.ReadAllText(Repo.PathOf("src", "Modules", "Attachments", "Erp.Modules.Attachments", "LocalAttachmentStore.cs"))));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Uses of a path or file API in a file that is not the store.</summary>
    internal static IEnumerable<string> PathApiUses(string name, IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var code = Regex.Replace(lines[i], "//.*$", "");
            foreach (var api in PathApis.Where(a => Regex.IsMatch(code, a)))
            {
                yield return $"{name}:{i + 1}: '{api}' outside the store: a path built here could come from a client name";
            }
        }
    }

    /// <summary>The store builds every path from the tenant identifier, an identifier it made, and a parsed key; it reads a key only in the
    /// anchored 32-hex form of its own tenant and creates files without overwriting.</summary>
    internal static IEnumerable<string> StoreProblems(string store)
    {
        var allowed = new[] { "_root,tenantId.ToString(\"N\")", "folder,id", "FolderOf(tenantId),match.Groups[2].Value", "AppContext.BaseDirectory,\"attachments-data\"" };
        foreach (var combine in Regex.Matches(store, @"Path\.(Combine|Join)\(([^()]*(?:\([^()]*\)[^()]*)*)\)").Select(m => m.Groups[2].Value.Replace(" ", "")))
        {
            if (!allowed.Contains(combine))
            {
                yield return $"LocalAttachmentStore builds a path from '{combine}': only the tenant identifier, a generated identifier and the parsed key are allowed";
            }
        }
        if (!store.Contains("\\z", StringComparison.Ordinal) || !store.Contains("[0-9a-f]{32}", StringComparison.Ordinal))
        {
            yield return "the store's key pattern is not exactly two 32-digit hex groups anchored at the end";
        }
        if (!store.Contains("match.Groups[1].Value == tenantId.ToString(\"N\")", StringComparison.Ordinal))
        {
            yield return "the store opens a key without checking it belongs to the calling tenant";
        }
        if (!store.Contains("FileMode.CreateNew", StringComparison.Ordinal))
        {
            yield return "the store may overwrite an existing file";
        }
    }

    [Fact]
    public void The_source_checks_catch_a_client_name_in_a_path_a_path_API_outside_the_store_and_a_loose_key()
    {
        // Planted: an endpoint that saves under the client's name, a helper that opens a file, and stores that trust a key.
        Assert.NotEmpty(PathApiUses("Evil.cs", ["await File.WriteAllBytesAsync(Path.Combine(root, name), bytes);"]));
        Assert.NotEmpty(PathApiUses("Evil.cs", ["using var stream = new FileStream(path, FileMode.Open);"]));
        Assert.NotEmpty(PathApiUses("Evil.cs", ["var all = Directory.GetFiles(folder);"]));
        Assert.Empty(PathApiUses("Fine.cs", ["// File.Delete is the store's business", "var name = raw.Replace('/', '_');"]));
        const string good = "FileMode.CreateNew; [GeneratedRegex(\"^([0-9a-f]{32})/([0-9a-f]{32})\\z\")] if (match.Groups[1].Value == tenantId.ToString(\"N\")) Path.Combine(FolderOf(tenantId), match.Groups[2].Value); Path.Combine(_root, tenantId.ToString(\"N\")); Path.Combine(folder, id);";
        Assert.Empty(StoreProblems(good));
        Assert.NotEmpty(StoreProblems(good + " Path.Combine(_root, clientName);"));
        Assert.NotEmpty(StoreProblems(good.Replace("match.Groups[1].Value == tenantId.ToString(\"N\")", "true")));
        Assert.NotEmpty(StoreProblems(good.Replace("\\z", "$")));
        Assert.NotEmpty(StoreProblems(good.Replace("FileMode.CreateNew", "FileMode.Create")));
    }

    /// <summary>The client inputs each attachment endpoint may read (round 2, gate backlog row 18): the record or file id from the route, the
    /// file's name from a header, and inline on the bytes endpoint. A storage key, a tenant, a version or a path from a client is never an input:
    /// the bytes are found only through the file's row, which row-level security limits to the caller's workspace.</summary>
    private static readonly Dictionary<string, string[]> ReviewedInputs = new()
    {
        ["GET /{id:guid}/files"] = ["id"],
        ["POST /{id:guid}/files"] = ["id", "name"],
        ["GET /files/{id:guid}/content"] = ["id", "inline"],
        ["DELETE /files/{id:guid}"] = ["id"],
    };

    /// <summary>The parameters of a handler that carry something a client sent (route, query, header and body values), by name.</summary>
    internal static IReadOnlyList<string> ClientInputs(MethodInfo handler) =>
        [.. handler.GetParameters().Where(p => IsClientValue(p.ParameterType) || p.GetCustomAttributes().Any(a => a.GetType().Name is "FromQueryAttribute" or "FromHeaderAttribute" or "FromBodyAttribute" or "FromRouteAttribute" or "FromFormAttribute"))
            .Select(p => p.Name!).Order(StringComparer.Ordinal)];

    private static bool IsClientValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(decimal)
            || type == typeof(Uri) || type == typeof(byte[]);
    }

    [Fact]
    public void Attachment_endpoints_read_only_the_reviewed_client_inputs_and_no_storage_key()
    {
        var endpoints = fixture.Env.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").StartsWith("/api/attachments/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(endpoints);
        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var raw = endpoint.RoutePattern.RawText!;
            var method = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()!.HttpMethods.Single();
            var shape = raw[(raw.IndexOf('/', "/api/attachments/".Length) < 0 ? raw.Length : raw.IndexOf('/', "/api/attachments/".Length))..];
            var key = $"{method} {shape}";
            var handler = endpoint.Metadata.GetMetadata<MethodInfo>();
            if (handler is null)
            {
                problems.Add($"{method} {raw}: the handler cannot be inspected");
            }
            else if (!ReviewedInputs.TryGetValue(key, out var expected))
            {
                problems.Add($"{method} {raw}: not one of the four reviewed attachment endpoints");
            }
            else if (!ClientInputs(handler).SequenceEqual(expected.Order(StringComparer.Ordinal)))
            {
                problems.Add($"{method} {raw} reads {string.Join(", ", ClientInputs(handler))} from the client; the reviewed inputs are {string.Join(", ", expected)}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Calls of the store outside the store: the tenant is the caller's (<c>tenant.TenantId</c>, or the seeded tenant), and a key is one read from
    /// the file's row (found under row-level security) or the one the store just made; never a value from the request.</summary>
    internal static IEnumerable<string> StoreCallProblems(string name, IReadOnlyList<string> lines)
    {
        string[] tenants = ["tenant.TenantId", "tenantId", "context.Tenant.Id"];
        string[] keys = ["file.StorageKey", "blob.Key", "key"];
        for (var i = 0; i < lines.Count; i++)
        {
            var code = Regex.Replace(lines[i], "//.*$", "");
            foreach (Match call in Regex.Matches(code, @"\bstore\.(OpenAsync|DeleteAsync|SaveAsync)\("))
            {
                var op = call.Groups[1].Value;
                var args = Arguments(code[(call.Index + call.Length)..]);
                var tenantArg = args.ElementAtOrDefault(0)?.Trim() ?? "";
                var keyArg = args.ElementAtOrDefault(1)?.Trim() ?? "";
                if (!tenants.Contains(tenantArg))
                {
                    yield return $"{name}:{i + 1}: store.{op} takes the tenant from '{tenantArg}'; it must be the caller's (tenant.TenantId)";
                }
                if (op != "SaveAsync" && !keys.Contains(keyArg))
                {
                    yield return $"{name}:{i + 1}: store.{op} takes the key from '{keyArg}'; it must come from the file's row or the store's own answer";
                }
            }
            foreach (Match assignment in Regex.Matches(code, @"\b(?:var|Guid|string)\s+(tenantId|key|file)\s*=\s*(.+?);"))
            {
                var value = assignment.Groups[2].Value.Trim();
                var fine = assignment.Groups[1].Value switch
                {
                    "tenantId" => value == "tenant.TenantId",
                    "key" => value == "file.StorageKey",
                    _ => value.StartsWith("await FindAsync(", StringComparison.Ordinal) || value.StartsWith("await query.SingleOrDefaultAsync(", StringComparison.Ordinal) || value.StartsWith("new AttachedFile", StringComparison.Ordinal),
                };
                if (!fine)
                {
                    yield return $"{name}:{i + 1}: '{assignment.Groups[1].Value}' is set from '{value}'; the tenant is the caller's, the key and the file are the file row's";
                }
            }
        }
    }

    /// <summary>The top-level arguments of a call whose text starts just after its opening parenthesis.</summary>
    private static List<string> Arguments(string text)
    {
        var args = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth == 0:
                    args.Add(text[start..i]);
                    return args;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    args.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }
        args.Add(text[start..]);
        return args;
    }

    [Fact]
    public void The_store_is_called_with_the_callers_tenant_and_a_key_from_the_files_row()
    {
        var problems = new List<string>();
        var files = MoneyGateTests.SourceFiles("src/Modules/Attachments", "*.cs").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && Path.GetFileName(f) != "LocalAttachmentStore.cs").ToList();
        Assert.True(files.Count >= 4, $"{files.Count} source files of the attachments module");
        var calls = 0;
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            calls += lines.Sum(l => Regex.Matches(l, @"\bstore\.(OpenAsync|DeleteAsync|SaveAsync)\(").Count);
            problems.AddRange(StoreCallProblems(Path.GetRelativePath(Repo.Root, file), lines));
        }
        Assert.True(calls >= 6, $"{calls} calls of the store found; the scan is not reading the endpoints");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_input_and_store_call_checks_catch_a_client_supplied_storage_key_a_version_parameter_and_a_foreign_tenant()
    {
        // The plant of round 1's critic: an optional 'version' query value opened as a storage key, its tenant parsed from the key.
        static Task<object> Plant(Guid id, bool? inline, string? version) => Task.FromResult<object>(new());
        static Task<object> Fine(Guid id, bool? inline, AttachmentsDbContextStub db, HttpContext http) => Task.FromResult<object>(new());
        Assert.Equal(["id", "inline", "version"], ClientInputs(((Delegate)Plant).Method));
        Assert.Equal(["id", "inline"], ClientInputs(((Delegate)Fine).Method));
        Assert.NotEmpty(StoreCallProblems("Evil.cs", ["var stream = await store.OpenAsync(tenant.TenantId, version, ct);"]));
        Assert.NotEmpty(StoreCallProblems("Evil.cs", ["var stream = await store.OpenAsync(ParseTenant(version), file.StorageKey, ct);"]));
        Assert.NotEmpty(StoreCallProblems("Evil.cs", ["var key = http.Request.Query[\"version\"];"]));
        Assert.NotEmpty(StoreCallProblems("Evil.cs", ["var tenantId = Guid.Parse(key.Split('/')[0]);"]));
        Assert.NotEmpty(StoreCallProblems("Evil.cs", ["var file = await db.Files.IgnoreQueryFilters().FirstAsync(ct);"]));
        Assert.Empty(StoreCallProblems("Fine.cs", ["var file = await FindAsync(record, fileId, db, http, tracked: false, ct);", "var stream = await store.OpenAsync(tenant.TenantId, file.StorageKey, ct);",
            "var key = file.StorageKey;", "var tenantId = tenant.TenantId;", "await store.DeleteAsync(tenantId, key, CancellationToken.None);", "blob = await store.SaveAsync(tenantId, body, max, ct);",
            "// store.OpenAsync(x, y, z) in a comment"]));
    }

    private sealed class AttachmentsDbContextStub;

    [Fact]
    public async Task The_database_refuses_a_file_row_that_names_another_tenants_bytes_and_a_row_without_a_checksum()
    {
        await using var admin = await fixture.Env.OpenAdminAsync();
        var problems = new List<string>();
        foreach (var (key, sha, why) in new[]
        {
            ($"{fixture.Env.TenantB.Id:N}/{Guid.NewGuid():N}", new string('a', 64), "another tenant's key"),
            ("../../etc/passwd", new string('a', 64), "a path"),
            ($"{fixture.Env.TenantA.Id:N}/{Guid.NewGuid():N}/x", new string('a', 64), "a key with a third part"),
            ($"{fixture.Env.TenantA.Id:N}/{Guid.NewGuid():N}", "not a checksum", "a checksum that is not one"),
        })
        {
            await using var insert = new Npgsql.NpgsqlCommand("""
                INSERT INTO attachments.files (id, tenant_id, record_type, record_id, name, content_type, size_bytes, sha256, storage_key, created_at, updated_at)
                VALUES (gen_random_uuid(), @t, 'contacts.contacts', gen_random_uuid(), 'x.pdf', 'application/pdf', 5, @s, @k, now(), now())
                """, admin);
            insert.Parameters.AddWithValue("t", fixture.Env.TenantA.Id);
            insert.Parameters.AddWithValue("s", sha);
            insert.Parameters.AddWithValue("k", key);
            try
            {
                await insert.ExecuteNonQueryAsync();
                problems.Add($"a row with {why} was accepted");
            }
            catch (Npgsql.PostgresException error) when (error.SqlState is "23514" or "22001")
            {
                // A check constraint, or a key longer than the column holds.
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
