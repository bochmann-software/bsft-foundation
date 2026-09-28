// Proves that the repository is laid out and named the way docs/package-conventions.md,
// docs/naming.md and ADR-0013 say it is, and fails when it is not (Node 24, no network, no
// dependencies).
//
// Usage:
//   node .github/verify-structure.mjs [--root <dir>] [--graph <overview.json>]
//
// Without --graph it checks what the files on disk decide: solution membership, namespace root and
// namespace = folder (independently of IDE0130, and also for files the build excludes), folder
// names, nesting, one-file namespaces, small packages being flat, what DependencyInjection/ holds,
// where the AddBsft*/MapBsft*/UseBsft* entry points live, .Abstractions folders, test projects and
// their mirroring, EFCore satellites, docs/packages.md and one type per file, and the naming rules
// of docs/naming.md that names of projects, folders, types, parameters and written-out contracts
// (endpoint names, routes, table names, bound keys, log properties, contract constants) decide (the
// name-* rules, in .github/structure/naming.mjs). With --graph, the
// export of `codecharter graph overview Bsft.sln --top 100000 --format json`, it also checks what
// only the dependency graph decides: the root is a sink, Diagnostics is a sink, nothing depends on
// DependencyInjection, and the namespaces form no cycle.
//
// The tier rules read each package's <Version> (Foundation or framework, PC 1 "Tiers: Foundation and
// framework") and, when the file exists, Bsft.Foundation.sln, the manifest the public repository is
// exported from.
//
// Each violation names the rule and the sentence of the conventions it enforces; the rules and
// their citations are in .github/structure/checks.mjs. Exit code 1 on any violation.

import { readFileSync, existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { readRepository, readSolution } from './structure/model.mjs';
import { runChecks, readGraph, RULES } from './structure/checks.mjs';

const { values } = parseArgs({
  options: {
    root: { type: 'string', default: '.' },
    graph: { type: 'string' },
  },
});

const root = resolve(values.root);
const repo = readRepository(root);
if (repo.projects.length === 0) {
  console.error(`::error::${root}: no project under src/, samples/ or tests/. Refusing to call an empty tree conforming.`);
  process.exit(1);
}

const packagesPath = join(root, 'docs', 'packages.md');
const { violations, skipped } = runChecks(repo, {
  solution: readSolution(root, 'Bsft.sln'),
  foundationSolution: readSolution(root, 'Bsft.Foundation.sln'),
  packagesMd: existsSync(packagesPath) ? readFileSync(packagesPath, 'utf8') : undefined,
  graph: values.graph ? readGraph(values.graph) : undefined,
});

const files = repo.projects.reduce((n, p) => n + p.files.length, 0) + repo.looseFiles.length;
console.log(`Read ${repo.projects.length} projects and ${files} C# files${values.graph ? ' and the namespace graph' : ''}.`);
for (const s of skipped) console.log(`Not checked: ${s}.`);

if (violations.length > 0) {
  const byRule = new Map();
  for (const v of violations) {
    console.error(`::error::[${v.rule}] ${v.where}: ${v.message}`);
    byRule.set(v.rule, (byRule.get(v.rule) ?? 0) + 1);
  }
  console.error('');
  for (const [rule, count] of byRule) console.error(`${rule}: ${count} violation(s). ${RULES[rule].cite}: "${RULES[rule].text}"`);
  console.error(`${violations.length} violation(s).`);
  process.exitCode = 1;
} else {
  console.log(`No violation of ${Object.keys(RULES).length} structure rules.`);
}
