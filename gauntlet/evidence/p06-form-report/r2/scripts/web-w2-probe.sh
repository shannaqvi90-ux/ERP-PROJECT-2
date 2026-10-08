# Inside the toolbox: shows that plant W2 sends a save from a read-only form (a probe assertion the
# suite lacks), so the plant is observable; the unchanged suite passes with it.
set -u
mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git web | tar -C /work -xf -
cd /work/web && npm ci --no-audit --no-fund --loglevel=error >/dev/null
T=src/kernel/forms/forms.test.tsx; F=src/kernel/forms/RecordForm.tsx; U=src/kernel/forms/useRecordForm.ts
# probe: the read-only test records its fetch calls and says whether a write went out after Ctrl+S
python3 - <<'PY'
p='src/kernel/forms/forms.test.tsx'; s=open(p).read()
old='''    mockFetch((method, url) => (method === "GET" && url === "/api/things/t1" ? { status: 200, body: thing } : { status: 404, body: {} }));
    await show(<ThingForm canEdit={false} />);'''
new='''    const probeCalls = mockFetch((method, url) => (method === "GET" && url === "/api/things/t1" ? { status: 200, body: thing } : { status: 404, body: {} }));
    await show(<ThingForm canEdit={false} />);'''
assert old in s; s=s.replace(old,new,1)
old2='''    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
  });

  it("moves to the next'''
new2='''    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    expect(probeCalls.filter((c) => c.method !== "GET").map((c) => `${c.method} ${c.url}`)).toEqual([]); // CRITIC PROBE
  });

  it("moves to the next'''
assert old2 in s; s=s.replace(old2,new2,1); open(p,'w').write(s)
PY
echo "== probe on the unplanted product"; npx vitest run $T --reporter=dot 2>&1 | grep -E "Tests |FAIL|AssertionError|Expected|Received|\+ |\- " | head -12
sed -i 's/useShortcut({ id: "forms.save", chord: formChords.save, labelKey: "forms.shortcut.save", groupKey: group, enabled: editable,/useShortcut({ id: "forms.save", chord: formChords.save, labelKey: "forms.shortcut.save", groupKey: group, enabled: true \/* PLANT W2 *\/,/' $F
sed -i 's/if (!current.canEdit || busy) return null;/if (busy) return null; \/\/ PLANT W2/' $U
echo "== probe with plant W2"; npx vitest run $T --reporter=dot 2>&1 | grep -E "Tests |FAIL|AssertionError|Expected|Received|\+ |\- |PUT|POST" | head -12
