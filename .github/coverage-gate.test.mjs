// Self-test of coverage-gate.mjs (Node 24): `node --test .github/coverage-gate.test.mjs`.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { isPositionalRecordParameter, mergeReports, summarize } from './coverage-gate.mjs';

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), 'coverage-gate.mjs');

const report = (pkg, file, lines, methodLines = []) => `<?xml version="1.0"?>
<coverage><packages><package name="${pkg}"><classes>
<class name="C" filename="${file}"><methods><method name="M"><lines>${methodLines.map(([n, h]) => `<line number="${n}" hits="${h}" />`).join('')}</lines></method></methods>
<lines>${lines.map(([n, h]) => `<line number="${n}" hits="${h}" branch="False" />`).join('')}</lines></class>
</classes></package></packages></coverage>`;

function run(files, args = []) {
  const dir = mkdtempSync(join(tmpdir(), 'coverage-gate-'));
  try {
    for (const [path, content] of Object.entries(files)) {
      mkdirSync(dirname(join(dir, path)), { recursive: true });
      writeFileSync(join(dir, path), content);
    }
    return spawnSync(process.execPath, [SCRIPT, '--reports', dir, ...args], { encoding: 'utf8' });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test('a line hit in any report counts as covered', () => {
  const s = summarize(mergeReports([report('A', 'a.cs', [[1, 0], [2, 3]]), report('A', 'a.cs', [[1, 1]])]));
  assert.deepEqual([s.covered, s.total], [2, 2]);
});

test('method copies of a line are not counted twice, and uncovered lines are named', () => {
  const s = summarize(mergeReports([report('A', 'a.cs', [[1, 1], [2, 0]], [[1, 1], [2, 0]])]));
  assert.deepEqual([s.covered, s.total, s.uncovered], [1, 2, ['a.cs:2']]);
});

test('full coverage passes, one missed line fails a 100 % gate', () => {
  assert.equal(run({ 'x/coverage.cobertura.xml': report('A', 'a.cs', [[1, 1]]) }).status, 0);
  const res = run({ 'x/coverage.cobertura.xml': report('A', 'a.cs', [[1, 1], [2, 0]]) });
  assert.equal(res.status, 1);
  assert.match(res.stderr, /not covered: a\.cs:2/);
  assert.equal(run({ 'x/coverage.cobertura.xml': report('A', 'a.cs', [[1, 1], [2, 0]]) }, ['--min', '50']).status, 0);
});

test('no report, or a report without lines, fails', () => {
  assert.equal(run({ 'readme.txt': 'x' }).status, 1);
  assert.equal(run({ 'x/coverage.cobertura.xml': '<coverage><packages></packages></coverage>' }).status, 1);
});

const accessor = (file, name, n, hits, source) => `<?xml version="1.0"?>
<coverage><sources><source>${source}</source></sources><packages><package name="A"><classes>
<class name="R" filename="${file}"><methods><method name="${name}"><lines><line number="${n}" hits="${hits}" /></lines></method></methods>
<lines><line number="${n}" hits="${hits}" branch="False" /></lines></class>
</classes></package></packages></coverage>`;

test('a synthesized positional record accessor nobody reads is left out', () => {
  const src = 'namespace N;\n\n/// <summary>R.</summary>\npublic sealed record R(string? TenantId, string Locale);\n';
  const s = summarize(mergeReports([accessor('r.cs', 'get_TenantId', 4, 0, '/src/')], () => src));
  assert.equal(s.total, 0);
});

test('a parameter on its own line of a multi-line record counts as positional', () => {
  const src = 'public sealed record R(\n    long Sequence,\n    DateTimeOffset? Timestamp,\n    string Text);\n';
  assert.equal(isPositionalRecordParameter(src, 3, 'Timestamp'), true);
  assert.equal(isPositionalRecordParameter('private readonly record struct K(string? A, string B);', 1, 'A'), true);
});

test('a hand-written getter, a method parameter or an unreadable source keeps the line counted', () => {
  assert.equal(isPositionalRecordParameter('public record R\n{\n    public string A { get; init; }\n}', 3, 'A'), false);
  assert.equal(isPositionalRecordParameter('public record R(string A)\n{\n    public string A => field;\n}', 3, 'A'), false);
  assert.equal(isPositionalRecordParameter('public sealed class C(string A);', 1, 'A'), false);
  assert.equal(isPositionalRecordParameter('public record R\n{\n    public void M(string A) { }\n}', 3, 'A'), false);
  const s = summarize(mergeReports([accessor('r.cs', 'get_A', 1, 0, '/src/')], () => undefined));
  assert.deepEqual(s.uncovered, ['r.cs:1']);
});

test('a line an ordinary method also reports keeps counting', () => {
  const src = 'public record R(string A);\n';
  const both = `<?xml version="1.0"?>
<coverage><packages><package name="A"><classes>
<class name="R" filename="r.cs"><methods>
<method name="get_A"><lines><line number="1" hits="0" /></lines></method>
<method name="Validate"><lines><line number="1" hits="0" /></lines></method>
</methods><lines><line number="1" hits="0" branch="False" /></lines></class>
</classes></package></packages></coverage>`;
  assert.deepEqual(summarize(mergeReports([both], () => src)).uncovered, ['r.cs:1']);
});
