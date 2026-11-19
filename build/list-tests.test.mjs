// Tests of the partition check's test listing (build/list-tests.sh): a good listing is read exactly,
// and a broken one (two names on a line, a cut line carrying a log line, a test before the header, no
// header) is reported as a broken listing (exit 3), never as a missing or an extra test.
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import assert from 'node:assert/strict';

const build = dirname(fileURLToPath(import.meta.url));
const lib = join(build, 'list-tests.sh');
const HEAD = 'Test run for /x/A.dll (.NETCoreApp,Version=v10.0)\nVSTest version 18.0.2 (x64)\n\nThe following Tests are available:\n';

function parse(text) {
  const r = spawnSync('bash', ['-c', `source ${lib}; list_parse P`], { input: text, encoding: 'utf8' });
  return { code: r.status, out: r.stdout, err: r.stderr };
}

test('a good listing gives its names, in order, theory arguments kept', () => {
  const r = parse(`${HEAD}    A.B.One\n    A.B.Two(x: "a b", y: 3)\n    A.C.Three\n`);
  assert.equal(r.code, 0);
  assert.equal(r.out, 'A.B.One\nA.B.Two(x: "a b", y: 3)\nA.C.Three\n');
});

test('a project without tests (no output) and a filter matching nothing (header only) give no names', () => {
  assert.deepEqual(parse(''), { code: 0, out: '', err: '' });
  assert.deepEqual(parse(HEAD), { code: 0, out: '', err: '' });
});

for (const [what, text] of [
  ['two names on one line', `${HEAD}    A.B.One    A.B.Two\n    A.C.Three\n`],
  ['two names on one line, the second a theory', `${HEAD}    A.B.One    A.B.Two(x: 1)\n`],
  ['a log line in a test line', `${HEAD}    A.B.One warn: something\n`],
  ['a test line before the header', `    A.B.One\n${HEAD}    A.B.Two\n`],
  ['no header and not empty (a crash, a build error)', 'error MSB1009: Project file does not exist.\n'],
  ['two listings in one stream (a second header)', `${HEAD}    A.B.One\n${HEAD}    A.B.Two\n`],
]) {
  test(`a broken listing is reported as broken: ${what}`, () => {
    const r = parse(text);
    assert.equal(r.code, 3);
    assert.match(r.err, /LISTING BROKEN/);
  });
}

test('the solution is listed one project at a time, and a broken project fails the whole listing', () => {
  const dir = mkdtempSync(join(tmpdir(), 'list-tests-'));
  try {
    mkdirSync(join(dir, 'bin'));
    writeFileSync(join(dir, 'T.slnx'), '<Solution><Project Path="tests/A/A.csproj" /><Project Path="tests/B/B.csproj" /></Solution>');
    const fake = join(dir, 'bin', 'dotnet');
    writeFileSync(fake, `#!/bin/bash
# stand-in: A lists two tests, B lists one (or, with BROKEN set, two names on a line)
echo "$@" >>"$FAKE_LOG"
case "$2" in
  tests/A/A.csproj) printf '${HEAD.replace(/\n/g, '\\n')}    N.A.One\\n    N.A.Two\\n' ;;
  tests/B/B.csproj) if [[ -n "$BROKEN" ]]; then printf '${HEAD.replace(/\n/g, '\\n')}    N.B.One    N.B.Two\\n'; else printf '${HEAD.replace(/\n/g, '\\n')}    N.B.One\\n'; exit 1; fi ;;
esac
`);
    chmodSync(fake, 0o755);
    const run = env => spawnSync('bash', ['-c', `source ${lib}; list_tests T.slnx --filter X`], {
      cwd: dir, encoding: 'utf8', env: { ...process.env, ...env, PATH: `${join(dir, 'bin')}:${process.env.PATH}`, FAKE_LOG: join(dir, 'log') },
    });
    const ok = run({});
    assert.equal(ok.status, 0);
    assert.equal(ok.stdout, 'N.A.One\nN.A.Two\nN.B.One\n');
    const bad = run({ BROKEN: '1' });
    assert.equal(bad.status, 3);
    assert.match(bad.stderr, /LISTING BROKEN \(tests\/B\/B\.csproj\)/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
