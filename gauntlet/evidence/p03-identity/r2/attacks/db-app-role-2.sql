\echo == can erp_app become erp_auth (which reads every tenant)?
set role erp_auth;
select pg_has_role('erp_app','erp_auth','member') as app_member_of_auth;
\echo == bound properly to tenant A, read and write B
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
select count(*) as a_users from identity.users;
select count(*) as b_users_seen_from_a from identity.users where tenant_id='0190a000-0000-7000-8000-000000000002';
update identity.users set display_name='pwned' where tenant_id='0190a000-0000-7000-8000-000000000002';
update identity.roles set tenant_id='0190a000-0000-7000-8000-000000000002' where is_system=false and tenant_id='0190a000-0000-7000-8000-000000000001';
rollback;
\echo == tenant left in the session (not the transaction) is ignored
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000002', false), set_config('app.tenant_tx', extract(epoch from now())::text, false);
select count(*) as users_with_session_level_tenant from identity.users;
