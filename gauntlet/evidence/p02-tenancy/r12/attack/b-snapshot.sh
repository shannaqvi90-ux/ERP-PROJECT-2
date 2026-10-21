#!/bin/bash
# md5 of every tenant B row, per table (superuser read)
B=0190a000-0000-7000-8000-000000000002
P="docker exec -i c-p02-tenancy-r12-db-1 psql -U postgres -d erp -At"
for t in $($P -c "select table_schema||'.'||table_name from information_schema.columns where column_name='tenant_id' and table_schema not in ('pg_catalog') order by 1"); do
  echo "$t $($P -c "select count(*)||' '||coalesce(md5(string_agg(md5(x::text),'' order by md5(x::text))),'-') from $t x where tenant_id='$B'")"
done
echo "tenancy.tenants $($P -c "select md5(x::text) from tenancy.tenants x where id='$B'")"
