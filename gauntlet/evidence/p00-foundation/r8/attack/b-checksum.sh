#!/usr/bin/env bash
# md5 of every tenant table's rows for tenant B (gulfsteel), as the superuser.
docker exec -i c-p00-foundation-r8-db-1 psql -U postgres -d erp -At <<'SQL'
DO $$ DECLARE r record; h text; b uuid; BEGIN
  SELECT id INTO b FROM tenancy.tenants WHERE code = 'gulfsteel';
  FOR r IN SELECT table_schema s, table_name t FROM information_schema.columns WHERE column_name='tenant_id' AND table_schema NOT IN ('pg_catalog','information_schema') ORDER BY 1,2 LOOP
    EXECUTE format('SELECT md5(coalesce(string_agg(x::text, ''|'' ORDER BY x::text), '''')) || '' '' || count(*) FROM %I.%I x WHERE tenant_id = $1', r.s, r.t) INTO h USING b;
    RAISE NOTICE '%.% %', r.s, r.t, h;
  END LOOP; END $$;
SQL
