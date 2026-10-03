#!/usr/bin/env bash
cd /home/user/critic/p00-foundation-r3
for t in $(cat /home/user/evidence-staging/p00-foundation/r3/attack/tenant-tables.txt); do
  printf '%s ' $t; env ERP_PROJECT=c-p00-foundation-r3 ERP_HTTP_PORT=20050 ERP_DB_PORT=20051 ./erp psql -At -c "select count(*)||':'||coalesce(md5(string_agg(x::text, '|' order by x::text)),'') from $t x where tenant_id='0190a000-0000-7000-8000-000000000002'" 2>/dev/null | tail -1
done
