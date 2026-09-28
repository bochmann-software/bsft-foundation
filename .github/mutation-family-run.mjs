// Runs one matrix cell: every project of one package family, one Stryker process per project.
//
// One process per project rather than one per family is the whole point of the split. A single
// Stryker process over the solution died with "Out of memory" during project analysis, before it
// tested a mutant; a process per project keeps the peak bounded by the largest single project and
// hands the memory back between projects. Grouping by family is only there to keep the number of
// jobs, and with it the per-job checkout and restore, in proportion to the work.
//
// A project that fails does not stop the cell. Its failure is recorded and the remaining projects
// still run, because a cell that aborts on the first failure would hide every survivor behind it.
// The failure then reaches the issue through family-summary.json, which the aggregator reads.
//
// Usage:
//   node .github/mutation-family-run.mjs --family Auth --out results/Auth [--concurrency 4]
//
// --concurrency is passed to Stryker unchanged. Without it Stryker starts one test runner per logical
// core. On a workstation with many cores that is more vstest.console processes than the machine can
// start within Stryker's 90 s connection window, and the project fails before it tests a mutant; a
// lower value produces the same report, only later. CI runners have few cores and leave it unset.

import { spawnSync } from 'node:child_process';
import { readdirSync, mkdirSync, copyFileSync, writeFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { discover } from './mutation-families.mjs';

function parseArgs(argv) {
  const args = { family: null, out: null, root: process.cwd(), concurrency: null };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--family': args.family = argv[++i]; break;
      case '--out': args.out = argv[++i]; break;
      case '--root': args.root = argv[++i]; break;
      case '--concurrency': args.concurrency = argv[++i]; break;
      default: throw new Error(`Unknown argument: ${argv[i]}`);
    }
  }
  if (!args.family || !args.out) throw new Error('Both --family and --out are required.');
  if (args.concurrency !== null && !/^[1-9][0-9]*$/.test(args.concurrency ?? '')) {
    throw new Error(`--concurrency needs a positive whole number, got "${args.concurrency}".`);
  }
  return args;
}

// Stryker writes StrykerOutput/<timestamp>/reports/mutation-report.json below its working
// directory. The timestamp is the run, so the newest one is this project's report.
function newestReport(dir) {
  const found = [];
  const walk = (current) => {
    let entries;
    try {
      entries = readdirSync(current, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const full = join(current, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.startsWith('mutation-report') && entry.name.endsWith('.json')) found.push(full);
    }
  };
  walk(join(dir, 'StrykerOutput'));
  if (found.length === 0) return null;
  return found.map((f) => ({ f, m: statSync(f).mtimeMs })).sort((a, b) => b.m - a.m)[0].f;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const { projects } = discover(args.root);
  const mine = projects.filter((p) => p.family === args.family);

  if (mine.length === 0) {
    console.error(`No project belongs to family "${args.family}". The matrix and the plan disagree.`);
    process.exit(2);
  }

  mkdirSync(args.out, { recursive: true });
  const results = [];

  for (const project of mine) {
    const workingDirectory = join(args.root, project.testDirectory);

    // Stryker reads stryker-config.json from its working directory, and that directory is the test
    // project, not the repository root where the file lies. Without this the run silently uses
    // Stryker defaults: no ignore-mutations, no ignore-methods, no target framework, no
    // coverage-analysis. It still produces a full report, so nothing looks wrong; the report simply
    // describes a wider surface than the repository has decided on, and its survivor counts cannot be
    // compared with any run that did read the file. --config-file is relative to the working
    // directory, hence the computed path rather than a literal '../../'.
    const configFile = relative(workingDirectory, join(args.root, 'stryker-config.json')).split('\\').join('/');
    console.log(`::group::${project.testProject}`);

    const started = Date.now();
    const run = spawnSync(
      'dotnet',
      ['tool', 'run', 'dotnet-stryker',
        '--config-file', configFile,
        '--project', project.sourceProject,
        '--reporter', 'json', '--reporter', 'progress',
        ...(args.concurrency === null ? [] : ['--concurrency', args.concurrency])],
      { cwd: workingDirectory, stdio: 'inherit', shell: process.platform === 'win32' });
    const seconds = Math.round((Date.now() - started) / 1000);

    const reportPath = newestReport(workingDirectory);
    // The exit code alone is not enough: Stryker exits non-zero on a break threshold as well, and
    // that run produced a perfectly good report. A run counts as done when it left one behind.
    const ok = reportPath !== null;

    if (ok) {
      const destination = join(args.out, project.testProject);
      mkdirSync(destination, { recursive: true });
      copyFileSync(reportPath, join(destination, 'mutation-report.json'));
    }

    results.push({
      testProject: project.testProject,
      status: ok ? 'ok' : 'failed',
      exitCode: run.status,
      seconds,
    });

    console.log(`::endgroup::`);
    console.log(`${project.testProject}: ${ok ? 'ok' : 'FAILED'} (exit ${run.status}, ${seconds}s)`);
  }

  const failed = results.filter((r) => r.status !== 'ok');

  writeFileSync(
    join(args.out, 'family-summary.json'),
    JSON.stringify(
      { family: args.family, status: failed.length === 0 ? 'ok' : 'partial', projects: results },
      null,
      2),
    'utf8');

  if (failed.length > 0) {
    // Red cell, but the summary is already written, so the failed projects are named in the issue
    // and not only in the Actions history.
    console.error(`${failed.length} of ${results.length} projects failed: ${failed.map((r) => r.testProject).join(', ')}`);
    process.exit(1);
  }
}

main();
