namespace Erp.Modules.Identity;

/// <summary>
/// The two reviewed lookups that run before a tenant is known. Both are SECURITY DEFINER
/// functions owned by the NOLOGIN role <c>erp_auth</c>, which may read only the columns they need
/// through a SELECT-only policy. The application role can execute them but cannot read those
/// tables across tenants any other way. Listed in tests/Gates/security-definer-allowlist.txt and
/// tests/Gates/policy-allowlist.txt.
/// </summary>
internal static class IdentitySql
{
    public const string Up = """
        GRANT USAGE, CREATE ON SCHEMA identity TO erp_auth;
        GRANT SELECT (tenant_id, id, email_normalized, password_hash, is_active, lockout_until) ON identity.users TO erp_auth;
        GRANT SELECT (tenant_id, id, user_id, token_hash, expires_at, revoked_at) ON identity.sessions TO erp_auth;
        CREATE POLICY auth_resolver_read ON identity.users AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
        CREATE POLICY auth_resolver_read ON identity.sessions AS PERMISSIVE FOR SELECT TO erp_auth USING (true);

        CREATE FUNCTION identity.resolve_login(p_email text)
            RETURNS TABLE (tenant_id uuid, user_id uuid, password_hash text, is_active boolean, lockout_until timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT u.tenant_id, u.id, u.password_hash, u.is_active, u.lockout_until
              FROM identity.users u
             WHERE u.email_normalized = lower(btrim(p_email))
             ORDER BY u.tenant_id
             LIMIT 10
        $$;

        CREATE FUNCTION identity.resolve_session(p_token_hash bytea)
            RETURNS TABLE (session_id uuid, tenant_id uuid, user_id uuid, expires_at timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT s.id, s.tenant_id, s.user_id, s.expires_at
              FROM identity.sessions s
             WHERE s.token_hash = p_token_hash
               AND s.revoked_at IS NULL
               AND s.expires_at > now()
        $$;

        ALTER FUNCTION identity.resolve_login(text) OWNER TO erp_auth;
        ALTER FUNCTION identity.resolve_session(bytea) OWNER TO erp_auth;
        REVOKE ALL ON FUNCTION identity.resolve_login(text) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity.resolve_session(bytea) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION identity.resolve_login(text) TO erp_app;
        GRANT EXECUTE ON FUNCTION identity.resolve_session(bytea) TO erp_app;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        """;

    public const string Down = """
        DROP FUNCTION IF EXISTS identity.resolve_session(bytea);
        DROP FUNCTION IF EXISTS identity.resolve_login(text);
        DROP POLICY IF EXISTS auth_resolver_read ON identity.sessions;
        DROP POLICY IF EXISTS auth_resolver_read ON identity.users;
        REVOKE ALL ON SCHEMA identity FROM erp_auth;
        """;

    /// <summary>
    /// Defence in depth for the two reviewed lookups: they answer only on a connection that is not
    /// bound to a tenant (no transaction-local <c>app.tenant_id</c>). Sign-in and session
    /// resolution run before any tenant is bound; an endpoint that tried to call them inside its
    /// request's tenant-bound transaction gets no rows. Replaced as <c>erp_auth</c>, which owns
    /// them, so ownership, grants and the pinned search_path stay as reviewed.
    /// </summary>
    public const string UnboundOnlyUp = """
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        CREATE OR REPLACE FUNCTION identity.resolve_login(p_email text)
            RETURNS TABLE (tenant_id uuid, user_id uuid, password_hash text, is_active boolean, lockout_until timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT u.tenant_id, u.id, u.password_hash, u.is_active, u.lockout_until
              FROM identity.users u
             WHERE u.email_normalized = lower(btrim(p_email))
               AND coalesce(current_setting('app.tenant_id', true), '') = ''
             ORDER BY u.tenant_id
             LIMIT 10
        $$;
        CREATE OR REPLACE FUNCTION identity.resolve_session(p_token_hash bytea)
            RETURNS TABLE (session_id uuid, tenant_id uuid, user_id uuid, expires_at timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT s.id, s.tenant_id, s.user_id, s.expires_at
              FROM identity.sessions s
             WHERE s.token_hash = p_token_hash
               AND s.revoked_at IS NULL
               AND s.expires_at > now()
               AND coalesce(current_setting('app.tenant_id', true), '') = ''
        $$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        """;

    public const string UnboundOnlyDown = """
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        CREATE OR REPLACE FUNCTION identity.resolve_login(p_email text)
            RETURNS TABLE (tenant_id uuid, user_id uuid, password_hash text, is_active boolean, lockout_until timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT u.tenant_id, u.id, u.password_hash, u.is_active, u.lockout_until
              FROM identity.users u
             WHERE u.email_normalized = lower(btrim(p_email))
             ORDER BY u.tenant_id
             LIMIT 10
        $$;
        CREATE OR REPLACE FUNCTION identity.resolve_session(p_token_hash bytea)
            RETURNS TABLE (session_id uuid, tenant_id uuid, user_id uuid, expires_at timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT s.id, s.tenant_id, s.user_id, s.expires_at
              FROM identity.sessions s
             WHERE s.token_hash = p_token_hash
               AND s.revoked_at IS NULL
               AND s.expires_at > now()
        $$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        """;

    /// <summary>
    /// Credentials apart from users, the sign-in history and the reviewed sign-in function that
    /// replaces <c>resolve_login</c>. Runs after the tables exist and before
    /// <c>users.password_hash</c> is dropped. The application role may write a password hash but
    /// never read one; the sign-in history is append-only for it.
    /// </summary>
    public const string CredentialsUp = """
        -- The application role writes credentials but never reads the hash back.
        REVOKE SELECT, UPDATE ON identity.user_credentials FROM erp_app;
        GRANT SELECT (id, tenant_id, must_change, expires_at, changed_at, changed_by) ON identity.user_credentials TO erp_app;
        GRANT UPDATE (password_hash, must_change, expires_at, changed_at, changed_by) ON identity.user_credentials TO erp_app;
        -- The sign-in history is append-only.
        REVOKE UPDATE, DELETE, TRUNCATE ON identity.sign_in_attempts FROM erp_app;

        -- Move every existing hash (the owner reads past row-level security only inside this
        -- migration's transaction; FORCE is restored before it commits).
        SELECT set_config('app.actor_kind', 'migration', true);
        ALTER TABLE identity.users NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE identity.user_credentials NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries NO FORCE ROW LEVEL SECURITY;
        INSERT INTO identity.user_credentials (id, tenant_id, password_hash, must_change, expires_at, changed_at, changed_by)
            SELECT id, tenant_id, password_hash, false, NULL, updated_at, NULL FROM identity.users;
        ALTER TABLE identity.users FORCE ROW LEVEL SECURITY;
        ALTER TABLE identity.user_credentials FORCE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;

        -- Users no longer carry a password; the last sign-in time stays out of the change set.
        DROP TRIGGER audit_capture ON identity.users;
        CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON identity.users
            FOR EACH ROW EXECUTE FUNCTION audit.capture('-last_sign_in_at');
        -- Sessions are business records now (who signed in, from where, until when); the sign-in
        -- history (sign_in_attempts) is the append-only log instead.
        CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON identity.sessions
            FOR EACH ROW EXECUTE FUNCTION audit.capture('token_hash');

        -- The key behind the decoy salt for addresses that exist nowhere. Platform table, no tenant
        -- data, readable only by the sign-in function's owner.
        CREATE TABLE identity.sign_in_secret (
            id integer PRIMARY KEY CHECK (id = 1),
            secret bytea NOT NULL CHECK (length(secret) >= 32));
        INSERT INTO identity.sign_in_secret (id, secret)
            VALUES (1, decode(replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', ''), 'hex'));
        REVOKE ALL ON identity.sign_in_secret FROM PUBLIC;
        GRANT SELECT ON identity.sign_in_secret TO erp_auth;

        GRANT USAGE ON SCHEMA erp TO erp_auth;
        GRANT SELECT (tenant_id, id, email_normalized, is_active, sign_in_unblocked_at) ON identity.users TO erp_auth;
        GRANT SELECT (tenant_id, id, password_hash, expires_at) ON identity.user_credentials TO erp_auth;
        CREATE POLICY auth_resolver_read ON identity.user_credentials AS PERMISSIVE FOR SELECT TO erp_auth USING (true);
        GRANT SELECT (tenant_id, user_id, source, outcome, occurred_at) ON identity.sign_in_attempts TO erp_auth;
        GRANT INSERT ON identity.sign_in_attempts TO erp_auth;

        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        DROP FUNCTION identity.resolve_login(text);

        -- Sign-in in two calls, so the application never reads a hash:
        --   p_proofs NULL: the hashing parameters (algorithm$cost$salt) of each account with the
        --     address, or one decoy keyed by sign_in_secret when there is none (the same every
        --     time, so it cannot be told apart from a real account by asking twice).
        --   p_proofs given (the application's PBKDF2 of the typed password under each salt, in the
        --     stored format): each account whose stored hash equals a proof, is active, whose code
        --     has not expired and whose client (p_source) is not paused returns its challenge and
        --     workspace. Every other outcome is recorded in that account's own sign-in history, with
        --     the account's tenant bound for that insert only and unbound again before returning.
        -- A client is paused on an account after p_threshold failures within p_window_minutes,
        -- counted since the client's last success there and the administrator's last unblock.
        CREATE FUNCTION identity.verify_sign_in(p_email text, p_proofs text[], p_source text, p_ip text, p_user_agent text,
                                                p_threshold integer, p_window_minutes integer)
            RETURNS TABLE (challenge text, tenant_id uuid)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $fn$
        #variable_conflict use_column
        DECLARE
            v_email text := lower(btrim(coalesce(p_email, '')));
            v_threshold integer := least(greatest(coalesce(p_threshold, 5), 3), 50);
            v_window interval := make_interval(mins => least(greatest(coalesce(p_window_minutes, 15), 1), 1440));
            v_source text := left(coalesce(nullif(btrim(p_source), ''), 'unknown'), 64);
            v_epoch text := extract(epoch from now())::text;
            v_found boolean := false;
            v_outcome text;
            v_failures integer;
            v_since timestamptz;
            v_decoy text;
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
                    SELECT split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                      FROM identity.users u
                      JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                     WHERE u.email_normalized = v_email
                     ORDER BY 1
                     LIMIT 10
                LOOP
                    v_found := true;
                    challenge := r.ch;
                    tenant_id := NULL;
                    RETURN NEXT;
                END LOOP;
                IF NOT v_found THEN
                    SELECT 'pbkdf2-sha512$210000$' || encode(substr(sha256(s.secret || convert_to(v_email, 'UTF8')), 1, 16), 'base64')
                      INTO v_decoy
                      FROM identity.sign_in_secret s
                     WHERE s.id = 1;
                    challenge := v_decoy;
                    tenant_id := NULL;
                    RETURN NEXT;
                END IF;
                RETURN;
            END IF;

            FOR r IN
                SELECT u.tenant_id AS t, u.id AS uid, u.is_active, u.sign_in_unblocked_at, c.password_hash AS stored, c.expires_at,
                       split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                  FROM identity.users u
                  JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                 WHERE u.email_normalized = v_email
                 ORDER BY 7
                 LIMIT 10
            LOOP
                PERFORM set_config('app.tenant_id', r.t::text, true), set_config('app.tenant_tx', v_epoch, true);
                SELECT max(a.occurred_at) INTO v_since
                  FROM identity.sign_in_attempts a
                 WHERE a.user_id = r.uid AND a.source = v_source AND a.outcome = 'succeeded';
                v_since := greatest(now() - v_window,
                                    coalesce(r.sign_in_unblocked_at, '-infinity'::timestamptz),
                                    coalesce(v_since, '-infinity'::timestamptz));
                SELECT count(*) INTO v_failures
                  FROM identity.sign_in_attempts a
                 WHERE a.user_id = r.uid AND a.source = v_source AND a.outcome = 'failed' AND a.occurred_at > v_since;
                IF NOT r.is_active THEN
                    v_outcome := 'inactive';
                ELSIF v_failures >= v_threshold THEN
                    v_outcome := 'throttled';
                ELSIF r.stored = ANY (p_proofs) THEN
                    v_outcome := CASE WHEN r.expires_at IS NOT NULL AND r.expires_at <= now() THEN 'expired' ELSE 'matched' END;
                ELSE
                    v_outcome := 'failed';
                END IF;
                IF v_outcome <> 'matched' THEN
                    INSERT INTO identity.sign_in_attempts (id, tenant_id, user_id, occurred_at, outcome, source, ip_address, user_agent)
                    VALUES (gen_random_uuid(), r.t, r.uid, clock_timestamp(), v_outcome, v_source, left(p_ip, 64), left(p_user_agent, 400));
                END IF;
                PERFORM set_config('app.tenant_id', '', true), set_config('app.tenant_tx', '', true);
                IF v_outcome = 'matched' THEN
                    challenge := r.ch;
                    tenant_id := r.t;
                    RETURN NEXT;
                END IF;
            END LOOP;
        END
        $fn$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        REVOKE ALL ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer) TO erp_app;
        """;

    /// <summary>Back to passwords on users and <c>resolve_login</c> (runs before the new tables
    /// are dropped and after <c>users.password_hash</c> is back).</summary>
    public const string CredentialsDown = """
        SELECT set_config('app.actor_kind', 'migration', true);
        ALTER TABLE identity.users NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE identity.user_credentials NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries NO FORCE ROW LEVEL SECURITY;
        UPDATE identity.users u SET password_hash = c.password_hash
          FROM identity.user_credentials c WHERE c.tenant_id = u.tenant_id AND c.id = u.id;
        ALTER TABLE identity.users FORCE ROW LEVEL SECURITY;
        ALTER TABLE identity.user_credentials FORCE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;
        DROP TRIGGER IF EXISTS audit_capture ON identity.sessions;
        DROP TRIGGER audit_capture ON identity.users;
        CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON identity.users
            FOR EACH ROW EXECUTE FUNCTION audit.capture('password_hash', '-last_sign_in_at', '-failed_sign_in_count', '-lockout_until');
        DROP TABLE identity.sign_in_secret;
        GRANT SELECT (password_hash, lockout_until) ON identity.users TO erp_auth;
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        DROP FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer);
        CREATE FUNCTION identity.resolve_login(p_email text)
            RETURNS TABLE (tenant_id uuid, user_id uuid, password_hash text, is_active boolean, lockout_until timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $$
            SELECT u.tenant_id, u.id, u.password_hash, u.is_active, u.lockout_until
              FROM identity.users u
             WHERE u.email_normalized = lower(btrim(p_email))
               AND coalesce(current_setting('app.tenant_id', true), '') = ''
             ORDER BY u.tenant_id
             LIMIT 10
        $$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        REVOKE ALL ON FUNCTION identity.resolve_login(text) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION identity.resolve_login(text) TO erp_app;
        """;

    /// <summary>
    /// Roles held in one company (critic p03 rounds 1-3 scope; decision p03-identity-roles-per-company):
    /// the rows belong to their company (the company scope hides other companies' rows; the user's
    /// own rows stay readable to their session, which reads them before its scope is bound), and
    /// <c>users.company_role_count</c> counts them in every company, kept by a trigger running with
    /// the caller's rights (no SECURITY DEFINER: the row inserted or deleted is one the caller may
    /// write, and the counter is on a row of the caller's own tenant), so an administrator can tell
    /// that a user holds roles in companies they cannot see. The counter stays out of the audit
    /// trail; the assignments themselves are audited.
    /// </summary>
    public const string CompanyRoles = """
        CREATE FUNCTION identity.count_company_roles() RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = pg_catalog, pg_temp
        AS $$
        BEGIN
            IF TG_OP IN ('DELETE', 'UPDATE') THEN
                UPDATE identity.users SET company_role_count = company_role_count - 1
                 WHERE tenant_id = OLD.tenant_id AND id = OLD.user_id;
            END IF;
            IF TG_OP IN ('INSERT', 'UPDATE') THEN
                UPDATE identity.users SET company_role_count = company_role_count + 1
                 WHERE tenant_id = NEW.tenant_id AND id = NEW.user_id;
            END IF;
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION identity.count_company_roles() FROM PUBLIC;
        CREATE TRIGGER count_company_roles AFTER INSERT OR DELETE OR UPDATE OF tenant_id, user_id ON identity.user_company_roles
            FOR EACH ROW EXECUTE FUNCTION identity.count_company_roles();

        DROP TRIGGER audit_capture ON identity.users;
        CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON identity.users
            FOR EACH ROW EXECUTE FUNCTION audit.capture('-last_sign_in_at', '-company_role_count');
        """;

    public const string CompanyRolesDown = """
        DROP TRIGGER IF EXISTS count_company_roles ON identity.user_company_roles;
        DROP FUNCTION IF EXISTS identity.count_company_roles();
        DROP TRIGGER audit_capture ON identity.users;
        CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON identity.users
            FOR EACH ROW EXECUTE FUNCTION audit.capture('-last_sign_in_at');
        """;

    /// <summary>
    /// Sign-in says when it is paused (critic p00 rounds 7 to 10, routed to p03): failed attempts
    /// are counted by address and client in <c>identity.sign_in_failures</c>, a platform table
    /// keyed by a hash of the address and the client under <c>sign_in_secret</c> (it names
    /// neither, and holds no tenant data), for every address typed, whether an account has it or
    /// not. An account is paused for a client when the address's failures from that client since
    /// the client's last success there and the administrator's last unblock reach the threshold
    /// within the window; an address no account has, by the same count since the window began.
    /// When nothing matched and every account with the address (or the address alone) is paused,
    /// the function answers one row with only <c>paused_until</c>: the time the pause lifts by
    /// itself, worked out the same way whether or not an account exists, so the answer tells an
    /// attacker nothing an unknown address would not. Failed and inactive attempts count alike:
    /// a deactivated account pauses exactly as an unknown address does.
    /// </summary>
    public const string PauseByAddressUp = PauseByAddressTable + """
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        DROP FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer);

        """ + PauseByAddressFunction;

    /// <summary>The platform table of counted failures (<see cref="PauseByAddressUp"/>).</summary>
    private const string PauseByAddressTable = """
        CREATE TABLE identity.sign_in_failures (
            key bytea NOT NULL CHECK (length(key) = 32),
            occurred_at timestamptz NOT NULL);
        CREATE INDEX ix_sign_in_failures_key_occurred_at ON identity.sign_in_failures (key, occurred_at);
        CREATE INDEX ix_sign_in_failures_occurred_at ON identity.sign_in_failures (occurred_at);
        REVOKE ALL ON identity.sign_in_failures FROM PUBLIC;
        GRANT SELECT, INSERT, DELETE ON identity.sign_in_failures TO erp_auth;

        """;

    /// <summary>The reviewed sign-in function of <see cref="PauseByAddressUp"/> (seven arguments),
    /// created as <c>erp_auth</c> (the caller sets the role), with its grants.</summary>
    private const string PauseByAddressFunction = """
        -- Sign-in in two calls, so the application never reads a hash:
        --   p_proofs NULL: the hashing parameters (algorithm$cost$salt) of each account with the
        --     address, or one decoy keyed by sign_in_secret when there is none (the same every
        --     time, so it cannot be told apart from a real account by asking twice).
        --   p_proofs given (the application's PBKDF2 of the typed password under each salt, in the
        --     stored format): each account whose stored hash equals a proof, is active, whose code
        --     has not expired and whose client (p_source) is not paused returns its challenge and
        --     workspace. Every other outcome is recorded in that account's own sign-in history, with
        --     the account's tenant bound for that insert only and unbound again before returning.
        --     When nothing matched, each failed or inactive attempt (or an attempt at an address no
        --     account has) is counted in sign_in_failures under the keyed address and client; then,
        --     if every account with the address (or the address alone) is paused, one row answers
        --     with only paused_until, the moment the pause lifts by itself.
        -- A client is paused on an account after p_threshold failures at the address within
        -- p_window_minutes, counted since the client's last success there and the administrator's
        -- last unblock; on an address no account has, since the window began. It lifts when the
        -- p_threshold-th latest of those failures leaves the window.
        CREATE FUNCTION identity.verify_sign_in(p_email text, p_proofs text[], p_source text, p_ip text, p_user_agent text,
                                                p_threshold integer, p_window_minutes integer)
            RETURNS TABLE (challenge text, tenant_id uuid, paused_until timestamptz)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $fn$
        #variable_conflict use_column
        DECLARE
            v_email text := lower(btrim(coalesce(p_email, '')));
            v_threshold integer := least(greatest(coalesce(p_threshold, 5), 3), 50);
            v_window interval := make_interval(mins => least(greatest(coalesce(p_window_minutes, 15), 1), 1440));
            v_source text := left(coalesce(nullif(btrim(p_source), ''), 'unknown'), 64);
            v_epoch text := extract(epoch from now())::text;
            v_found boolean := false;
            v_matched boolean := false;
            v_counted boolean := false;
            v_key bytea;
            v_outcome text;
            v_success timestamptz;
            v_since timestamptz;
            v_sinces timestamptz[] := ARRAY[]::timestamptz[];
            v_nth timestamptz;
            v_until timestamptz;
            v_decoy text;
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
                    SELECT split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                      FROM identity.users u
                      JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                     WHERE u.email_normalized = v_email
                     ORDER BY 1
                     LIMIT 10
                LOOP
                    v_found := true;
                    challenge := r.ch;
                    tenant_id := NULL;
                    paused_until := NULL;
                    RETURN NEXT;
                END LOOP;
                IF NOT v_found THEN
                    SELECT 'pbkdf2-sha512$210000$' || encode(substr(sha256(s.secret || convert_to(v_email, 'UTF8')), 1, 16), 'base64')
                      INTO v_decoy
                      FROM identity.sign_in_secret s
                     WHERE s.id = 1;
                    challenge := v_decoy;
                    tenant_id := NULL;
                    paused_until := NULL;
                    RETURN NEXT;
                END IF;
                RETURN;
            END IF;

            -- The address and the client, keyed so that the table names neither.
            SELECT sha256(s.secret || convert_to('sign-in-failure' || chr(10) || v_email || chr(10) || v_source, 'UTF8'))
              INTO v_key
              FROM identity.sign_in_secret s
             WHERE s.id = 1;

            FOR r IN
                SELECT u.tenant_id AS t, u.id AS uid, u.is_active, u.sign_in_unblocked_at, c.password_hash AS stored, c.expires_at,
                       split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                  FROM identity.users u
                  JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                 WHERE u.email_normalized = v_email
                 ORDER BY 7
                 LIMIT 10
            LOOP
                v_found := true;
                PERFORM set_config('app.tenant_id', r.t::text, true), set_config('app.tenant_tx', v_epoch, true);
                SELECT max(a.occurred_at) INTO v_success
                  FROM identity.sign_in_attempts a
                 WHERE a.user_id = r.uid AND a.source = v_source AND a.outcome = 'succeeded';
                v_since := greatest(now() - v_window,
                                    coalesce(r.sign_in_unblocked_at, '-infinity'::timestamptz),
                                    coalesce(v_success, '-infinity'::timestamptz));
                v_sinces := v_sinces || v_since;
                SELECT f.occurred_at INTO v_nth
                  FROM identity.sign_in_failures f
                 WHERE f.key = v_key AND f.occurred_at > v_since
                 ORDER BY f.occurred_at DESC
                OFFSET v_threshold - 1 LIMIT 1;
                IF v_nth IS NOT NULL THEN
                    v_outcome := 'throttled';
                ELSIF NOT r.is_active THEN
                    v_outcome := 'inactive';
                    v_counted := true;
                ELSIF r.stored = ANY (p_proofs) THEN
                    v_outcome := CASE WHEN r.expires_at IS NOT NULL AND r.expires_at <= now() THEN 'expired' ELSE 'matched' END;
                ELSE
                    v_outcome := 'failed';
                    v_counted := true;
                END IF;
                IF v_outcome <> 'matched' THEN
                    INSERT INTO identity.sign_in_attempts (id, tenant_id, user_id, occurred_at, outcome, source, ip_address, user_agent)
                    VALUES (gen_random_uuid(), r.t, r.uid, clock_timestamp(), v_outcome, v_source, left(p_ip, 64), left(p_user_agent, 400));
                END IF;
                PERFORM set_config('app.tenant_id', '', true), set_config('app.tenant_tx', '', true);
                IF v_outcome = 'matched' THEN
                    v_matched := true;
                    challenge := r.ch;
                    tenant_id := r.t;
                    paused_until := NULL;
                    RETURN NEXT;
                END IF;
            END LOOP;
            IF v_matched THEN
                RETURN;
            END IF;

            IF NOT v_found THEN
                -- No account has the address: the same count, since the window began.
                v_sinces := ARRAY[now() - v_window];
                SELECT f.occurred_at INTO v_nth
                  FROM identity.sign_in_failures f
                 WHERE f.key = v_key AND f.occurred_at > now() - v_window
                 ORDER BY f.occurred_at DESC
                OFFSET v_threshold - 1 LIMIT 1;
                v_counted := v_nth IS NULL;
            END IF;
            IF v_counted THEN
                INSERT INTO identity.sign_in_failures (key, occurred_at) VALUES (v_key, clock_timestamp());
                DELETE FROM identity.sign_in_failures WHERE occurred_at < now() - interval '1440 minutes';
            END IF;

            -- Paused now (this attempt counted) for every account with the address, or for the
            -- address alone: until the earliest moment one of them lifts by itself.
            FOREACH v_since IN ARRAY v_sinces LOOP
                SELECT f.occurred_at INTO v_nth
                  FROM identity.sign_in_failures f
                 WHERE f.key = v_key AND f.occurred_at > v_since
                 ORDER BY f.occurred_at DESC
                OFFSET v_threshold - 1 LIMIT 1;
                IF v_nth IS NULL THEN
                    RETURN;
                END IF;
                v_until := least(coalesce(v_until, 'infinity'::timestamptz), v_nth + v_window);
            END LOOP;
            IF v_until IS NOT NULL THEN
                challenge := NULL;
                tenant_id := NULL;
                paused_until := v_until;
                RETURN NEXT;
            END IF;
        END
        $fn$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        REVOKE ALL ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer) TO erp_app;
        """;

    /// <summary>Back to the per-account count (the function of <see cref="CredentialsUp"/>).</summary>
    public const string PauseByAddressDown = """
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        DROP FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer);
        CREATE FUNCTION identity.verify_sign_in(p_email text, p_proofs text[], p_source text, p_ip text, p_user_agent text,
                                                p_threshold integer, p_window_minutes integer)
            RETURNS TABLE (challenge text, tenant_id uuid)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $fn$
        #variable_conflict use_column
        DECLARE
            v_email text := lower(btrim(coalesce(p_email, '')));
            v_threshold integer := least(greatest(coalesce(p_threshold, 5), 3), 50);
            v_window interval := make_interval(mins => least(greatest(coalesce(p_window_minutes, 15), 1), 1440));
            v_source text := left(coalesce(nullif(btrim(p_source), ''), 'unknown'), 64);
            v_epoch text := extract(epoch from now())::text;
            v_found boolean := false;
            v_outcome text;
            v_failures integer;
            v_since timestamptz;
            v_decoy text;
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
                    SELECT split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                      FROM identity.users u
                      JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                     WHERE u.email_normalized = v_email
                     ORDER BY 1
                     LIMIT 10
                LOOP
                    v_found := true;
                    challenge := r.ch;
                    tenant_id := NULL;
                    RETURN NEXT;
                END LOOP;
                IF NOT v_found THEN
                    SELECT 'pbkdf2-sha512$210000$' || encode(substr(sha256(s.secret || convert_to(v_email, 'UTF8')), 1, 16), 'base64')
                      INTO v_decoy
                      FROM identity.sign_in_secret s
                     WHERE s.id = 1;
                    challenge := v_decoy;
                    tenant_id := NULL;
                    RETURN NEXT;
                END IF;
                RETURN;
            END IF;

            FOR r IN
                SELECT u.tenant_id AS t, u.id AS uid, u.is_active, u.sign_in_unblocked_at, c.password_hash AS stored, c.expires_at,
                       split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                  FROM identity.users u
                  JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                 WHERE u.email_normalized = v_email
                 ORDER BY 7
                 LIMIT 10
            LOOP
                PERFORM set_config('app.tenant_id', r.t::text, true), set_config('app.tenant_tx', v_epoch, true);
                SELECT max(a.occurred_at) INTO v_since
                  FROM identity.sign_in_attempts a
                 WHERE a.user_id = r.uid AND a.source = v_source AND a.outcome = 'succeeded';
                v_since := greatest(now() - v_window,
                                    coalesce(r.sign_in_unblocked_at, '-infinity'::timestamptz),
                                    coalesce(v_since, '-infinity'::timestamptz));
                SELECT count(*) INTO v_failures
                  FROM identity.sign_in_attempts a
                 WHERE a.user_id = r.uid AND a.source = v_source AND a.outcome = 'failed' AND a.occurred_at > v_since;
                IF NOT r.is_active THEN
                    v_outcome := 'inactive';
                ELSIF v_failures >= v_threshold THEN
                    v_outcome := 'throttled';
                ELSIF r.stored = ANY (p_proofs) THEN
                    v_outcome := CASE WHEN r.expires_at IS NOT NULL AND r.expires_at <= now() THEN 'expired' ELSE 'matched' END;
                ELSE
                    v_outcome := 'failed';
                END IF;
                IF v_outcome <> 'matched' THEN
                    INSERT INTO identity.sign_in_attempts (id, tenant_id, user_id, occurred_at, outcome, source, ip_address, user_agent)
                    VALUES (gen_random_uuid(), r.t, r.uid, clock_timestamp(), v_outcome, v_source, left(p_ip, 64), left(p_user_agent, 400));
                END IF;
                PERFORM set_config('app.tenant_id', '', true), set_config('app.tenant_tx', '', true);
                IF v_outcome = 'matched' THEN
                    challenge := r.ch;
                    tenant_id := r.t;
                    RETURN NEXT;
                END IF;
            END LOOP;
        END
        $fn$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        REVOKE ALL ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer) TO erp_app;
        DROP TABLE identity.sign_in_failures;
        """;

    /// <summary>
    /// A set-up code still signs in from a shared address while sign-in from that address is
    /// paused (critic p03 round 11): behind the shipped compose gateway every client has the same
    /// address, so five anonymous wrong passwords kept a newly invited person (who has a set-up
    /// code and, on a fresh browser, no device of the account) out for as long as anyone kept
    /// failing. The application passes <c>p_shared</c> when the client's address is one many
    /// clients share and the browser is not a known device of the account. Only a valid,
    /// unexpired set-up code (59 random bits, 168 hours) gets past the pause, and from a shared
    /// address every client together is held to the sign-in rate limit of that address; a wrong
    /// code from a paused client answers exactly as before. Passwords, temporary passwords and
    /// every client with an address of its own are paused exactly as before.
    /// </summary>
    public const string SetupCodeFromSharedAddressUp = """
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        DROP FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer);

        -- Sign-in in two calls, so the application never reads a hash:
        --   p_proofs NULL: the hashing parameters (algorithm$cost$salt) of each account with the
        --     address, or one decoy keyed by sign_in_secret when there is none (the same every
        --     time, so it cannot be told apart from a real account by asking twice).
        --   p_proofs given (the application's PBKDF2 of the typed password under each salt, in the
        --     stored format): each account whose stored hash equals a proof, is active, whose code
        --     has not expired and whose client (p_source) is not paused returns its challenge and
        --     workspace. Every other outcome is recorded in that account's own sign-in history, with
        --     the account's tenant bound for that insert only and unbound again before returning.
        --     When nothing matched, each failed or inactive attempt (or an attempt at an address no
        --     account has) is counted in sign_in_failures under the keyed address and client; then,
        --     if every account with the address (or the address alone) is paused, one row answers
        --     with only paused_until, the moment the pause lifts by itself.
        -- A client is paused on an account after p_threshold failures at the address within
        -- p_window_minutes, counted since the client's last success there and the administrator's
        -- last unblock; on an address no account has, since the window began. It lifts when the
        -- p_threshold-th latest of those failures leaves the window.
        -- p_shared: the client is a network address many clients share (a container gateway, a
        -- proxy that does not forward the client's address, a configured office address) and not
        -- a browser that signed in to the account before. From such an address a pause made by
        -- one client would keep out everyone behind it, so a valid, unexpired one-time set-up code
        -- (an account whose credential has an expiry) still signs in while the address is paused;
        -- any other answer from it, a wrong code included, is the same 'throttled' as before.
        CREATE FUNCTION identity.verify_sign_in(p_email text, p_proofs text[], p_source text, p_ip text, p_user_agent text,
                                                p_threshold integer, p_window_minutes integer, p_shared boolean DEFAULT false)
            RETURNS TABLE (challenge text, tenant_id uuid, paused_until timestamptz)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
        AS $fn$
        #variable_conflict use_column
        DECLARE
            v_email text := lower(btrim(coalesce(p_email, '')));
            v_threshold integer := least(greatest(coalesce(p_threshold, 5), 3), 50);
            v_window interval := make_interval(mins => least(greatest(coalesce(p_window_minutes, 15), 1), 1440));
            v_source text := left(coalesce(nullif(btrim(p_source), ''), 'unknown'), 64);
            v_epoch text := extract(epoch from now())::text;
            v_found boolean := false;
            v_matched boolean := false;
            v_counted boolean := false;
            v_key bytea;
            v_outcome text;
            v_success timestamptz;
            v_since timestamptz;
            v_sinces timestamptz[] := ARRAY[]::timestamptz[];
            v_nth timestamptz;
            v_until timestamptz;
            v_decoy text;
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
                    SELECT split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                      FROM identity.users u
                      JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                     WHERE u.email_normalized = v_email
                     ORDER BY 1
                     LIMIT 10
                LOOP
                    v_found := true;
                    challenge := r.ch;
                    tenant_id := NULL;
                    paused_until := NULL;
                    RETURN NEXT;
                END LOOP;
                IF NOT v_found THEN
                    SELECT 'pbkdf2-sha512$210000$' || encode(substr(sha256(s.secret || convert_to(v_email, 'UTF8')), 1, 16), 'base64')
                      INTO v_decoy
                      FROM identity.sign_in_secret s
                     WHERE s.id = 1;
                    challenge := v_decoy;
                    tenant_id := NULL;
                    paused_until := NULL;
                    RETURN NEXT;
                END IF;
                RETURN;
            END IF;

            -- The address and the client, keyed so that the table names neither.
            SELECT sha256(s.secret || convert_to('sign-in-failure' || chr(10) || v_email || chr(10) || v_source, 'UTF8'))
              INTO v_key
              FROM identity.sign_in_secret s
             WHERE s.id = 1;

            FOR r IN
                SELECT u.tenant_id AS t, u.id AS uid, u.is_active, u.sign_in_unblocked_at, c.password_hash AS stored, c.expires_at,
                       split_part(c.password_hash, '$', 1) || '$' || split_part(c.password_hash, '$', 2) || '$' || split_part(c.password_hash, '$', 3) AS ch
                  FROM identity.users u
                  JOIN identity.user_credentials c ON c.tenant_id = u.tenant_id AND c.id = u.id
                 WHERE u.email_normalized = v_email
                 ORDER BY 7
                 LIMIT 10
            LOOP
                v_found := true;
                PERFORM set_config('app.tenant_id', r.t::text, true), set_config('app.tenant_tx', v_epoch, true);
                SELECT max(a.occurred_at) INTO v_success
                  FROM identity.sign_in_attempts a
                 WHERE a.user_id = r.uid AND a.source = v_source AND a.outcome = 'succeeded';
                v_since := greatest(now() - v_window,
                                    coalesce(r.sign_in_unblocked_at, '-infinity'::timestamptz),
                                    coalesce(v_success, '-infinity'::timestamptz));
                v_sinces := v_sinces || v_since;
                SELECT f.occurred_at INTO v_nth
                  FROM identity.sign_in_failures f
                 WHERE f.key = v_key AND f.occurred_at > v_since
                 ORDER BY f.occurred_at DESC
                OFFSET v_threshold - 1 LIMIT 1;
                IF v_nth IS NOT NULL AND NOT (coalesce(p_shared, false) AND r.is_active AND r.expires_at IS NOT NULL
                                              AND r.expires_at > now() AND r.stored = ANY (p_proofs)) THEN
                    v_outcome := 'throttled';
                ELSIF NOT r.is_active THEN
                    v_outcome := 'inactive';
                    v_counted := true;
                ELSIF r.stored = ANY (p_proofs) THEN
                    v_outcome := CASE WHEN r.expires_at IS NOT NULL AND r.expires_at <= now() THEN 'expired' ELSE 'matched' END;
                ELSE
                    v_outcome := 'failed';
                    v_counted := true;
                END IF;
                IF v_outcome <> 'matched' THEN
                    INSERT INTO identity.sign_in_attempts (id, tenant_id, user_id, occurred_at, outcome, source, ip_address, user_agent)
                    VALUES (gen_random_uuid(), r.t, r.uid, clock_timestamp(), v_outcome, v_source, left(p_ip, 64), left(p_user_agent, 400));
                END IF;
                PERFORM set_config('app.tenant_id', '', true), set_config('app.tenant_tx', '', true);
                IF v_outcome = 'matched' THEN
                    v_matched := true;
                    challenge := r.ch;
                    tenant_id := r.t;
                    paused_until := NULL;
                    RETURN NEXT;
                END IF;
            END LOOP;
            IF v_matched THEN
                RETURN;
            END IF;

            IF NOT v_found THEN
                -- No account has the address: the same count, since the window began.
                v_sinces := ARRAY[now() - v_window];
                SELECT f.occurred_at INTO v_nth
                  FROM identity.sign_in_failures f
                 WHERE f.key = v_key AND f.occurred_at > now() - v_window
                 ORDER BY f.occurred_at DESC
                OFFSET v_threshold - 1 LIMIT 1;
                v_counted := v_nth IS NULL;
            END IF;
            IF v_counted THEN
                INSERT INTO identity.sign_in_failures (key, occurred_at) VALUES (v_key, clock_timestamp());
                DELETE FROM identity.sign_in_failures WHERE occurred_at < now() - interval '1440 minutes';
            END IF;

            -- Paused now (this attempt counted) for every account with the address, or for the
            -- address alone: until the earliest moment one of them lifts by itself.
            FOREACH v_since IN ARRAY v_sinces LOOP
                SELECT f.occurred_at INTO v_nth
                  FROM identity.sign_in_failures f
                 WHERE f.key = v_key AND f.occurred_at > v_since
                 ORDER BY f.occurred_at DESC
                OFFSET v_threshold - 1 LIMIT 1;
                IF v_nth IS NULL THEN
                    RETURN;
                END IF;
                v_until := least(coalesce(v_until, 'infinity'::timestamptz), v_nth + v_window);
            END LOOP;
            IF v_until IS NOT NULL THEN
                challenge := NULL;
                tenant_id := NULL;
                paused_until := v_until;
                RETURN NEXT;
            END IF;
        END
        $fn$;
        RESET ROLE;
        REVOKE CREATE ON SCHEMA identity FROM erp_auth;
        REVOKE ALL ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer, boolean) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer, boolean) TO erp_app;
        """;

    /// <summary>Back to the seven-argument function of <see cref="PauseByAddressUp"/>.</summary>
    public const string SetupCodeFromSharedAddressDown = """
        GRANT CREATE ON SCHEMA identity TO erp_auth;
        SET LOCAL ROLE erp_auth;
        DROP FUNCTION identity.verify_sign_in(text, text[], text, text, text, integer, integer, boolean);

        """ + PauseByAddressFunction;
}
