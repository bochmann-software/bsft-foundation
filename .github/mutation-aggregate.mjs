// Merges the per-family results of the mutation matrix into the single Stryker report that
// .github/mutation-report.mjs reads, and writes the accounting that says what actually ran.
//
// Two things make this necessary. The matrix exists because one Stryker process over the whole
// solution ran out of memory on the runner, so the run is now split across jobs and produces one
// JSON report per project instead of one per run. And a split run can be partial: a cell can die
// and leave its family unmutated. A survivor list assembled from whatever happened to arrive would
// then look shorter than the week before for the wrong reason, so the missing families are named at
// the top of the issue body rather than left in the Actions history.
//
// Survivor extraction is deliberately NOT repeated here. This script only merges and counts; the
// merged report goes to StrykerOutput/merged/reports/mutation-report.json, where the unchanged
// mutation-report.mjs finds it. There is one implementation of "what counts as a survivor".
//
// Usage:
//   node .github/mutation-aggregate.mjs --input <dir-of-downloaded-artifacts> \
//        --expected <json-plan> --out-report <path> --out-accounting <path>

import { readFileSync, writeFileSync, readdirSync, mkdirSync, appendFileSync } from 'node:fs';
import { join, dirname } from 'node:path';

function parseArgs(argv) {
  const args = { input: null, expected: null, outReport: null, outAccounting: null };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--input': args.input = argv[++i]; break;
      case '--expected': args.expected = argv[++i]; break;
      case '--out-report': args.outReport = argv[++i]; break;
      case '--out-accounting': args.outAccounting = argv[++i]; break;
      default: throw new Error(`Unknown argument: ${argv[i]}`);
    }
  }
  for (const required of ['input', 'expected', 'outReport', 'outAccounting']) {
    if (!args[required]) throw new Error(`Missing --${required.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`);
  }
  return args;
}

// Every file under the artifact directory whose name matches, at any depth. Artifact downloads nest
// one directory per artifact, and Stryker nests one per run, so neither depth is assumed.
function collect(dir, predicate, found = []) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return found;
  }
  for (const entry of entries) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) collect(full, predicate, found);
    else if (predicate(entry.name)) found.push(full);
  }
  return found;
}

// Test ids are only unique within the Stryker run that produced them. Merging several runs without
// namespacing them would let one family's mutant claim another family's tests as its coverage, and
// the "covering tests" column of the issue would quietly point at the wrong suite.
function mergeReport(target, source, scope) {
  const rename = (id) => `${scope}:${id}`;

  for (const [path, entry] of Object.entries(source.testFiles ?? {})) {
    const existing = target.testFiles[path] ?? { tests: [] };
    existing.tests = [
      ...existing.tests,
      ...(entry.tests ?? []).map((test) => ({ ...test, id: rename(test.id) })),
    ];
    target.testFiles[path] = existing;
  }

  for (const [path, entry] of Object.entries(source.files ?? {})) {
    const mutants = (entry.mutants ?? []).map((mutant) => ({
      ...mutant,
      id: rename(mutant.id),
      coveredBy: mutant.coveredBy?.map(rename),
      killedBy: mutant.killedBy?.map(rename),
    }));
    if (target.files[path]) {
      // The same source file mutated by two projects. It happens when a project references another
      // project's sources directly; keeping both sets would double-count, so the first one wins and
      // the collision is reported rather than silently resolved.
      target.collisions.push(path);
      continue;
    }
    target.files[path] = { ...entry, mutants };
  }

  if (!target.schemaVersion && source.schemaVersion) target.schemaVersion = source.schemaVersion;
  if (!target.thresholds && source.thresholds) target.thresholds = source.thresholds;
}

function renderAccounting(plan, families, projectsRan, projectsMissing, collisions) {
  const expected = plan.families ?? [];
  const ranFamilies = [...families.entries()].filter(([, s]) => s.status === 'ok').map(([f]) => f).sort();
  const failedFamilies = [...families.entries()].filter(([, s]) => s.status !== 'ok').map(([f]) => f).sort();
  const absentFamilies = expected.filter((f) => !families.has(f)).sort();

  const complete = failedFamilies.length === 0
    && absentFamilies.length === 0
    && projectsMissing.length === 0;

  // The project census is NOT repeated here. mutation-report.mjs owns it, compares the expectation it
  // is given against the merged report, and is the same file in both repositories. What is added here
  // is the part only this repository has: the matrix cell a missing project belongs to, which is what
  // turns "these projects carry no mutant" into "this job died".
  const lines = [];
  lines.push('');
  lines.push('### Matrix cells');
  lines.push('');
  lines.push(complete
    ? `Every one of the ${expected.length} matrix cells finished.`
    : `${ranFamilies.length} of ${expected.length} matrix cells finished. The census above names the projects this cost.`);
  lines.push('');

  if (failedFamilies.length > 0) {
    lines.push(`Families that ran but did not finish every project: ${failedFamilies.map((f) => `\`${f}\``).join(', ')}.`);
  }
  if (absentFamilies.length > 0) {
    // No result file at all: the job was cancelled, or died before it could write one. That is the
    // out-of-memory shape, so it is named separately from a job that ran and reported a failure.
    lines.push(`Families that produced no result at all (cancelled, or the job died): ${absentFamilies.map((f) => `\`${f}\``).join(', ')}.`);
  }
  if (collisions.length > 0) {
    lines.push(`Source files mutated by more than one project, counted once: ${collisions.length}.`);
  }
  if (plan.unmatched?.length > 0) {
    lines.push(`Test projects with no matching project under \`src/\`, outside the matrix by construction: ${plan.unmatched.map((p) => `\`${p}\``).join(', ')}.`);
  }

  // The renderer below names the whole-solution command, which is what ran out of memory here. The
  // per-project form is the one that works in this repository, so it is stated where it is read.
  lines.push('');
  lines.push('Reproduce one family locally, one project at a time:');
  lines.push('');
  lines.push('```bash');
  lines.push('dotnet tool restore');
  lines.push('node .github/mutation-family-run.mjs --family <Family> --out results/<Family>');
  lines.push('```');
  lines.push('');
  return { text: lines.join('\n'), complete };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const plan = JSON.parse(readFileSync(args.expected, 'utf8'));

  const families = new Map();
  const projectsRan = [];
  const projectsMissing = [];

  for (const summaryPath of collect(args.input, (name) => name === 'family-summary.json')) {
    const summary = JSON.parse(readFileSync(summaryPath, 'utf8'));
    families.set(summary.family, summary);
    for (const project of summary.projects ?? []) {
      (project.status === 'ok' ? projectsRan : projectsMissing).push(project.testProject);
    }
  }

  // A family that ran but never reached a project still owes its projects; the plan is the only
  // place that knows they existed.
  const accountedFor = new Set([...projectsRan, ...projectsMissing]);
  for (const family of plan.families ?? []) {
    if (families.has(family)) continue;
    for (const project of plan.projectsByFamily?.[family] ?? []) {
      if (!accountedFor.has(project)) projectsMissing.push(project);
    }
  }

  const merged = { schemaVersion: null, thresholds: null, files: {}, testFiles: {}, collisions: [] };
  for (const reportPath of collect(args.input, (name) => name.startsWith('mutation-report') && name.endsWith('.json'))) {
    // The artifact path carries the family and project, which is the only scope available here and
    // is unique per project by construction.
    const scope = dirname(reportPath).split(/[\\/]/).slice(-3).join('/');
    mergeReport(merged, JSON.parse(readFileSync(reportPath, 'utf8')), scope);
  }

  const collisions = merged.collisions;
  delete merged.collisions;
  merged.schemaVersion ??= '1';
  merged.thresholds ??= { high: 100, low: 100 };

  mkdirSync(dirname(args.outReport), { recursive: true });
  writeFileSync(args.outReport, JSON.stringify(merged), 'utf8');

  const { text, complete } = renderAccounting(plan, families, projectsRan, projectsMissing, collisions);
  writeFileSync(args.outAccounting, text, 'utf8');
  console.log(text);

  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, `complete=${complete}\nfamilies_ran=${[...families.keys()].length}\nprojects_ran=${projectsRan.length}\n`);
  }
}

main();
