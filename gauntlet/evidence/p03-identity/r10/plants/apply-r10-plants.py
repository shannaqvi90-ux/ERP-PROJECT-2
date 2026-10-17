#!/usr/bin/env python3
"""Critic p03 round 10 plants. Usage: apply-r10-plants.py <clone>. Each edit must match exactly once."""
import sys, pathlib
root = pathlib.Path(sys.argv[1])
U = 'src/Modules/Identity/Erp.Modules.Identity/Users/UserEndpoints.cs'
PK = 'src/Modules/Identity/Erp.Modules.Identity/Auth/Passkeys/PasskeyEndpoints.cs'
SIS = 'src/Modules/Identity/Erp.Modules.Identity/Auth/SignInService.cs'
SQL = 'src/Modules/Identity/Erp.Modules.Identity/IdentitySql.cs'
plants = [
 # S1 (round 9's P4 again): Sign out everywhere with removePasskeys=true skips the target check.
 ('S1', U, """        Guid id, bool? removePasskeys, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        if (await TargetProblemAsync(""", """        Guid id, bool? removePasskeys, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        if (removePasskeys != true && await TargetProblemAsync("""),
 # S2: Unblock never checks the target: a clerk lifts the Administrator's sign-in pause.
 ('S2', U, """        Guid id, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        // A concurrent request may have removed the user since the target check.
        if (await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) is not { } user)
        {
            return Problems.NotFound(http);
        }
        user.SignInUnblockedAt""", """        Guid id, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        // A concurrent request may have removed the user since the target check.
        if (await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) is not { } user)
        {
            return Problems.NotFound(http);
        }
        user.SignInUnblockedAt"""),
 # S3: choosing another user's default company skips the target check.
 ('S3', U, """        // Who it is for comes first: a refusal never depends on the company asked for.
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }""", """        // Who it is for comes first: a refusal never depends on the company asked for.
        if (id == caller.UserId && await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }"""),
 # S4: removing one passkey by id (passkeyId) skips the target check: "just one key, from a lost device".
 ('S4', PK, """        if (await UserEndpoints.TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        var removed = await RemoveAsync(db, id, passkeyId, cancellationToken);""", """        if (passkeyId is null && await UserEndpoints.TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        var removed = await RemoveAsync(db, id, passkeyId, cancellationToken);"""),
 # L1: a process-wide memo of when each address's pause lifts, by address only (no client, no tenant).
 ('L1a', SIS, """    private sealed record Match(string Challenge, Guid TenantId);""", """    private sealed record Match(string Challenge, Guid TenantId);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> PauseMemo = new(StringComparer.OrdinalIgnoreCase);"""),
 ('L1b', SIS, """        if (matches.Count == 0)
        {
            return pausedUntil is { } until ? new SignInOutcome.Paused(until) : new SignInOutcome.Failed();
        }""", """        if (matches.Count == 0)
        {
            if (pausedUntil is { } p) PauseMemo[email.Trim()] = p;
            return pausedUntil is { } until ? new SignInOutcome.Paused(until) : new SignInOutcome.Failed();
        }
        if (PauseMemo.TryGetValue(email.Trim(), out var memo) && memo > time.GetUtcNow())
        {
            return new SignInOutcome.Paused(memo);
        }"""),
 # L2: the sign-in function answers the workspace of the first account with the address, not the one whose password matched.
 ('L2a', SQL, """            v_decoy text;
            r record;
        BEGIN
            IF coalesce(current_setting('app.tenant_id', true), '') <> '' THEN
                RETURN;
            END IF;
            IF v_email = '' OR length(v_email) > 254 THEN
                RETURN;
            END IF;

            IF p_proofs IS NULL THEN
                FOR r IN
                    SELECT split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch""", """            v_decoy text;
            v_first uuid;
            r record;
        BEGIN
            IF coalesce(current_setting('app.tenant_id', true), '') <> '' THEN
                RETURN;
            END IF;
            IF v_email = '' OR length(v_email) > 254 THEN
                RETURN;
            END IF;

            IF p_proofs IS NULL THEN
                FOR r IN
                    SELECT split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch"""),
 ('L2b', SQL, """                v_found := true;
                PERFORM set_config('app.tenant_id', r.t::text, true), set_config('app.tenant_tx', v_epoch, true);""", """                v_found := true;
                v_first := coalesce(v_first, r.t);
                PERFORM set_config('app.tenant_id', r.t::text, true), set_config('app.tenant_tx', v_epoch, true);"""),
 ('L2c', SQL, """                    v_matched := true;
                    challenge := r.ch;
                    tenant_id := r.t;""", """                    v_matched := true;
                    challenge := r.ch;
                    tenant_id := v_first;"""),
 # U1: the palette's "Allow sign-in again" offered without the sign-in history it opens.
 ('U1', 'web/src/modules/identity/extensions.ts', """      permission: "identity.users.update",
      requires: ["identity.signIns.read"],""", """      permission: "identity.users.update","""),
 # U2: the palette's "Sign out everywhere" offered to someone who may not end this user's sessions.
 ('U2', 'web/src/modules/identity/UserPanel.tsx', """    enabled: allowed.signOutEverywhere && !busy,""", """    enabled: !busy,"""),
 # U3: the palette's "Delete role" offered to someone who may not delete roles.
 ('U3', 'web/src/modules/identity/RolesPage.tsx', """    enabled: Boolean(role && actions.delete && !confirmDelete),""", """    enabled: Boolean(role && !confirmDelete),"""),
]
only = set(sys.argv[2].split(',')) if len(sys.argv) > 2 else None
for pid, f, find, repl in plants:
    if only and pid.rstrip('abc') not in only and pid not in only: continue
    p = root / f; s = p.read_text()
    head, body = '', s
    if f == SQL:
        i = s.index('public const string PauseByAddressUp'); j = s.index('public const string', i + 10) if 'public const string' in s[i+10:] else len(s)
        head, body, tail = s[:i], s[i:j], s[j:]
    else:
        tail = ''
    n = body.count(find)
    if n != 1: sys.exit(f'{pid}: {n} matches in {f}')
    p.write_text(head + body.replace(find, repl) + tail); print('planted', pid, f)
