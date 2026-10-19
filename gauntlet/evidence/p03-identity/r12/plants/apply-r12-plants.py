#!/usr/bin/env python3
"""Critic p03 round 12: deliberate faults planted in this piece's surface (a clone only).
usage: apply-r12-plants.py <repo> <plant ids, comma separated: L1,S1,S2,S3,U1>"""
import sys, pathlib
repo = pathlib.Path(sys.argv[1]); which = set(sys.argv[2].split(','))
def sub(rel, old, new, count=1):
    p = repo / rel; s = p.read_text()
    assert s.count(old) >= 1, f'{rel}: anchor not found: {old[:80]!r}'
    s = s.replace(old, new, count); p.write_text(s); print('planted in', rel)
ID = 'src/Modules/Identity/Erp.Modules.Identity/'
if 'L1' in which:
    # L1 (G1): the new singleton ClientAddresses keeps, per network address, the e-mail of the last
    # account that signed in from it (any workspace), and the sign-in history shows it next to the
    # user agent of each attempt from that address: tenant A's admin reads tenant B's sign-ins.
    sub(ID + 'Auth/ClientAddresses.cs', 'internal sealed class ClientAddresses\n{\n',
        'internal sealed class ClientAddresses\n{\n    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LastAccountAt = new();\n')
    sub(ID + 'Auth/SignInService.cs', '        var source = client.Shared ? device : client.Source;\n',
        '        var source = client.Shared ? device : client.Source;\n        addresses.LastAccountAt[client.Address ?? ""] = email;\n')
    sub(ID + 'Users/UserEndpoints.cs', 'new SignInDto(a.Id, a.OccurredAt, a.Outcome, a.IpAddress, a.UserAgent, a.Active, a.Method,',
        'new SignInDto(a.Id, a.OccurredAt, a.Outcome, a.IpAddress, a.UserAgent + (addresses.LastAccountAt.TryGetValue(a.IpAddress ?? "", out var last) ? " (last here: " + last + ")" : ""), a.Active, a.Method,')
if 'S1' in which:
    # S1 (G2): 'Allow sign-in again' (unblock) skips the check that the target's access is within the caller's.
    p = repo / (ID + 'Users/UserEndpoints.cs'); s = p.read_text()
    i = s.index('private static async Task<Results<NoContent, ProblemHttpResult>> Unblock(')
    j = s.index('if (await TargetProblemAsync(', i)
    s = s[:j] + 'if (false && await TargetProblemAsync(' + s[j+len('if (await TargetProblemAsync('):]
    p.write_text(s); print('planted S1')
if 'S2' in which:
    # S2 (G2): reset password to a one-time set-up code (no password given) skips the target check:
    # a holder of identity.users.resetPassword resets the Administrator and is handed their set-up code.
    p = repo / (ID + 'Users/UserEndpoints.cs'); s = p.read_text()
    i = s.index('Ok<ResetPasswordResponse>, ProblemHttpResult>> ResetPassword(')
    j = s.index('if (await TargetProblemAsync(', i)
    s = s[:j] + 'if (!string.IsNullOrEmpty(request.Password) && await TargetProblemAsync(' + s[j+len('if (await TargetProblemAsync('):]
    p.write_text(s); print('planted S2')
if 'S3' in which:
    # S3 (lockout, this round's change): from a shared address any right password, not only an
    # unexpired set-up code, gets past the pause, so the pause no longer limits password guessing there.
    sub(ID + 'IdentitySql.cs', """IF v_nth IS NOT NULL AND NOT (coalesce(p_shared, false) AND r.is_active AND r.expires_at IS NOT NULL
                                              AND r.expires_at > now() AND r.stored = ANY (p_proofs)) THEN""",
        """IF v_nth IS NOT NULL AND NOT (coalesce(p_shared, false) AND r.is_active
                                              AND coalesce(r.expires_at, 'infinity') > now() AND r.stored = ANY (p_proofs)) THEN""")
if 'U1' in which:
    # U1 (G2 on screen): the Sign-in history tab is shown without identity.signIns.read.
    sub('web/src/modules/identity/UserPanel.tsx', 'hidden: !can("identity.signIns.read") }', 'hidden: false }')
