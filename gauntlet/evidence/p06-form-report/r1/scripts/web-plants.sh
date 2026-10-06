set -u
tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf -
cd /work/web && npm ci --no-audit --no-fund --loglevel=error >/dev/null
echo "== baseline"; npx vitest run --reporter=dot 2>&1 | grep -E "Tests |Test Files " 
cp src/kernel/forms/useRecordForm.ts /tmp/urf.ts
sed -i 's/const readOnly = !spec.canEdit;/const readOnly = false; \/\/ PLANT P3a/' src/kernel/forms/useRecordForm.ts
echo "== P3a useRecordForm ignores canEdit"; grep -n "PLANT" src/kernel/forms/useRecordForm.ts; npx vitest run --reporter=dot 2>&1 | grep -E "FAIL|Tests |Test Files |✗|×" | head -20
cp /tmp/urf.ts src/kernel/forms/useRecordForm.ts
cp src/modules/tenancy/CompanyForm.tsx /tmp/cf.tsx
sed -i 's/canEdit: id === null ? can("tenancy.companies.create") : can("tenancy.companies.update"),/canEdit: true, \/\/ PLANT P3b/' src/modules/tenancy/CompanyForm.tsx
echo "== P3b company form editable for everyone"; grep -n "PLANT" src/modules/tenancy/CompanyForm.tsx; npx vitest run --reporter=dot 2>&1 | grep -E "FAIL|Tests |Test Files |×" | head -20
node scripts/identity-plant-self-test.mjs 2>&1 | tail -3
cp /tmp/cf.tsx src/modules/tenancy/CompanyForm.tsx
