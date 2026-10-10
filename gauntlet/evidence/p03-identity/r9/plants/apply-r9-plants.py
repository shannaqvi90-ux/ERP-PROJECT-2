#!/usr/bin/env python3
"""Critic p03 round 9 plants. Usage: apply-r9-plants.py <copy root> <set>
set 1: L9 (process-state tenant leak), P4 (revoke sessions + removePasskeys skips the target check),
       P5 (removing one passkey by passkeyId ignores whose passkey it is)
set 2: L10 (database: a SECURITY DEFINER lookup of passkeys by credential id with an erp_auth policy)
Each plant is one hunk; the script fails loudly if an anchor is missing."""
import sys, pathlib

root = pathlib.Path(sys.argv[1]); which = sys.argv[2]
ident = root / "src/Modules/Identity/Erp.Modules.Identity"

def sub(path, old, new, count=1):
    p = ident / path
    s = p.read_text()
    if s.count(old) != count:
        sys.exit(f"anchor not found exactly {count}x in {path}: {old[:80]!r} (found {s.count(old)})")
    p.write_text(s.replace(old, new))
    print("planted in", path)

if which == "1":
    # L9: the sign-in service remembers, per e-mail address, how that address last signed in, in a
    # process-wide dictionary, and a failed password sign-in hints "use your passkey" from it.
    # E-mail addresses repeat across workspaces, so tenant A learns that B's same-address user
    # signs in with a passkey (a cross-tenant oracle held in process state).
    sub("Auth/SignInService.cs",
        "    private static string? Truncate(string? value, int max) =>",
        "    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LastMethodByEmail = new(StringComparer.OrdinalIgnoreCase);\n\n"
        "    private static string? Truncate(string? value, int max) =>")
    sub("Auth/SignInService.cs",
        "        await db.SaveChangesAsync(cancellationToken);\n        devices.Remember(http, email, options.Value.AlwaysSecureCookie || http.Request.IsHttps);",
        "        await db.SaveChangesAsync(cancellationToken);\n        LastMethodByEmail[email] = method;\n        devices.Remember(http, email, options.Value.AlwaysSecureCookie || http.Request.IsHttps);")
    sub("Auth/AuthEndpoints.cs",
        "            default:\n                return Problems.Result(http, StatusCodes.Status401Unauthorized, withPasskey ? \"auth.passkeyFailed\" : \"auth.signInFailed\");",
        "            default:\n"
        "                if (!withPasskey && SignInService.LastMethodByEmail.TryGetValue(request.Email!.Trim(), out var last) && last == SignInMethods.Passkey)\n"
        "                {\n"
        "                    var hint = Problems.Create(http, StatusCodes.Status401Unauthorized, \"auth.signInFailed\");\n"
        "                    hint.Extensions[\"tryPasskey\"] = true;\n"
        "                    return TypedResults.Problem(hint);\n"
        "                }\n"
        "                return Problems.Result(http, StatusCodes.Status401Unauthorized, withPasskey ? \"auth.passkeyFailed\" : \"auth.signInFailed\");")
    # P4: "Sign out everywhere" with removePasskeys is treated as an emergency lever (a lost device)
    # and skips the check that the target's access is within the caller's own.
    sub("Users/UserEndpoints.cs",
        "        Guid id, bool? removePasskeys, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)\n    {\n        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)",
        "        Guid id, bool? removePasskeys, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)\n    {\n        if (removePasskeys != true && await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)")
    # P5: removing one passkey by passkeyId looks it up by its id alone, not among the target
    # user's passkeys: the target check passes for a weak user, and the Administrator's passkey goes.
    sub("Auth/Passkeys/PasskeyEndpoints.cs",
        "db.Passkeys.Where(p => p.UserId == userId && (passkeyId == null || p.Id == passkeyId))",
        "db.Passkeys.Where(p => passkeyId == null ? p.UserId == userId : p.Id == passkeyId)")
elif which == "2":
    # L10: discoverable passkeys without a user handle: a reviewed-looking SECURITY DEFINER lookup
    # that finds a credential's workspace and user before any tenant is bound, owned by erp_auth
    # with a SELECT policy on identity.passkeys (not added to the allowlists).
    mig = ident / "Migrations/20261010065900_SignInMethod.cs"
    s = mig.read_text()
    anchor = "                sql: \"method IN ('password', 'passkey')\");\n        }"
    if s.count(anchor) != 1:
        sys.exit("anchor missing in SignInMethod migration")
    s = s.replace(anchor, anchor[:-10] + """
            migrationBuilder.Sql(@"
                GRANT USAGE, CREATE ON SCHEMA identity TO erp_auth;
                GRANT SELECT (tenant_id, id, user_id, credential_id) ON identity.passkeys TO erp_auth;
                CREATE POLICY auth_resolver_read ON identity.passkeys AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
                CREATE FUNCTION identity.resolve_passkey(p_credential bytea)
                    RETURNS TABLE (tenant_id uuid, user_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER
                    SET search_path = pg_catalog, pg_temp
                AS $f$ SELECT p.tenant_id, p.user_id FROM identity.passkeys p WHERE p.credential_id = p_credential $f$;
                ALTER FUNCTION identity.resolve_passkey(bytea) OWNER TO erp_auth;
                REVOKE ALL ON FUNCTION identity.resolve_passkey(bytea) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION identity.resolve_passkey(bytea) TO erp_app;
                REVOKE CREATE ON SCHEMA identity FROM erp_auth;");
        }""", 1)
    mig.write_text(s)
    print("planted L10 in SignInMethod migration")
else:
    sys.exit("set 1 or 2")
