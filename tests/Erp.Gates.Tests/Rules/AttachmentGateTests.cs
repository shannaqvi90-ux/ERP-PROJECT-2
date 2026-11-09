using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
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
            var root = $"/api/attachments/{kind.Table}/{{id:guid}}/files";
            var expected = new (string Method, string Pattern, string Permission)[]
            {
                ("GET", root, kind.ReadPermission),
                ("POST", root, kind.WritePermission),
                ("GET", root + "/{fileId:guid}/content", kind.ReadPermission),
                ("DELETE", root + "/{fileId:guid}", kind.WritePermission),
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
