#!/bin/bash
# md5 of tenant B's (gulfsteel) jobs tables, as superuser
docker exec c-p12-jobs-r6-db-1 psql -U postgres -d erp -At -c "
WITH b AS (SELECT id FROM tenancy.tenants WHERE code='gulfsteel')
SELECT 'jobs', count(*), md5(string_agg(j::text, '|' ORDER BY j.id)) FROM jobs.jobs j WHERE tenant_id IN (SELECT id FROM b)
UNION ALL SELECT 'attempts', count(*), md5(string_agg(a::text, '|' ORDER BY a.id)) FROM jobs.job_attempts a WHERE tenant_id IN (SELECT id FROM b)
UNION ALL SELECT 'schedules', count(*), md5(string_agg(s::text, '|' ORDER BY s.id)) FROM jobs.schedules s WHERE tenant_id IN (SELECT id FROM b);" 2>&1
