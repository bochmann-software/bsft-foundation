// Turns the Stryker JSON report into the two things a run actually has to produce: a survivor
// table in the job log and in the job summary, and — for the nightly run — a body file for the
// GitHub issue that .github/mutation-issue.mjs then creates, comments on or closes.
//
// Nothing here uploads an artifact. The report is read where Stryker wrote it and thrown away
// with the workspace. The local command that reproduces the full HTML report belongs to the
// repository, not to this file, and reaches the body through --appendix.
//
// It also carries the census: which projects the run was supposed to cover, and which of them carry no
// mutant in the report. The expectation is an INPUT and is never fetched here. Which projects a run
// should have covered is a property of that run, not of a configuration file, and how it is procured is
// the one thing that differs between the repositories sharing this script: one derives it from the
// repository layout, the other from the solution. The comparison is identical in both, so only the
// comparison lives here. A missing or empty expectation is a failure of the run, never an empty success.
//
// Usage:
//   node .github/mutation-report.mjs --mode nightly [--root .] --expected <path> --body-file <path> [--appendix <path>] [--justifications <path>]
//   node .github/mutation-report.mjs --mode pr      [--root .] [--fail]
//
// The "pr" mode is kept because it is the mode a per-project gate would use; it is not wired into
// any workflow in either repository sharing this file, and each one documents for itself why there
// is no mutation gate per pull request.

import { readFileSync, writeFileSync, appendFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep, isAbsolute } from 'node:path';

// A mutant that no test kills. Stryker separates the two cases and both mean the same thing for
// this gate: changing the production code there does not fail a test. "NoCoverage" is kept as its
// own status in the table because it points at a different fix (write a test at all, rather than
// sharpen an existing assertion).
const SURVIVING_STATUSES = new Set(['Survived', 'NoCoverage']);

// The GitHub issue body caps at 65536 characters. The table is cut well before that so the total
// count, the header and the appendix always survive the truncation.
const ISSUE_BODY_LIMIT = 60000;
const MAX_ROWS = { pr: 200, nightly: 100 };

// The members emptied by safe mode are listed as well, but they are a diagnostic and must not push
// the survivors out of the body, so their table is shorter.
const MAX_BLIND_ROWS = 40;

function parseArgs(argv) {
  const args = {
    mode: 'pr', root: process.cwd(), fail: false,
    bodyFile: null, expected: null, appendix: null, justifications: null,
  };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--mode': args.mode = argv[++i]; break;
      case '--root': args.root = argv[++i]; break;
      case '--body-file': args.bodyFile = argv[++i]; break;
      case '--expected': args.expected = argv[++i]; break;
      case '--appendix': args.appendix = argv[++i]; break;
      case '--justifications': args.justifications = argv[++i]; break;
      case '--fail': args.fail = true; break;
      default: throw new Error(`Unknown argument: ${argv[i]}`);
    }
  }
  if (args.mode !== 'pr' && args.mode !== 'nightly') {
    throw new Error(`--mode must be "pr" or "nightly", got "${args.mode}"`);
  }
  // Mandatory for the run that reports on everything, because that is the run whose survivor count is
  // read as a total. A pull request run is scoped to a diff by construction and has nothing to census.
  if (args.mode === 'nightly' && !args.expected) {
    throw new Error('--expected is required in nightly mode: the census compares, it does not fetch.');
  }
  return args;
}

// The expectation as procured by whatever this repository uses: either a bare array of project names,
// or an object carrying one under "expected". Anything else, or an empty list, is refused rather than
// treated as "nothing was expected", which would make any run at all look complete.
function readExpectation(path) {
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(path, 'utf8'));
  } catch (error) {
    throw new Error(`The expectation at ${path} could not be read: ${error.message}`);
  }

  const names = Array.isArray(parsed) ? parsed : parsed?.expected;

  if (!Array.isArray(names) || names.length === 0) {
    throw new Error(
      `The expectation at ${path} names no project. An empty expectation would make any run look complete.`);
  }

  return [...new Set(names.map(String))].sort();
}

// Stryker writes into StrykerOutput/<timestamp>/reports/. The timestamp is the run, so the newest
// file is this run's report; searching for it beats hard-coding a path that changes every run.
function findReport(root) {
  const found = [];
  const walk = (dir) => {
    let entries;
    try {
      entries = readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.json') && entry.name.startsWith('mutation-report')) found.push(full);
    }
  };
  walk(join(root, 'StrykerOutput'));
  if (found.length === 0) return null;
  return found.map((f) => ({ f, m: statSync(f).mtimeMs })).sort((a, b) => b.m - a.m)[0].f;
}

// The report carries the file's source text, so the original of a mutant is the span its location
// points at. Without it the table would state what the code was changed into but not from what.
function originalText(source, location) {
  if (typeof source !== 'string') return '';
  const lines = source.split(/\r\n|\r|\n/);
  const { start, end } = location ?? {};
  if (!start || !end || start.line < 1 || end.line > lines.length) return '';
  if (start.line === end.line) {
    return lines[start.line - 1].slice(start.column - 1, end.column - 1);
  }
  const head = lines[start.line - 1].slice(start.column - 1);
  const tail = lines[end.line - 1].slice(0, end.column - 1);
  return [head, ...lines.slice(start.line, end.line - 1), tail].join(' ');
}

// One line: collapsed to a single line and length-capped, with nothing escaped. Outside a table a
// pipe is an ordinary character, and escaping it there would put backslashes into prose.
function flatten(text, limit = 60) {
  const flat = String(text ?? '').replace(/\s+/g, ' ').trim();
  return flat.length > limit ? `${flat.slice(0, limit - 1)}…` : flat;
}

// One line of a markdown table cell: flattened, with pipes escaped so a mutated expression cannot
// break the table it is reported in, and backticks flattened for the same reason.
function cell(text, limit = 60) {
  return flatten(text, limit).replace(/\|/g, '\\|').replace(/`/g, "'");
}

// The report states absolute paths. A table of absolute paths is unreadable and differs between
// the runner and a developer's machine, so they are cut back to the repository-relative form. The
// fallback keeps a run started from inside a project directory readable as well.
function toRepoPath(root, filePath) {
  const rel = relative(root, filePath);
  if (rel && !rel.startsWith('..') && !isAbsolute(rel)) return rel.split(sep).join('/');
  const normalised = filePath.split('\\').join('/');
  const match = /\/((?:src|tests|samples)\/.*)$/.exec(normalised);
  return match ? match[1] : normalised;
}

// Which test project covers the mutant. The report links a mutant to test ids and the test ids to
// their files, so the project is the folder the covering test file sits in. It answers the first
// question a survivor raises: which suite was supposed to have caught this.
function coveringProjects(mutant, testFileIndex) {
  const ids = [...(mutant.coveredBy ?? []), ...(mutant.killedBy ?? [])];
  const projects = new Set();
  for (const id of ids) {
    const project = testFileIndex.get(id);
    if (project) projects.add(project);
  }
  return [...projects].sort().join(', ');
}

function buildTestFileIndex(report, root) {
  const index = new Map();
  for (const [path, entry] of Object.entries(report.testFiles ?? {})) {
    const repoPath = toRepoPath(root, path);
    // tests/<Project>/... or samples/<Project>/...; fall back to the file itself.
    const segments = repoPath.split('/');
    const project = segments.length > 1 ? segments[segments.length - 2] : repoPath;
    const named = segments.find((s) => s.endsWith('.Tests')) ?? project;
    for (const test of entry.tests ?? []) index.set(test.id, named);
  }
  return index;
}

function collectSurvivors(report, root) {
  const testFileIndex = buildTestFileIndex(report, root);
  const survivors = [];
  let total = 0;
  for (const [path, entry] of Object.entries(report.files ?? {})) {
    for (const mutant of entry.mutants ?? []) {
      // Ignored and CompileError mutants are not results; counting them would inflate the total
      // the issue states and would put unkillable noise in front of the reader.
      if (mutant.status === 'Ignored' || mutant.status === 'CompileError') continue;
      total++;
      if (!SURVIVING_STATUSES.has(mutant.status)) continue;
      survivors.push({
        file: toRepoPath(root, path),
        line: mutant.location?.start?.line ?? 0,
        // Not shown in the survivor table, where the line number already locates the mutant. It is
        // carried because it is part of the key an equivalence justification is written in, which
        // has to hold still while lines around it move.
        member: memberAt(entry.source, mutant.location?.start?.line ?? 0),
        mutator: mutant.mutatorName ?? '',
        original: originalText(entry.source, mutant.location),
        mutated: mutant.replacement ?? '',
        status: mutant.status,
        tests: coveringProjects(mutant, testFileIndex),
      });
    }
  }
  survivors.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line);
  return { survivors, total };
}

function renderTable(survivors, maxRows) {
  const shown = survivors.slice(0, maxRows);
  const head = [
    '| File | Line | Mutator | Original | Mutated | Status | Covering tests |',
    '| --- | ---: | --- | --- | --- | --- | --- |',
  ];
  const rows = shown.map((s) =>
    `| ${cell(s.file, 70)} | ${s.line} | ${cell(s.mutator, 28)} | ${cell(s.original, 40)} | ${cell(s.mutated, 40)} | ${s.status} | ${cell(s.tests, 40)} |`);
  const note = survivors.length > shown.length
    ? [`\n${survivors.length - shown.length} further surviving mutants are not listed; the run's own report has all of them.`]
    : [];
  return [...head, ...rows, ...note].join('\n');
}

// Which project a survivor belongs to, read off the repository layout (src/<Project>/..., and the
// same for tests and samples). It is only used for the summary table, so a path that does not fit
// the layout degrades to its first segment rather than breaking the report.
function projectOf(file) {
  const segments = file.split('/');
  return segments.length > 1 ? `${segments[0]}/${segments[1]}` : file;
}

// The project name alone, which is the unit the expectation is written in. projectOf keeps the leading
// src/ because the survivor table reads better with it; the census compares names.
function projectNameOf(file) {
  const segments = file.split('/');
  return segments.length > 1 ? segments[1] : file;
}

// Which of the expected projects carry no mutant in this report. A project can be absent because its
// job died, because it was never started, or because it genuinely produced nothing; the report cannot
// tell those apart and does not pretend to. It states the fact, which is what stops a shorter survivor
// list from reading as progress.
function census(report, root, expected) {
  const covered = new Set(Object.keys(report.files ?? {}).map((path) => projectNameOf(toRepoPath(root, path))));
  const missing = expected.filter((name) => !covered.has(name));

  return { expected: expected.length, mutated: expected.length - missing.length, missing };
}

function renderCensus({ expected, mutated, missing }) {
  if (missing.length === 0) {
    return `Coverage of the run: all ${expected} expected projects were mutated.`;
  }

  const named = missing.slice(0, 40).map((name) => `\`${name}\``).join(', ');
  const rest = missing.length > 40 ? `, and ${missing.length - 40} more` : '';

  return [
    `**This run is incomplete.** ${mutated} of ${expected} expected projects were mutated, so the survivor list below is a floor, not a total.`,
    '',
    `Projects with no mutant in this report: ${named}${rest}.`,
  ].join('\n');
}

// 800 survivors in one table say nothing. The per-project counts say where the work is, and they
// are what makes a gate decision per package possible instead of one number for the repository.
function renderProjectSummary(survivors) {
  const counts = new Map();
  for (const s of survivors) counts.set(projectOf(s.file), (counts.get(projectOf(s.file)) ?? 0) + 1);
  const rows = [...counts.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([project, count]) => `| ${cell(project, 70)} | ${count} |`);
  return ['| Project | Survivors |', '| --- | ---: |', ...rows].join('\n');
}

// Stryker books a timeout as a kill. A mutant abandoned for exceeding the time limit is counted
// among the caught although nothing told it from its original, so the number belongs in the
// bookkeeping twice over.
//
// It says the survivor list is a floor by at least that many, the same thing the census says about
// a missing project. And it says under what conditions the run measured, which is the part that is
// easy to miss: a timeout is what a mutant produces when the machine is busy, so a run with none was
// not competing for the machine, and a count that has risen against an earlier run of the same
// projects was. The consequence runs the wrong way round. A loaded machine does not depress the
// score, it inflates it, because every abandoned mutant is booked as caught. A threshold on the
// score would therefore be easiest to pass on the worst-measured run, which is why this repository
// gates on the survivor list and this bookkeeping instead, and why the number is stated here rather
// than left in console output the run discards.
function timeouts(report, root) {
  const perProject = new Map();
  let total = 0;
  for (const [path, entry] of Object.entries(report.files ?? {})) {
    const project = projectOf(toRepoPath(root, path));
    for (const mutant of entry.mutants ?? []) {
      if (mutant.status !== 'Timeout') continue;
      total++;
      perProject.set(project, (perProject.get(project) ?? 0) + 1);
    }
  }

  return { total, perProject };
}

function renderTimeouts({ total, perProject }) {
  if (total === 0) {
    return 'Timeouts: none. Every mutant was resolved by a test rather than abandoned, so nothing below '
      + 'was counted as caught without a test telling it from its original, and the run had the machine '
      + 'to itself.';
  }

  const rows = [...perProject.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([project, count]) => `| ${cell(project, 70)} | ${count} |`);

  return [
    `**${total} mutants timed out, and a timeout counts as a kill.** Each was abandoned for exceeding the`,
    'time limit rather than told apart by a test, so the survivor list below is short by at least that many',
    'and the score is correspondingly flattered. Hold the number against the previous run of the same',
    'projects: a rise means this run was competing for the machine, and a run under load reports a better',
    'score than the same suite would report on a quiet one.',
    '',
    '| Project | Mutants abandoned as timeouts |',
    '| --- | ---: |',
    ...rows,
  ].join('\n');
}

// A declaration line, as opposed to a line that merely calls something: every member in this
// repository carries an explicit accessibility or another modifier, so requiring one separates
// `private void Send(...)` from a `Send(transport, frame);` further down the same method.
const DECLARATION =
  /^\s*(?:(?:public|private|protected|internal|file|static|async|sealed|override|virtual|abstract|partial|extern|unsafe|new|required|readonly|const|implicit|explicit|operator)\s+)+/;
const TYPE = /^\s*(?:[\w[\]?]+\s+)*(?:class|record|struct|interface|enum)\s+([A-Za-z_]\w*)/;

// The declared name: whatever stands immediately in front of the parameter list, or, for a
// property or a field, the last name in front of its body. Generic arguments are dropped first so
// that `Task<int> ReadAsync(` does not answer with the type argument.
// The declared name is the last identifier before whatever ends the declarator: a parameter list, a
// body, an arrow, an initialiser or the end of the line. Finding it needs one pass with a depth count
// rather than a search for the first "(", because a "(" is not always a parameter list. A method that
// returns a tuple wears one in its return type, and a property initialised with an object creation
// wears one after "= new". Reading the first one in either case takes the last identifier before it,
// which is a modifier or the word "new" — a name that is not wrong so much as not a name at all. It
// still keys consistently, since the index and the lookup make the same mistake, so nothing breaks
// loudly; it merely spends the most discriminating part of the key on a word every such member shares
// and writes a justification that names "new" where a reader wanted the property.
function declaredName(code) {
  let head = code.replace(/<[^<>]*>/g, ' ');
  let depth = 0;
  let lastCall = -1;
  let ends = -1;
  for (let i = 0; i < head.length && ends < 0; i++) {
    const c = head[i];
    if (c === '(' || c === '[') {
      // A square bracket at depth zero is a parameter list only on an indexer, where 'this' precedes
      // it; on an array type it is part of the type and naming it would take the type for the member.
      if (depth === 0 && (c === '(' || /\bthis\s*$/.test(head.slice(0, i)))) lastCall = i;
      depth++;
    } else if (c === ')' || c === ']') {
      depth--;
    } else if (depth === 0) {
      // Only at depth zero: a default argument's "=" and a lambda inside a parameter list belong to
      // the declarator, not after it.
      if (c === '=' && head[i + 1] === '>') ends = i;
      else if (c === '=' && head[i + 1] !== '=' && !'=!<>+-*/%&|^'.includes(head[i - 1])) ends = i;
      else if (c === '{' || c === ';') ends = i;
    }
  }
  // The scan stops at the terminator, so a parameter list it found is always before it.
  head = head.slice(0, lastCall >= 0 ? lastCall : ends >= 0 ? ends : head.length);
  const names = head.match(/[A-Za-z_]\w*/g);
  return names ? names[names.length - 1] : '';
}

// Which member a mutant sits in, read off the source the report carries. Stryker's JSON states the
// position of a mutant but not the member that holds it, and the member is what a reader needs
// here: safe mode empties a whole member, so "which member has no mutation signal" is the
// question. The nearest declaration above the mutant answers it; a position that matches nothing
// degrades to the enclosing type, and then to an empty name, rather than guessing.
function memberAt(source, line) {
  if (typeof source !== 'string') return '';
  const lines = source.split(/\r\n|\r|\n/);
  for (let i = Math.min(line, lines.length) - 1; i >= 0; i--) {
    const code = lines[i].replace(/\/\/.*$/, '').trim();
    if (code === '' || code.startsWith('*') || code.startsWith('/*') || code.startsWith('[')) continue;
    if (!DECLARATION.test(lines[i])) continue;
    const type = TYPE.exec(lines[i]);
    if (type) return type[1];
    const name = declaredName(code);
    if (name) return name;
  }
  return '';
}

// Mutants Stryker could not compile. They are not survivors and not kills: they were never run.
// Stryker's safe mode reacts to one of them by removing every mutation in the member that held it,
// so such a member carries no mutation signal at all and looks, in a survivor table, exactly like
// a member whose mutants were all killed. CS0165 (definite assignment, where a mutated condition
// leaves a local unassigned) is the recurring cause and a known limitation of the mutator, not
// something the production code is changed for. What the report owes the reader is the list, so
// that a project without survivors is not read as a project that was tested.
function collectBlindSpots(report, root) {
  const members = new Map();
  let total = 0;
  for (const [path, entry] of Object.entries(report.files ?? {})) {
    const file = toRepoPath(root, path);
    const names = new Map();
    for (const mutant of entry.mutants ?? []) {
      if (mutant.status === 'Ignored') continue;
      const line = mutant.location?.start?.line ?? 0;
      if (!names.has(line)) names.set(line, memberAt(entry.source, line));
      const member = names.get(line);
      const key = `${file}\0${member}`;
      const found = members.get(key)
        ?? { file, member, project: projectOf(file), first: line, last: line, failed: 0, tested: 0 };
      if (mutant.status === 'CompileError') {
        total++;
        found.failed++;
        found.first = Math.min(found.first, line);
        found.last = Math.max(found.last, line);
      } else {
        found.tested++;
      }

      members.set(key, found);
    }
  }
  // A member whose mutants all failed to compile carries no mutation signal at all. One that kept
  // some is reported too, because part of its signal is gone, but the two must not read alike.
  const blind = [...members.values()]
    .filter((member) => member.failed > 0)
    .sort((a, b) => a.tested - b.tested || b.failed - a.failed || a.file.localeCompare(b.file));
  return { blind, total, silent: blind.filter((member) => member.tested === 0).length };
}

function renderBlindSpots({ blind, total, silent }, tested) {
  if (total === 0) {
    return 'Every mutant compiled, so no member lost its mutation signal.';
  }

  const perProject = new Map();
  for (const entry of blind) {
    const counts = perProject.get(entry.project) ?? { failed: 0, silent: 0 };
    counts.failed += entry.failed;
    counts.silent += entry.tested === 0 ? 1 : 0;
    perProject.set(entry.project, counts);
  }

  const projects = [...perProject.entries()]
    .sort((a, b) => b[1].silent - a[1].silent || b[1].failed - a[1].failed || a[0].localeCompare(b[0]))
    .map(([project, counts]) => `| ${cell(project, 70)} | ${counts.failed} | ${counts.silent} |`);

  const shown = blind.slice(0, MAX_BLIND_ROWS);
  const rows = shown.map((entry) =>
    `| ${cell(entry.file, 70)} | ${entry.first === entry.last ? entry.first : `${entry.first}-${entry.last}`} | ${cell(entry.member, 40)} | ${entry.failed} | ${entry.tested} |`);
  const note = blind.length > shown.length
    ? [`\n${blind.length - shown.length} further members lost part of their mutants; the run's own report has all of them.`]
    : [];

  return [
    '### Members without a mutation signal',
    '',
    `${total} mutants did not compile, next to the ${tested} that were tested, and ${silent} members were left without a`,
    "single tested mutant. Stryker's safe mode reacts to a mutant that does not compile by removing every mutation in the",
    'member that held it, so such a member is not mutation tested at all and an empty survivor table says nothing about',
    'it. The recurring cause is CS0165, definite assignment, where a mutated condition leaves a local unassigned; that is',
    'a limitation of the mutator and not a defect in the code under test. The member names are read off the source at the',
    'mutant\'s position, so they name the nearest declaration above it.',
    '',
    '| Project | Mutants that did not compile | Members left silent |',
    '| --- | ---: | ---: |',
    ...projects,
    '',
    '| File | Lines | Member | Did not compile | Still tested |',
    '| --- | --- | --- | ---: | ---: |',
    ...rows,
    ...note,
  ].join('\n');
}

// The standard for anything built here later, written down because it has now been learned three
// times. The key started without the replacement text, so one entry matched both mutants a single
// mutator made out of one span. Then a surviving match outvoted a killed one, so an entry stayed
// standing on the strength of the mutant it was not about. Then a twin span in the same member let a
// justification absorb a kill. None of those three is a slip in the implementation. They all follow
// from the same original decision: the lookup was built to find a match rather than to demand a
// unique one, and a lookup satisfied by the first thing it finds will always resolve doubt in favour
// of whoever wrote the entry.
//
// So the rule for every future change to this mechanism: ambiguity is refused by name, never assumed
// away. If a change makes a justification match more loosely, that property is what is being given
// up, and it is the one this whole report exists to protect.

// A mutant that no possible value can tell apart from its original is not a missing test and not
// dead code, and it must not be filtered away. It is justified: a sentence saying why the two
// cannot differ, written against the range of values rather than against the test suite, exactly as
// a deletion is argued. The justification stays in the report, because it is a decision somebody
// made and decisions have to remain visible; an ignore list would turn it into a permanent invisible
// rule, which is the failure mode this whole report exists to avoid.
//
// The key is chosen for one property: a justification must survive an unrelated change in the file
// it lives in, and must break the moment the expression it is about changes. That is what makes the
// claim perishable instead of eternal. A line number fails the first half, a file name fails the
// second, so the key is the file, the member, the mutator, the mutated span's own source text and
// the text that replaced it. Whitespace inside both is collapsed before comparing: reflowing a long
// expression across lines changes no value it can take, and a justification is about meaning rather
// than layout. The parts are joined on a NUL, which is the one byte none of them can contain, so no
// run of parts can spell out a different key by accident.
//
// The replacement earns its place in the key rather than sitting there for symmetry. One mutator
// routinely makes several mutants out of one span: an equality operator yields both the loosened and
// the reversed form of the same comparison, and those are two different claims about the code. They
// can land on opposite sides, one killed by a boundary test and the other unreachable by any value a
// single-threaded test can produce, and a key without the replacement matches both at once. The
// entry is then refuted by the mutant it was never about, and the run reports a stale justification
// when what is actually stale is the key's ability to say which mutant is meant.
// What the key still cannot do, and why there is a "line" field rather than a cleverer key. Two
// mutants in one member can be identical in every part of the key: the same mutator turning the same
// source text into the same replacement, twice, at two different places. An early return is the
// ordinary shape of it — "return;" mutated to ";" in a method with two guards is one span written
// twice — and no amount of text in the key separates them, because the text is the same text.
//
// The failure that produces is the quiet kind. One key matches both mutants, so a justification
// written about the first is applied to the second as well, and if a test later kills the second the
// entry is not refuted: a surviving sibling keeps it alive. A justification would then be absorbing a
// kill, which is precisely the direction this report exists to catch.
//
// So ambiguity is refused rather than resolved. A key that matches mutants on more than one line is
// reported as broken and justifies nothing until the entry adds "line" to name the one it means. The
// line is not part of the key and never takes part in matching by itself; it only picks candidates out
// of several. That keeps the perishability intact from both ends: change the expression and the key
// stops matching, move the code and the line stops matching, and either way the claim has to be made
// again.
//
// A line does not resolve everything, and the remaining case is left deliberately unresolved rather
// than given a weaker handle. One mutator can make several mutants out of one span on one line, and
// they are then identical in the key, identical on the line, and identical to a reader: there is no
// sentence that is true of one and false of another. Justifying one of them and not the rest would be
// a distinction nobody can act on. So when the candidates share a line the entry covers all of them,
// which is one claim about one span, and the count of justified mutants may legitimately exceed the
// count of entries.
//
// That concession has one hard edge, and it is the edge the whole guard exists for: if any of those
// candidates was killed, the entry is refused outright. It cannot be about all of them, because a test
// tells one of them apart, and it cannot pick one, because nothing distinguishes them. An entry in that
// position is not a justification any more, whatever it once was.

// One standard about checking this mechanism, from the same family and learned the same way. Three
// audits of it came back clean while what they were about was broken, and each had measured the right
// function with the wrong question. One asked whether the member resolution lands on a statement or on
// nothing; it lands on neither, and the claim was that it lands on the wrong name, which it did. One
// compared keys without the member the real key carries, and so reported a collision the mechanism does
// not have. A checker coarser than the thing checked reports clean and means nothing by it. So: a check
// is written against the claim, not against the mechanism's general health, and it runs the code under
// test itself rather than a copy of it that drifts in the checker's favour.
function mutantKey(file, member, mutator, original, mutated) {
  const flat = (text) => String(text ?? '').replace(/\s+/g, ' ').trim();
  return [file, member, mutator, flat(original), flat(mutated)].join('\0');
}

// The two things a justified survivor can be. They are counted, rendered and worded separately, and
// the reason is what happens to the code a year from now rather than anything about this run.
//
// "equivalent" means no possible value tells the mutant from the code it replaced. The two are the
// same program, so no test can kill it and none is owed — and the check the mutant touched is then
// fairly asked whether it needs to exist at all.
//
// "unobservable" is the opposite case wearing the same clothes. The mutant behaves differently, often
// dramatically so, and the difference is simply not of a kind any test can assert on: a removed bounds
// check does not produce a wrong answer a test could read back, it produces undefined behaviour over
// memory that is not ours. There the check is the defence, and the one thing that must never happen to
// it is removal.
//
// Under one label those two invert each other. Somebody tidying the justified list in a year would
// read "nothing can tell the difference" over a memory guard and take the guard out. So they are two
// labels, the kind is mandatory on every entry, and there is no default: a justification that has not
// said which of the two it is has not been thought through, and guessing for the author is exactly the
// mistake this separation exists to prevent.
const KINDS = new Set(['equivalent', 'unobservable']);

// The justifications this repository holds. Repository-specific by nature, so it is an input and not
// a constant here, and like the appendix it is optional but fails when it is named and unreadable.
// A malformed entry fails too: a justification missing the expression it is about cannot be checked
// against anything, and an unfalsifiable justification is worse than none.
function readJustifications(path) {
  if (!path) return [];

  let parsed;
  try {
    parsed = JSON.parse(readFileSync(path, 'utf8'));
  } catch (error) {
    throw new Error(`The survivor justifications at ${path} could not be read: ${error.message}`);
  }

  const entries = Array.isArray(parsed) ? parsed : parsed?.justifications;
  if (!Array.isArray(entries)) {
    throw new Error(`The survivor justifications at ${path} must be an array, or an object carrying one under "justifications".`);
  }

  for (const [index, entry] of entries.entries()) {
    for (const field of ['file', 'member', 'mutator', 'original', 'mutated', 'because']) {
      if (typeof entry?.[field] !== 'string' || entry[field].trim() === '') {
        throw new Error(`Survivor justification ${index + 1} in ${path} is missing "${field}".`);
      }
    }
    if (entry.line !== undefined && (!Number.isInteger(entry.line) || entry.line < 1)) {
      throw new Error(
        `Survivor justification ${index + 1} in ${path} has a "line" of ${JSON.stringify(entry.line)}, `
        + 'which is not a line number. It is only ever needed to pick one of several identical spans.');
    }
    if (!KINDS.has(entry.kind)) {
      throw new Error(
        `Survivor justification ${index + 1} in ${path} must declare "kind" as "equivalent" (no possible value `
        + 'tells the mutant apart) or "unobservable" (it behaves differently and no test can assert on the '
        + `difference), got ${JSON.stringify(entry?.kind)}.`);
    }
  }

  return entries;
}

// Every mutant the run actually tested, indexed by the same key the justifications are written in.
// Killed mutants are indexed as well, and deliberately: an entry that matches only kills is not
// stale but refuted, which is the more interesting failure of the two. Ignored and CompileError
// mutants are left out for the same reason they are left out of the total — they were never run, so
// they can neither confirm nor refute anything.
function indexTestedMutants(report, root) {
  const index = new Map();
  for (const [path, entry] of Object.entries(report.files ?? {})) {
    const file = toRepoPath(root, path);
    for (const mutant of entry.mutants ?? []) {
      if (mutant.status === 'Ignored' || mutant.status === 'CompileError') continue;
      const line = mutant.location?.start?.line ?? 0;
      const key = mutantKey(
        file,
        memberAt(entry.source, line),
        mutant.mutatorName ?? '',
        originalText(entry.source, mutant.location),
        mutant.replacement ?? '');
      const found = index.get(key) ?? [];
      found.push({ line, surviving: SURVIVING_STATUSES.has(mutant.status) });
      index.set(key, found);
    }
  }
  return index;
}

// Sorts the justifications into the three things they can be, and takes the justified survivors out
// of the table. Nothing is filtered on the way in: a survivor leaves the table only because an entry
// claims it and that claim was checked against this run's own mutants.
function applyJustifications(survivors, entries, index) {
  const justifiedAt = new Map();
  const broken = [];

  for (const entry of entries) {
    const key = mutantKey(entry.file, entry.member, entry.mutator, entry.original, entry.mutated);
    const matches = index.get(key) ?? [];
    const candidates = entry.line === undefined ? matches : matches.filter((m) => m.line === entry.line);

    if (candidates.length === 0) {
      broken.push({
        entry,
        why: entry.line === undefined
          ? 'stale: this run has no such mutant, so the justification describes code that is gone'
          : `stale: this run has no such mutant on line ${entry.line}`
            + (matches.length > 0 ? `, though the same span is mutated on ${matches.map((m) => m.line).join(', ')}` : ''),
      });
      continue;
    }

    const lines = [...new Set(candidates.map((m) => m.line))];

    if (lines.length > 1) {
      broken.push({
        entry,
        why: `ambiguous: this key matches ${candidates.length} mutants, on lines ${lines.join(' and ')}, `
          + 'so it cannot say which one it justifies — add "line" to name the one it is about',
      });
      continue;
    }

    const killed = candidates.filter((m) => !m.surviving).length;

    if (killed > 0) {
      broken.push({
        entry,
        why: candidates.length === 1
          ? 'refuted: a test killed this mutant, so a value does tell the two apart'
          : `refuted: line ${lines[0]} carries ${candidates.length} mutants of this span and `
            + `${killed === 1 ? 'one of them is killed' : `${killed} of them are killed`}, so this cannot be one claim `
            + 'about all of them and nothing distinguishes one from another',
      });
      continue;
    }

    justifiedAt.set(`${key}\u0000${lines[0]}`, entry);
  }

  const remaining = [];
  const justified = [];
  for (const survivor of survivors) {
    const key = mutantKey(survivor.file, survivor.member, survivor.mutator, survivor.original, survivor.mutated);
    const entry = justifiedAt.get(`${key}\u0000${survivor.line}`);
    if (entry) justified.push({ ...survivor, because: entry.because, kind: entry.kind }); else remaining.push(survivor);
  }

  return { remaining, justified, broken };
}

// Loud, and above the survivor list, because it is a defect in the report's own input rather than in
// the code under test. A justification nobody checks any more is a claim about code that no longer
// exists, and the run that carries one has to be read differently from a run that does not.
function renderBrokenJustifications(broken) {
  if (broken.length === 0) return '';

  const rows = broken.map(({ entry, why }) =>
    `| ${cell(entry.file, 70)} | ${cell(entry.kind, 14)} | ${cell(entry.member, 30)} | ${cell(entry.mutator, 28)} | ${cell(entry.original, 40)} | ${cell(entry.mutated, 40)} | ${cell(why, 80)} |`);

  return [
    `**${broken.length} survivor justification${broken.length === 1 ? '' : 's'} no longer hold${broken.length === 1 ? 's' : ''}.**`,
    'Each one below describes a mutant this run does not have, one a test killed, a span written on more than one',
    'line so the entry cannot say which line it means, or one span whose several mutants this run no longer agrees',
    'about. They all mean the same thing for a reader: a sentence in the justification file is making a claim nobody',
    'is checking. Fix or remove the entry; do not leave it standing.',
    '',
    '| File | Kind | Member | Mutator | Original | Mutated | Why |',
    '| --- | --- | --- | --- | --- | --- | --- |',
    ...rows,
  ].join('\n');
}

// The justified survivors stay in the body with their reasons, and as a list rather than a table:
// the reason is the entry, and a table cell that cuts it at the column width would leave a claim
// nobody can weigh. The count alone would make the decision cheap to forget, and this list is the
// only place a reader can disagree with one.
function renderJustified(justified, kind) {
  const picked = justified.filter((s) => s.kind === kind);
  if (picked.length === 0) return '';

  const grouped = new Map();
  for (const s of picked) {
    const key = [s.file, s.line, s.mutator, s.original, s.because].join('\0');
    const group = grouped.get(key) ?? { s, covers: 0 };
    group.covers++;
    grouped.set(key, group);
  }

  const items = [...grouped.values()].flatMap(({ s, covers }) => [
    `- **${flatten(s.file, 90)}:${s.line}** — ${flatten(s.mutator, 40)} on \`${flatten(s.original, 160).replace(/`/g, "'")}\``
      + (covers > 1 ? ` — the line holds ${covers} mutants of this one span, and this entry covers them all` : ''),
    `  ${flatten(s.because, 700)}`,
  ]);

  const lead = kind === 'equivalent'
    ? [
      '### Survivors justified as equivalent',
      '',
      'No possible value can tell these mutants apart from the code they replaced, so no test can kill them and none is',
      'owed. Each justification is argued over the range of values, never over the suite. **The code these touch is open',
      'to question:** an expression whose mutant cannot be distinguished is a fair candidate for simplification or',
      'removal, and several entries here have ended that way.',
    ]
    : [
      '### Survivors justified as unobservable',
      '',
      'These mutants **do** behave differently, and no test can assert on the difference — not because the suite is thin,',
      'but because of what the difference is: undefined behaviour, a read past the end of something, a race, an effect',
      'that has no return value to read back. **Read this list the opposite way round from the one above.** The check each',
      'of these mutants removes is load-bearing precisely because nothing downstream would notice its absence, so it is',
      'the last thing to delete, not a candidate for tidying. Each entry names what the check defends.',
    ];

  return [
    ...lead,
    '',
    'Each is checked against this run: an entry that matches no mutant, or matches one a test killed, fails the run',
    'rather than ageing quietly.',
    '',
    ...items,
  ].join('\n');
}

// The closing section of the body, supplied by the repository rather than known here. It holds the
// local command that reproduces a run and whatever else is true of this repository and not of the
// other one sharing this file; those sentences were in this file once and were wrong in one of the
// two repositories the whole time.
//
// Unlike the expectation, it is optional, and the difference is deliberate. The expectation carries a
// claim about completeness, and there silence is a lie: a run that cannot say what it should have
// covered must fail rather than report a clean total. An appendix carries no claim at all. It is help
// for a reader, and failing a full mutation run over a missing block of help text would be formalism.
//
// Named but unreadable is a third case and does fail: the run was told to include something and could
// not, which is an instruction that went unmet rather than an instruction that was never given.
function readAppendix(path) {
  if (!path) return '';
  try {
    return `
${readFileSync(path, 'utf8').trimEnd()}`;
  } catch (error) {
    throw new Error(`The appendix at ${path} could not be read: ${error.message}`);
  }
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const reportPath = findReport(args.root);
  if (!reportPath) {
    console.error('No Stryker JSON report found under StrykerOutput. Did the run produce one?');
    process.exit(2);
  }

  const report = JSON.parse(readFileSync(reportPath, 'utf8'));
  const all = collectSurvivors(report, args.root);
  const total = all.total;
  const { remaining: survivors, justified, broken } = applyJustifications(
    all.survivors, readJustifications(args.justifications), indexTestedMutants(report, args.root));
  const blindSpots = collectBlindSpots(report, args.root);
  const scope = args.mode === 'pr' ? 'the files this pull request changed' : 'the projects it covered';

  const counted = args.expected ? census(report, args.root, readExpectation(args.expected)) : null;
  const abandoned = timeouts(report, args.root);

  // Every number, always, and the two kinds are never added together in the heading. A reader told
  // only a total would have no way to see that the other exists, and a reader told one combined
  // "justified" figure would have no way to see that half of it is load-bearing checks.
  const equivalent = justified.filter((s) => s.kind === 'equivalent');
  const unobservable = justified.filter((s) => s.kind === 'unobservable');
  const parts = [
    equivalent.length === 0 ? null : `${equivalent.length} justified as equivalent`,
    unobservable.length === 0 ? null : `${unobservable.length} justified as unobservable`,
  ].filter(Boolean);

  const accounted = parts.length === 0
    ? ''
    : ` Of those, ${parts.join(' and ')}, listed separately, leaving ${survivors.length} open.`;

  const heading = all.survivors.length === 0
    ? `No mutant survived over ${scope} (${total} mutants tested).`
    : `${all.survivors.length} of ${total} mutants survived over ${scope}.${accounted}`;

  // The census goes first. A reader who sees a shorter list than last week has to learn why before
  // reading the list, not after it. The timeouts and a broken justification follow it for the same
  // reason: each is another way for the list to be shorter than it should be. The timeouts are
  // stated even at zero, because "no mutant was abandoned" is the sentence that lets the rest of the
  // report be read as a measurement, and it has to be said rather than inferred from its absence.
  const preamble = [
    ...(counted ? [renderCensus(counted), ''] : []),
    renderTimeouts(abandoned),
    '',
    ...(broken.length === 0 ? [] : [renderBrokenJustifications(broken), '']),
  ];

  // The blind spots are reported whether or not anything survived. A run without survivors is
  // exactly the run in which an unreported blind spot would be read as a finished package.
  const body = [
    ...preamble,
    heading,
    '',
    renderBlindSpots(blindSpots, total),
    ...(survivors.length === 0
      ? []
      : ['', renderProjectSummary(survivors), '', renderTable(survivors, MAX_ROWS[args.mode])]),
    ...(equivalent.length === 0 ? [] : ['', renderJustified(justified, 'equivalent')]),
    ...(unobservable.length === 0 ? [] : ['', renderJustified(justified, 'unobservable')]),
    readAppendix(args.appendix),
  ].join('\n');

  console.log(body);

  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY, `## Mutation testing\n\n${body}\n`);
  }
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(
      process.env.GITHUB_OUTPUT,
      `survivors=${survivors.length}\ntotal=${total}\ncomplete=${counted ? counted.missing.length === 0 : ''}\n`
      + `compile-errors=${blindSpots.total}\nsilent-members=${blindSpots.silent}\n`
      + `equivalents=${equivalent.length}\nunobservable=${unobservable.length}\n`
      + `broken-justifications=${broken.length}\n`
      + `timeouts=${abandoned.total}\n`);
  }
  if (args.bodyFile) {
    const capped = body.length > ISSUE_BODY_LIMIT
      ? `${body.slice(0, ISSUE_BODY_LIMIT)}\n\n_Truncated; the full list is in the report the run left behind._`
      : body;
    writeFileSync(args.bodyFile, capped, 'utf8');
  }

  // --fail is what turns a survivor count into a red job. The scheduled run never passes it: it
  // reports, it does not block.
  if (args.fail && survivors.length > 0) process.exit(1);
}

main();
