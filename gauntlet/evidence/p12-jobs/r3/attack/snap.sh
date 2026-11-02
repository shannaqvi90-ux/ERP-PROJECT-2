docker exec c-p12-jobs-r3-db-1 psql -U postgres -d erp -At -c "
select 'jobs', count(*), md5(string_agg(j::text, ',' order by id)) from jobs.jobs j where tenant_id=(select id from tenancy.tenants where code='gulfsteel')
union all select 'attempts', count(*), md5(string_agg(a::text, ',' order by id)) from jobs.job_attempts a where tenant_id=(select id from tenancy.tenants where code='gulfsteel')
union all select 'schedules', count(*), md5(string_agg(s::text, ',' order by id)) from jobs.schedules s where tenant_id=(select id from tenancy.tenants where code='gulfsteel')"
