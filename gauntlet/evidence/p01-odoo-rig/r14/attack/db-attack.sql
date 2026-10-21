\echo == roles (name|super|bypassrls)
select rolname, rolsuper, rolbypassrls from pg_roles where rolname like 'erp%' order by 1;
\echo == tenant_id tables: name|rls|forced
select n.nspname||'.'||c.relname, c.relrowsecurity, c.relforcerowsecurity from pg_class c join pg_namespace n on n.oid=c.relnamespace join pg_attribute a on a.attrelid=c.oid and a.attname='tenant_id' where c.relkind in ('r','p') order by 1;
\echo == tenant_id tables without RLS forced
select count(*) from pg_class c join pg_attribute a on a.attrelid=c.oid and a.attname='tenant_id' where c.relkind in ('r','p') and not (c.relrowsecurity and c.relforcerowsecurity);
\echo == app role, no context
set role erp_app;
select 'no context users', count(*) from identity.users;
select 'no context companies', count(*) from tenancy.companies;
reset role;
\echo == app role as tenant A (alnoor), stamped
set role erp_app;
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
select 'A sees users', count(*) from identity.users;
select 'A sees B users', count(*) from identity.users where tenant_id='0190a000-0000-7000-8000-000000000002';
select 'A sees B companies', count(*) from tenancy.companies where tenant_id='0190a000-0000-7000-8000-000000000002';
select 'A sees B audit', count(*) from audit.entries where tenant_id='0190a000-0000-7000-8000-000000000002';
select 'A sees B views', count(*) from lists.saved_views where tenant_id='0190a000-0000-7000-8000-000000000002';
update identity.users set display_name='pwned' where tenant_id='0190a000-0000-7000-8000-000000000002';
rollback;
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
insert into lists.saved_views (id, tenant_id, list_key, name) values (gen_random_uuid(), '0190a000-0000-7000-8000-000000000002', 'identity.users', 'x');
rollback;
\echo == tenant B id set without the tx stamp
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000002',true);
select 'no stamp users', count(*) from identity.users;
rollback;
\echo == audit tamper / RLS off as app
delete from audit.entries;
update audit.entries set tenant_id=tenant_id;
alter table identity.users disable row level security;
reset role;
