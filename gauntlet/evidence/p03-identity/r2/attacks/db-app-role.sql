\echo == as erp_app, no tenant bound
select count(*) as users_visible from identity.users;
select count(*) as roles_visible from identity.roles;
select count(*) as user_roles_visible from identity.user_roles;
select count(*) as attempts_visible from identity.sign_in_attempts;
select count(*) as sessions_visible from identity.sessions;
\echo == credentials column
select password_hash from identity.user_credentials limit 1;
\echo == bind to tenant A in a transaction, try to read and write B
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true);
select count(*) as a_users from identity.users;
select count(*) as b_rows_seen_from_a from identity.users where tenant_id='0190a000-0000-7000-8000-000000000002';
update identity.users set display_name='pwned' where tenant_id='0190a000-0000-7000-8000-000000000002';
delete from identity.user_roles where tenant_id='0190a000-0000-7000-8000-000000000002';
insert into identity.roles(id, tenant_id, name_en, name_ar, permissions, is_system, created_at, updated_at) values (gen_random_uuid(),'0190a000-0000-7000-8000-000000000002','Evil','x','{}',false,now(),now());
rollback;
\echo == move an A row into tenant B
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true);
update identity.roles set tenant_id='0190a000-0000-7000-8000-000000000002' where is_system=false;
rollback;
\echo == app role attributes
select rolname, rolsuper, rolbypassrls, rolcreaterole from pg_roles where rolname like 'erp%';
\echo == RLS on identity tables (enabled, forced)
select c.relname, c.relrowsecurity, c.relforcerowsecurity from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='identity' and c.relkind='r' order by 1;
\echo == can the app role change RLS?
alter table identity.users disable row level security;
set role erp_owner;
\echo == unique indexes in identity
select indexrelid::regclass, pg_get_indexdef(indexrelid) from pg_index i join pg_class c on c.oid=i.indrelid join pg_namespace n on n.oid=c.relnamespace where n.nspname='identity' and i.indisunique;
