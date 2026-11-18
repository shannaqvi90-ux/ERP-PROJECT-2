\echo == as erp_app, no tenant bound
select count(*) as seq_visible from numbering.sequences;
select count(*) as issued_visible from numbering.issued_numbers;
\echo == bind tenant A (alnoor) with tx stamp, company scope all
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.company_tx', extract(epoch from now())::text, true), set_config('app.company_scope','all',true);
select tenant_id, count(*) from numbering.sequences group by 1;
select tenant_id, count(*) from numbering.counters group by 1;
select tenant_id, count(*) from numbering.issued_numbers group by 1;
\echo -- update B rows (expect 0)
update numbering.sequences set prefix='HACK-' where tenant_id='0190a000-0000-7000-8000-000000000002';
update numbering.counters set next_value=1 where tenant_id='0190a000-0000-7000-8000-000000000002';
delete from numbering.counters where tenant_id='0190a000-0000-7000-8000-000000000002';
\echo -- insert a sequence row for tenant B (expect RLS refusal)
savepoint s1;
insert into numbering.sequences (id, tenant_id, company_id, document_type, prefix, suffix, padding, reset, gapless, per_branch, created_by, updated_by) select gen_random_uuid(), '0190a000-0000-7000-8000-000000000002', company_id, 'zz.test', 'X', '', 5, 0, false, false, null, null from numbering.sequences limit 1;
rollback to s1;
\echo -- move A row into tenant B (expect RLS refusal)
savepoint s2;
update numbering.sequences set tenant_id='0190a000-0000-7000-8000-000000000002' where id=(select id from numbering.sequences limit 1);
rollback to s2;
\echo -- update / delete issued numbers of A (expect permission denied)
savepoint s3;
update numbering.issued_numbers set number='X' where id=(select id from numbering.issued_numbers limit 1);
rollback to s3;
savepoint s4;
delete from numbering.issued_numbers;
rollback to s4;
rollback;
\echo == tenant set without tx stamp
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000002',false);
select count(*) as seq_visible_without_stamp from numbering.sequences;
rollback;
