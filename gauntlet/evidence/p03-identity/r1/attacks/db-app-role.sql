\echo == as erp_app, no tenant bound
select count(*) as users_visible from identity.users;
select count(*) as roles_visible from identity.roles;
select count(*) as attempts_visible from identity.sign_in_attempts;
\echo == credentials column
select password_hash from identity.user_credentials limit 1;
\echo == bind to tenant A in a transaction, try to read B
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true);
select count(*) as a_users from identity.users;
select count(*) as b_rows_seen_from_a from identity.users where tenant_id='0190a000-0000-7000-8000-000000000002';
\echo == insert a row claiming tenant B while bound to A
insert into identity.roles(id, tenant_id, name_en, name_ar, permissions, is_system, created_at, updated_at) values (gen_random_uuid(),'0190a000-0000-7000-8000-000000000002','Evil','x','{}',false,now(),now());
rollback;
\echo == reviewed function from the app role: what does it reveal for B's address?
select * from identity.verify_sign_in('admin@gulfsteel.example', NULL, 'critic', NULL, NULL, 5, 15);
select * from identity.verify_sign_in('nobody@nowhere.example', NULL, 'critic', NULL, NULL, 5, 15);
\echo == app role attributes
select rolname, rolsuper, rolbypassrls, rolinherit from pg_roles where rolname like 'erp%';
\echo == can the app role change RLS or policies?
alter table identity.users disable row level security;
\echo == functions callable
select n.nspname, p.proname, p.prosecdef from pg_proc p join pg_namespace n on n.oid=p.pronamespace where n.nspname not in ('pg_catalog','information_schema') and p.prosecdef;
