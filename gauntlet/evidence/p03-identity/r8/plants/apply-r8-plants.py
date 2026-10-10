#!/usr/bin/env python3
"""Critic p03 round 8 plants. Usage: apply-r8-plants.py <clone dir> [L7] [L8] [P1] [P2]
L7  tenant leak (database): an extra PERMISSIVE SELECT policy on identity.passkeys that lets a
    connection with no workspace bound read every workspace's passkeys ("so passkey sign-in can
    find the credential before a workspace is known").
L8  tenant leak (process state): the PasskeyChallenges singleton remembers, per e-mail address,
    the credentials it has offered, and CreationOptions adds them to excludeCredentials. A person
    whose address also exists in another workspace is handed that workspace's credential ids.
P1  missing permission check: DELETE /api/identity/users/{id}/passkeys no longer checks the target
    (TargetProblemAsync), so anyone with identity.users.update strips the Administrator's passkeys.
P2  missing ownership check: DELETE /api/identity/me/passkeys/{id} removes any passkey of the
    workspace, not only the caller's own.
"""
import sys, pathlib, re
root = pathlib.Path(sys.argv[1]); which = set(sys.argv[2:]) or {"L7","L8","P1","P2"}
def sub(path, old, new, count=1):
    p = root / path; s = p.read_text()
    assert s.count(old) >= 1, f"{path}: anchor not found: {old[:60]}"
    p.write_text(s.replace(old, new, count)); print("planted", path)
ep = "src/Modules/Identity/Erp.Modules.Identity/Auth/Passkeys/PasskeyEndpoints.cs"
ch = "src/Modules/Identity/Erp.Modules.Identity/Auth/Passkeys/PasskeyChallenges.cs"
if "L7" in which:
    sub("src/Modules/Identity/Erp.Modules.Identity/Migrations/20261008144935_Passkeys.cs",
        'ignore: ["last_used_at", "last_challenge_at", "sign_count", "backed_up"]);',
        'ignore: ["last_used_at", "last_challenge_at", "sign_count", "backed_up"]);\n'
        '            migrationBuilder.Sql("CREATE POLICY passkeys_before_workspace ON identity.passkeys AS PERMISSIVE FOR SELECT USING (NULLIF(current_setting(\'app.tenant_id\', true), \'\') IS NULL);");')
if "L8" in which:
    sub(ch, "    private byte[] Mac(ReadOnlySpan<byte> payload)",
        "    // Credentials already offered to an address, so the same person is not asked to create a passkey twice.\n"
        "    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<PasskeyDescriptor>> _offered = new(StringComparer.OrdinalIgnoreCase);\n"
        "    public IReadOnlyList<PasskeyDescriptor> Offered(string email, IReadOnlyList<PasskeyDescriptor> mine)\n"
        "    {\n"
        "        var all = _offered.AddOrUpdate(email, _ => mine.ToList(), (_, known) => known.Concat(mine).DistinctBy(d => d.Id).ToList());\n"
        "        return all;\n"
        "    }\n\n"
        "    private byte[] Mac(ReadOnlySpan<byte> payload)")
    sub(ep, 'existing.Select(p => new PasskeyDescriptor("public-key", WebAuthn.ToBase64Url(p.CredentialId), p.Transports.Count > 0 ? p.Transports : null)).ToList(),',
        'challenges.Offered(user.Email, existing.Select(p => new PasskeyDescriptor("public-key", WebAuthn.ToBase64Url(p.CredentialId), p.Transports.Count > 0 ? p.Transports : null)).ToList()),')
if "P1" in which:
    sub(ep, """        if (await UserEndpoints.TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        // Tracked removal""", """        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        // Tracked removal""")
if "P2" in which:
    p = root / ep; s = p.read_text()
    old = """    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(Guid id, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var passkey = await db.Passkeys.SingleOrDefaultAsync(p => p.Id == id && p.UserId == caller.UserId, cancellationToken);"""
    assert old in s
    s = s.replace(old, old.replace(" && p.UserId == caller.UserId", "")); p.write_text(s); print("planted", ep, "P2")
