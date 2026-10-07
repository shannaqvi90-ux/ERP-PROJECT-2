# Runs inside the toolbox container: /src is the clone (read-only).
set -u
mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git web | tar -C /work -xf -
cd /work/web && npm ci --no-audit --no-fund --loglevel=error >/dev/null
F=src/kernel/forms/RecordForm.tsx; U=src/kernel/forms/useRecordForm.ts
cp $F /tmp/rf.tsx; cp $U /tmp/urf.ts
run() { echo "== $1"; grep -n "PLANT" $F $U; npx vitest run --reporter=dot 2>&1 | grep -E "Tests |Test Files |FAIL" | head -15; node scripts/tenancy-plant-self-test.mjs 2>&1 | tail -2; node scripts/identity-plant-self-test.mjs 2>&1 | tail -2; }
run baseline
# W1: the kernel record form offers editing and Save to a read-only user (fields enabled, Save button and keys).
sed -i 's/const editable = !form.readOnly;/const editable = true; \/\/ PLANT W1/' $F
sed -i 's/<FormContext.Provider value={{ readOnly: form.readOnly }}>/<FormContext.Provider value={{ readOnly: false }}>{\/* PLANT W1 *\/}/' $F
sed -i 's/<fieldset className="form-section" disabled={readOnly}>/<fieldset className="form-section" disabled={false}>{\/* PLANT W1 *\/}/' $F
run W1
cp /tmp/rf.tsx $F
# W2: Ctrl+S saves a read-only record: the save keys stay on and save() no longer checks canEdit (nothing visible changes).
sed -i 's/useShortcut({ id: "forms.save", chord: formChords.save, labelKey: "forms.shortcut.save", groupKey: group, enabled: editable,/useShortcut({ id: "forms.save", chord: formChords.save, labelKey: "forms.shortcut.save", groupKey: group, enabled: true \/* PLANT W2 *\/,/' $F
sed -i 's/if (!current.canEdit || busy) return null;/if (busy) return null; \/\/ PLANT W2/' $U
run W2
cp /tmp/rf.tsx $F; cp /tmp/urf.ts $U
