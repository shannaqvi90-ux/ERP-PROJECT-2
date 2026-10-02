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
}
