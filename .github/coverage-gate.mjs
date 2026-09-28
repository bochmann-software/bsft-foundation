// The line coverage gate of this repository (Node 24, no dependencies, no network).
//
// Reads the Cobertura reports `dotnet test --collect "XPlat Code Coverage"` leaves behind, merges them
// line by line and fails when the covered share of the lines is below the minimum. It needs no
// licence or secret, so it runs on every pull request, forks included. A line counts as covered when
// any report hit it: each test project measures only the module it covers (Directory.Build.targets
// derives that filter), so a line appears in more than one report only where two projects measure
// the same module.
//
// One kind of line is left out, the same one CodeCharter's gate leaves out in the private repository:
// the accessor the compiler writes for a positional record parameter (`record R(string? A)` gets a
// `get_A` nobody typed). Coverlet reports such an accessor on the parameter's line, and it is uncovered
// whenever no caller reads that property, although the parameter itself is used through construction,
// equality and deconstruction. A zero-hit line is left out only when every method reporting it is such
// an accessor and the source, read next to the report, shows that line inside a record's parameter list;
// a hand-written getter or a source the gate cannot read keeps the line counted.
//
// The reports given to one run must come from one target framework. The packages target several, and
// merged reports would count a line covered on one framework as covered on all of them, so the CI
// tests each framework into a directory of its own and gates each directory separately.
//
// Usage:
//   node .github/coverage-gate.mjs [--reports TestResults/coverage] [--min 100]
//
// Exit code 1 below the minimum, and also when no report is found or a report names no line: a gate
// that measured nothing must not pass.

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';
import { parseArgs } from 'node:util';

export function findReports(dir) {
  const out = [];
  const walk = (d) => {
    let entries;
    try { entries = readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of entries.sort((a, b) => a.name.localeCompare(b.name))) {
      const full = join(d, e.name);
      if (e.isDirectory()) walk(full);
      else if (e.name === 'coverage.cobertura.xml') out.push(full);
    }
  };
  walk(dir);
  return out;
}

// Merges reports into Map<"package|file", Map<line, hits>>. `readSource(sources, filename)` returns the
// text of a reported file or undefined; it is read once per class that reports a property accessor.
export function mergeReports(texts, readSource = readReportedSource) {
  const lines = new Map();
  // "package|file|line" -> whether every method reporting that line so far is a synthesized accessor.
  const accessorOnly = new Map();
  for (const xml of texts) {
    const sources = [...xml.matchAll(/<source>([^<]*)<\/source>/g)].map((m) => decode(m[1]));
    for (const pkg of xml.matchAll(/<package\s+[^>]*name="([^"]*)"[^>]*>([\s\S]*?)<\/package>/g)) {
      for (const cls of pkg[2].matchAll(/<class\s+[^>]*filename="([^"]*)"[^>]*>([\s\S]*?)<\/class>/g)) {
        const key = `${pkg[1]}|${cls[1]}`;
        const byLine = lines.get(key) ?? new Map();
        lines.set(key, byLine);
        let source;
        for (const m of cls[2].matchAll(/<method\s+[^>]*name="([^"]*)"[^>]*>([\s\S]*?)<\/method>/g)) {
          const property = /^(?:get|set)_(\w+)$/.exec(m[1])?.[1];
          for (const l of m[2].matchAll(/<line\s+[^>]*number="(\d+)"/g)) {
            const n = Number(l[1]);
            let synthesized = false;
            if (property) {
              source ??= readSource(sources, decode(cls[1])) ?? null;
              synthesized = source !== null && isPositionalRecordParameter(source, n, property);
            }
            const k = `${key}|${n}`;
            accessorOnly.set(k, (accessorOnly.get(k) ?? true) && synthesized);
          }
        }
        // Only the class's own <lines>, not the per-method copies inside <methods>.
        const own = cls[2].replace(/<methods>[\s\S]*?<\/methods>/g, '');
        for (const l of own.matchAll(/<line\s+[^>]*number="(\d+)"[^>]*hits="(\d+)"/g)) {
          const n = Number(l[1]);
          byLine.set(n, Math.max(byLine.get(n) ?? 0, Number(l[2])));
        }
      }
    }
  }
  for (const [key, byLine] of lines) {
    for (const [n, hits] of byLine) {
      if (hits === 0 && accessorOnly.get(`${key}|${n}`) === true) byLine.delete(n);
    }
  }
  return lines;
}

// Whether line `n` (1-based) of `source` declares `property` as a parameter of a record's positional
// parameter list: the name stands there as a parameter, with nothing on the line that would make it a
// hand-written member, and the nearest open parenthesis before it belongs to a record declaration.
export function isPositionalRecordParameter(source, n, property) {
  const all = source.split(/\r?\n/);
  const line = all[n - 1];
  if (line === undefined || /[{}]|=>|\b(?:get|set|init)\b/.test(line)) return false;
  const at = line.search(new RegExp(`\\b${property}\\s*(?:[,)=]|$)`));
  if (at < 0) return false;
  const before = [...all.slice(0, n - 1), line.slice(0, at)]
    .map((l) => l.replace(/\/\/.*$/, ''))
    .join('\n');
  let depth = 0;
  for (let i = before.length - 1; i >= 0; i--) {
    const c = before[i];
    if (c === ')') depth++;
    else if (c === '(') {
      if (depth === 0) return /\brecord(?:\s+(?:struct|class))?\s+\w+(?:<[^()]*>)?\s*$/.test(before.slice(0, i));
      depth--;
    } else if (c === ';' || c === '{' || c === '}') return false;
  }
  return false;
}

function decode(text) {
  return text.replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

function readReportedSource(sources, filename) {
  const candidates = isAbsolute(filename) ? [filename] : sources.map((s) => join(s, filename));
  for (const path of candidates) {
    if (existsSync(path)) return readFileSync(path, 'utf8');
  }
  return undefined;
}

export function summarize(lines) {
  let total = 0;
  let covered = 0;
  const uncovered = [];
  for (const [key, byLine] of [...lines].sort(([a], [b]) => a.localeCompare(b))) {
    for (const [n, hits] of [...byLine].sort(([a], [b]) => a - b)) {
      total++;
      if (hits > 0) covered++;
      else uncovered.push(`${key.split('|')[1]}:${n}`);
    }
  }
  return { total, covered, uncovered, percent: total === 0 ? 0 : (covered / total) * 100 };
}

function main() {
  const { values } = parseArgs({ options: { reports: { type: 'string', default: 'TestResults/coverage' }, min: { type: 'string', default: '100' } } });
  const min = Number(values.min);
  if (!Number.isFinite(min) || min < 0 || min > 100) throw new Error(`--min needs a percentage, got "${values.min}".`);
  const reports = findReports(values.reports);
  if (reports.length === 0) {
    console.error(`::error::No coverage.cobertura.xml under ${values.reports}; run dotnet test with --collect "XPlat Code Coverage" first.`);
    process.exitCode = 1;
    return;
  }
  const s = summarize(mergeReports(reports.map((r) => readFileSync(r, 'utf8'))));
  if (s.total === 0) {
    console.error(`::error::The ${reports.length} coverage reports name no line.`);
    process.exitCode = 1;
    return;
  }
  console.log(`Line coverage: ${s.covered} of ${s.total} lines (${s.percent.toFixed(2)} %) from ${reports.length} reports; minimum ${min} %.`);
  // Compared on the counts, not on a rounded percentage: 99.999 % must not pass a 100 % gate.
  if (s.covered * 100 < min * s.total) {
    for (const u of s.uncovered.slice(0, 200)) console.error(`::error::not covered: ${u}`);
    if (s.uncovered.length > 200) console.error(`... and ${s.uncovered.length - 200} more uncovered lines.`);
    process.exitCode = 1;
  }
}

if (import.meta.main) main();
