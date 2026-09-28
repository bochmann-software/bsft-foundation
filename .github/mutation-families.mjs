// Derives the mutation matrix from the repository rather than from a list somebody maintains by hand.
//
// A family is the first segment of a package name after the product prefix: Bsft.Auth.Idp.EFCore and
// Bsft.Auth.TokenServer are both "Auth". One matrix cell runs one family, and inside a cell Stryker is
// started once per project, because a single Stryker process over a whole solution died of memory
// exhaustion on a hosted runner before it tested one mutant.
//
// The same output feeds the matrix and the final report, so a family cannot be mutated without being
// expected, and cannot be expected without being mutated. That is the point of deriving it here: a cell
// that dies leaves a family in the expected list with no result, which the report then names.
//
// Usage:
//   node .github/mutation-families.mjs                 # {"families":[...],"projects":N}
//   node .github/mutation-families.mjs --family Auth   # the projects of one family
//
// Conventions assumed, the same two the other mutation scripts assume: test projects end in ".Tests"
// and live under tests/, and the project a test project covers has the same name without that suffix
// and lives under src/. A test project with no such source project is reported as unmatched rather
// than silently dropped.

import { readdirSync, existsSync, appendFileSync } from 'node:fs';
import { join } from 'node:path';

const TEST_SUFFIX = '.Tests';

function parseArgs(argv) {
  const args = { root: process.cwd(), family: null };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--root': args.root = argv[++i]; break;
      case '--family': args.family = argv[++i]; break;
      default: throw new Error(`Unknown argument: ${argv[i]}`);
    }
  }
  return args;
}

// "Bsft.Auth.Idp.EFCore.Tests" -> "Auth". A package with no segment after the product prefix, such as
// Bsft.Core, is its own family under its own name.
function familyOf(sourceName) {
  const segments = sourceName.split('.');
  return segments.length > 1 ? segments[1] : segments[0];
}

export function discover(root) {
  const testsDir = join(root, 'tests');
  let entries;
  try {
    entries = readdirSync(testsDir, { withFileTypes: true });
  } catch {
    return { projects: [], unmatched: [] };
  }

  const projects = [];
  const unmatched = [];

  for (const entry of entries) {
    if (!entry.isDirectory() || !entry.name.endsWith(TEST_SUFFIX)) continue;

    const testProject = entry.name;
    const sourceName = testProject.slice(0, -TEST_SUFFIX.length);
    const sourceProject = join('src', sourceName, `${sourceName}.csproj`);

    if (!existsSync(join(root, sourceProject))) {
      // A test project covering something outside src/ — a sample, or a suite with no single owner.
      // Named rather than dropped, so the report can say why a project carries no mutants.
      unmatched.push(testProject);
      continue;
    }

    projects.push({
      family: familyOf(sourceName),
      testProject,
      // The name of the project being mutated, which is the unit the census in mutation-report.mjs
      // compares in. It is the one identifier both repositories can produce, here from the layout and
      // in the sibling product repository from the solution, and the one the report can read back off a mutated file's
      // path. Test project names are this repository's business and stay out of the shared comparison.
      sourceName,
      testDirectory: join('tests', testProject).split('\\').join('/'),
      sourceProject: `${sourceName}.csproj`,
    });
  }

  projects.sort((a, b) => a.family.localeCompare(b.family) || a.testProject.localeCompare(b.testProject));
  unmatched.sort();

  return { projects, unmatched };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const { projects, unmatched } = discover(args.root);

  if (args.family) {
    const mine = projects.filter((p) => p.family === args.family);
    if (mine.length === 0) {
      console.error(`No project belongs to family "${args.family}".`);
      process.exit(2);
    }
    console.log(JSON.stringify(mine));
    return;
  }

  const families = [...new Set(projects.map((p) => p.family))].sort();

  // The plan carries the membership, not only the family names, so the aggregator can name the
  // projects of a family whose job died before it wrote anything. Without it a dead cell could only
  // be reported as a missing family, and the project count in the issue would be a guess.
  const projectsByFamily = {};
  for (const project of projects) {
    (projectsByFamily[project.family] ??= []).push(project.testProject);
  }

  // The expectation the census compares against. It is procured here, from the repository, and handed
  // to mutation-report.mjs as an input; that script no longer fetches an expectation of its own from
  // anywhere. Procurement differs between repositories, the comparison does not, so only procurement
  // lives in a file like this one.
  const expected = projects.map((p) => p.sourceName).sort();

  const plan = { families, projects: projects.length, projectsByFamily, expected, unmatched };

  console.log(JSON.stringify(plan));

  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(
      process.env.GITHUB_OUTPUT,
      `families=${JSON.stringify(families)}\nprojects=${projects.length}\nplan=${JSON.stringify(plan)}\n`);
  }
}

// Importable for the runner and the aggregator; only the direct invocation prints.
if (process.argv[1] && process.argv[1].endsWith('mutation-families.mjs')) main();
