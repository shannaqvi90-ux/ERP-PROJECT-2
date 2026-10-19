\set ON_ERROR_STOP off
select current_user, (select rolbypassrls from pg_roles where rolname=current_user) bypass;
select 'no tenant: users', count(*) from identity.users;
select 'no tenant: roles', count(*) from identity.roles;
-- every tenant table: RLS enabled and forced
select 'tenant tables without forced RLS', count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace join pg_attribute a on a.attrelid=c.oid and a.attname='tenant_id' where c.relkind in ('r','p') and n.nspname not in ('pg_catalog','information_schema') and not (c.relrowsecurity and c.relforcerowsecurity);
select n.nspname||'.'||c.relname from pg_class c join pg_namespace n on n.oid=c.relnamespace join pg_attribute a on a.attrelid=c.oid and a.attname='tenant_id' where c.relkind in ('r','p') and n.nspname not in ('pg_catalog','information_schema') and not (c.relrowsecurity and c.relforcerowsecurity);
select 'tenant tables total', count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace join pg_attribute a on a.attrelid=c.oid and a.attname='tenant_id' where c.relkind in ('r','p') and n.nspname not in ('pg_catalog','information_schema');
select 'roles with bypassrls', string_agg(rolname, ',') from pg_roles where rolbypassrls and rolname like 'erp%';
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
select 'A bound users', count(*), count(*) filter (where tenant_id <> '0190a000-0000-7000-8000-000000000001') from identity.users;
select 'A bound roles', count(*), count(*) filter (where tenant_id <> '0190a000-0000-7000-8000-000000000001') from identity.roles;
select 'A bound user_roles', count(*), count(*) filter (where tenant_id <> '0190a000-0000-7000-8000-000000000001') from identity.user_roles;
select 'A bound sessions', count(*), count(*) filter (where tenant_id <> '0190a000-0000-7000-8000-000000000001') from identity.sessions;
select 'A bound sign_in_attempts', count(*), count(*) filter (where tenant_id <> '0190a000-0000-7000-8000-000000000001') from identity.sign_in_attempts;
select 'A bound audit', count(*), count(*) filter (where tenant_id <> '0190a000-0000-7000-8000-000000000001') from audit.entries;
update identity.users set display_name = display_name||'x' where tenant_id='0190a000-0000-7000-8000-000000000002';
update identity.roles set name_en = name_en where tenant_id='0190a000-0000-7000-8000-000000000002';
delete from identity.user_roles where tenant_id='0190a000-0000-7000-8000-000000000002';
rollback;
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
insert into identity.roles (id, tenant_id, name_en, name_ar) values (gen_random_uuid(), '0190a000-0000-7000-8000-000000000002', 'evil','evil');
rollback;
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true);
select 'A bound w/o tenant_tx users', count(*) from identity.users;
rollback;
set row_security = off;
select 'row_security off users', count(*) from identity.users;
reset row_security;
set role erp_owner;
set role postgres;
alter table identity.users disable row level security;
reset role;
set role erp_auth;
select 'erp_app sees erp_auth policy rows', count(*) from identity.user_credentials;
-- A bound, read a B user via security-definer functions exposed to erp_app
select 'functions executable by erp_app (security definer)', string_agg(n.nspname||'.'||p.proname, ', ') from pg_proc p join pg_namespace n on n.oid=p.pronamespace where p.prosecdef and has_function_privilege('erp_app', p.oid, 'execute') and n.nspname not in ('pg_catalog','information_schema');
-- r13 additions: the device approval columns and the sign-in function's new arguments
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
select 'A bound: users with an approval, other tenant', count(*) from identity.users where device_approval_hash is not null and tenant_id <> '0190a000-0000-7000-8000-000000000001';
update identity.users set device_approval_hash = '\x00', device_approval_until = now() + interval '1 day' where tenant_id = '0190a000-0000-7000-8000-000000000002';
select 'A bound verify_sign_in for B admin', * from identity.verify_sign_in('admin@gulfsteel.example', null, 'x','1.2.3.4','ua',5,15,true,'device:abc','\x00');
rollback;
select 'unbound: B approvals visible', count(*) from identity.users where tenant_id='0190a000-0000-7000-8000-000000000002';
select 'column privilege erp_app on device_approval_hash', has_column_privilege('erp_app','identity.users','device_approval_hash','SELECT');
