namespace Erp.Modules.Tenancy;

/// <summary>Database-side bookkeeping of the tenancy module.</summary>
internal static class TenancySql
{
    /// <summary>
    /// Counts kept by triggers, so a request can compare what it sees with the whole without
    /// reading rows outside its company scope: how many companies the workspace has
    /// (<c>tenants.company_count</c>; companies are never deleted) and how many companies each
    /// user may work in (<c>user_company_totals</c>). Both functions run with the caller's rights
    /// (no SECURITY DEFINER): the row being inserted or deleted is already one the caller may
    /// write, and the counters are tenant rows of the caller's own tenant. Existing rows are
    /// counted once here (the owner reads past row-level security only inside this migration's
    /// transaction; FORCE is restored before it commits).
    /// </summary>
    public const string AccessCounts = """
        CREATE FUNCTION tenancy.count_company() RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = pg_catalog, pg_temp
        AS $$
        BEGIN
            UPDATE tenancy.tenants SET company_count = company_count + 1 WHERE id = NEW.tenant_id;
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION tenancy.count_company() FROM PUBLIC;
        CREATE TRIGGER count_company AFTER INSERT ON tenancy.companies
            FOR EACH ROW EXECUTE FUNCTION tenancy.count_company();

        CREATE FUNCTION tenancy.count_user_companies() RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = pg_catalog, pg_temp
        AS $$
        BEGIN
            IF TG_OP IN ('DELETE', 'UPDATE') THEN
                UPDATE tenancy.user_company_totals SET company_count = company_count - 1, updated_at = now()
                 WHERE tenant_id = OLD.tenant_id AND user_id = OLD.user_id;
            END IF;
            IF TG_OP IN ('INSERT', 'UPDATE') THEN
                INSERT INTO tenancy.user_company_totals (id, tenant_id, user_id, company_count, created_at, updated_at)
                VALUES (gen_random_uuid(), NEW.tenant_id, NEW.user_id, 1, now(), now())
                ON CONFLICT (tenant_id, user_id) DO UPDATE SET company_count = tenancy.user_company_totals.company_count + 1, updated_at = now();
            END IF;
            RETURN NULL;
        END;
        $$;
        REVOKE ALL ON FUNCTION tenancy.count_user_companies() FROM PUBLIC;
        CREATE TRIGGER count_user_companies AFTER INSERT OR DELETE OR UPDATE OF tenant_id, user_id ON tenancy.user_company_access
            FOR EACH ROW EXECUTE FUNCTION tenancy.count_user_companies();

        SELECT set_config('app.actor_kind', 'migration', true);
        ALTER TABLE tenancy.tenants NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE tenancy.companies NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE tenancy.user_company_access NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE tenancy.user_company_totals NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries NO FORCE ROW LEVEL SECURITY;
        UPDATE tenancy.tenants t SET company_count = (SELECT count(*) FROM tenancy.companies c WHERE c.tenant_id = t.id);
        INSERT INTO tenancy.user_company_totals (id, tenant_id, user_id, company_count, created_at, updated_at)
            SELECT gen_random_uuid(), tenant_id, user_id, count(*), now(), now() FROM tenancy.user_company_access GROUP BY tenant_id, user_id;
        ALTER TABLE tenancy.tenants FORCE ROW LEVEL SECURITY;
        ALTER TABLE tenancy.companies FORCE ROW LEVEL SECURITY;
        ALTER TABLE tenancy.user_company_access FORCE ROW LEVEL SECURITY;
        ALTER TABLE tenancy.user_company_totals FORCE ROW LEVEL SECURITY;
        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;
        """;

    public const string AccessCountsDown = """
        DROP TRIGGER IF EXISTS count_user_companies ON tenancy.user_company_access;
        DROP FUNCTION IF EXISTS tenancy.count_user_companies();
        DROP TRIGGER IF EXISTS count_company ON tenancy.companies;
        DROP FUNCTION IF EXISTS tenancy.count_company();
        """;
}
