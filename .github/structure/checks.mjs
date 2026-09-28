// The structure checks of verify-structure.mjs (Node 24). Each check enforces a sentence of
// docs/package-conventions.md ("PC") or docs/design/adr-0013-namespaces-by-feature.md ("ADR-0013"),
// quoted in RULES below; nothing here is a rule of its own. A check that needs the namespace graph
// reads it from CodeCharter's export (`codecharter graph overview --format json`, node ids
// `project|namespace`) and is skipped, loudly, when no graph is given.

import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { NAMING_RULES, runNamingChecks } from './naming.mjs';

export const RULES = {
  'solution': {
    cite: 'PC 6 step 6 and "The conventions rules"; docs/releasing.md "Pack"',
    text: 'The gates run on the solution (`dotnet build Bsft.sln`, `codecharter analyze Bsft.sln`) and the release packs `Bsft.sln`, so a project outside it is neither built, analysed nor shipped.',
  },
  'namespace-root': {
    cite: 'PC 3',
    text: 'The namespace root is the assembly name, except for an `.Abstractions` package, whose root is the name of the package it abstracts ... `Directory.Build.props` sets the project\'s `RootNamespace` to match.',
  },
  'namespace-folder': {
    cite: 'PC 3; ADR-0013 D3',
    text: 'The namespace of a file is the package\'s namespace root followed by its folder path, without exceptions ... File-scoped namespaces only. ... The same holds in test projects, whose root is their assembly name.',
  },
  'forbidden-folder': {
    cite: 'PC 2',
    text: 'Role and structural names are not folder names: `Services`, `Commands`, ... `Dtos`. Nor is a type name: the namespace would shadow the type for everything inside it.',
  },
  'folder-shadows-type': {
    cite: 'PC 2; PC 3',
    text: 'A folder must not produce a namespace that equals a type name in either assembly, since the namespace would shadow the type.',
  },
  'nesting': {
    cite: 'PC 2, table row `<Feature>/`',
    text: 'One use case or concern ... Nested at most once (`Delivery/Retries/`).',
  },
  'one-file-namespace': {
    cite: 'PC 2 "Do not over-split"',
    text: 'A one-file namespace is merged into the root or a neighbour unless that creates a cycle. Files are counted per namespace across an `.Abstractions` assembly and its implementation ...; `DependencyInjection`, `Diagnostics` and `Testing` are exempt. A package root may hold a single file.',
  },
  'small-package-flat': {
    cite: 'PC 2 "Do not over-split"',
    text: 'A package with about a dozen files or fewer besides its entry point stays flat: root plus `DependencyInjection/`.',
  },
  'root-sink': {
    cite: 'PC 2, table row "package root"; ADR-0013 D2 (a)',
    text: 'The root is a sink: no root type depends on a feature namespace of its own package. (`Diagnostics/`: cross-cutting, and allowed as long as it stays a sink.)',
  },
  'diagnostics-sink': {
    cite: 'PC 2, table row `Diagnostics/`',
    text: '`ActivitySource`, meters, event ids, log-message definitions. Cross-cutting, and allowed as long as it stays a sink.',
  },
  'di-contents': {
    cite: 'PC 2, table row `DependencyInjection/`',
    text: 'The DI entry points — `*ServiceCollectionExtensions`, `*EndpointRouteBuilderExtensions`, `*ApplicationBuilderExtensions` — and wiring-only helpers such as registration markers and the `IConfigureOptions<>` implementations that exist only for the registration. Nothing that runs at request time.',
  },
  'entry-point-class': {
    cite: 'PC 4 "DI entry point", "Pipeline / route wiring"',
    text: '`<Feature>ServiceCollectionExtensions` ..., methods `AddBsft<Feature>[<Aspect>]` ... `<Feature>ApplicationBuilderExtensions` (`UseBsft<Feature>[<Aspect>]`), `<Feature>EndpointRouteBuilderExtensions` (`MapBsft<Feature>[<Aspect>]`).',
  },
  'di-direction': {
    cite: 'PC 2, table row `DependencyInjection/`',
    text: 'It depends on the features it wires, never the reverse.',
  },
  'entry-point-namespace': {
    cite: 'PC 3; PC 4 "DI entry point", "Pipeline / route wiring"; ADR-0013 D2 (a)',
    text: '`<Feature>ServiceCollectionExtensions` in `<Package>.DependencyInjection`, methods `AddBsft<Feature>[<Aspect>]` ... `UseBsft<Feature>[<Aspect>]`, `MapBsft<Feature>[<Aspect>]`, both in `<Package>.DependencyInjection`.',
  },
  'cycles': {
    cite: 'PC 2 "The feature namespaces of a package form no cycle"; ADR-0013 D5',
    text: 'A cycle is a finding, not a layout choice. ... With D2 (a) there is no accepted exception.',
  },
  'abstractions-folders': {
    cite: 'PC 2; ADR-0013 D4',
    text: 'An `.Abstractions` package uses the feature folders of its implementation. ... A feature folder may carry the name of a satellite package when it holds the seam that satellite implements.',
  },
  'test-project': {
    cite: 'PC 1',
    text: 'Every package that ships behaviour has exactly one test project, `tests/<Package>.Tests` ... A contract-only package is the exception: ... `Bsft.Correlation.Abstractions`, `Bsft.Data.Contracts`, `Bsft.Mediator.Abstractions` and `Bsft.Notifications.Abstractions`. (PC 5: `Bsft.Templates`, under templates/, has no mirroring test project.)',
  },
  'test-mirror': {
    cite: 'PC 1; PC 6 step 4',
    text: '`tests/<Package>.Tests`, whose folder tree mirrors the package. ... Test fixtures live in `Support/`, fakes in `Fakes/`. Test classes are named `<TypeUnderTest>Tests`.',
  },
  'satellite-mirror': {
    cite: 'PC 2 "EFCore and other satellite packages"',
    text: 'A small one stays flat. A large one mirrors the core package\'s features.',
  },
  'packages-md': {
    cite: 'PC 5; PC 6 step 5',
    text: 'Every package also appears in packages.md, which maps each package to the topic page that documents it and to its entry point.',
  },
  'one-type-per-file': {
    cite: 'PC 4',
    text: 'One top-level type per file, file named after the type.',
  },
  'tier-marker': {
    cite: 'PC 1 "Tiers: Foundation and framework"',
    text: 'A package is Foundation if and only if its project file says `<Version>$(BsftFoundationVersion)</Version>`; every other package says `<Version>$(BsftVersion)</Version>`. There is no second marker.',
  },
  'tier-target': {
    cite: 'PC 1 "Tiers: Foundation and framework"',
    text: 'The target frameworks follow the tier: Directory.Build.props gives a Foundation package and its test project net8.0 and net10.0, and every other project net8.0. No project file names its target frameworks.',
  },
  'tier-reference': {
    cite: 'PC 1 "Tiers: Foundation and framework"',
    text: 'A Foundation package references only Foundation packages. The public repository is exported from this one and has to build without anything that stays here.',
  },
  'foundation-solution': {
    cite: 'PC 1 "Tiers: Foundation and framework"',
    text: '`Bsft.Foundation.sln` is the manifest of the export: every Foundation package, the test project of each and the Foundation samples, and no other package or its tests. Every project in it references only projects in it.',
  },
  // docs/naming.md, read by naming.mjs.
  ...NAMING_RULES,
};

// PC 2, the role and structural names, verbatim and in the order the section lists them.
export const FORBIDDEN_FOLDERS = [
  'Services', 'Commands', 'Queries', 'Endpoints', 'Stores', 'Models', 'Handlers', 'Abstractions',
  'Internal', 'Options', 'Validation', 'Validators', 'Middleware', 'Exceptions', 'Entities',
  'Configurations', 'Helpers', 'Utilities', 'Common', 'Shared', 'Extensions', 'Interfaces',
  'Infrastructure', 'Core', 'Types', 'Records', 'Dtos',
];

// PC 2: "about a dozen files or fewer besides its entry point". The docs say "about"; the check has
// to say a number, and it says the dozen itself. A package with 12 files outside
// DependencyInjection/ or fewer must be flat; one with 13 may have features.
export const SMALL_PACKAGE_MAX_FILES = 12;

// PC 2: the three namespaces "Do not over-split" exempts from the file count.
const EXEMPT_NAMESPACES = new Set(['DependencyInjection', 'Diagnostics', 'Testing']);

// PC 1: the four contract-only packages, exercised from their implementation's tests.
const CONTRACT_ONLY = new Set([
  'Bsft.Correlation.Abstractions', 'Bsft.Data.Contracts', 'Bsft.Mediator.Abstractions',
  'Bsft.Notifications.Abstractions',
]);

// PC 1: "Test fixtures live in `Support/`, fakes in `Fakes/`." The two folders a test project has
// beside the package's own tree.
const TEST_ONLY_FOLDERS = new Set(['Support', 'Fakes']);

const ENTRY_POINT = /(?:ServiceCollection|EndpointRouteBuilder|ApplicationBuilder)Extensions$/;

export function runChecks(repo, { solution, foundationSolution, packagesMd, graph } = {}) {
  const violations = [];
  const report = (rule, where, message) => violations.push({ rule, where, message });
  const skipped = [];

  const src = repo.projects.filter((p) => p.kind === 'src');
  const code = repo.projects.filter((p) => p.kind !== 'tests');
  const byName = new Map(repo.projects.map((p) => [`${p.kind}/${p.name}`, p]));
  const srcByName = new Map(src.map((p) => [p.name, p]));

  // A family shares namespaces: an `.Abstractions` assembly and its implementation (PC 3). Every
  // namespace count and folder comparison below runs over the family, keyed by the shared root.
  const families = new Map();
  for (const p of repo.projects) {
    const key = `${p.kind === 'tests' ? 'tests' : 'code'}:${p.root}`;
    if (!families.has(key)) families.set(key, { key, root: p.root, kind: p.kind, projects: [] });
    families.get(key).projects.push(p);
    p.family = families.get(key);
  }
  const familyFiles = (family) => family.projects.flatMap((p) => p.files);
  const familyFolders = (family) => new Set(family.projects.flatMap((p) => [...p.folders]));
  const familyTypes = (family) => new Set(familyFiles(family).flatMap((f) => f.types.map((t) => t.name)));

  // --- solution ---------------------------------------------------------------------------------
  if (solution) {
    const listed = new Set(solution);
    for (const p of repo.projects) {
      if (!listed.has(p.csproj)) report('solution', p.csproj, 'is not in Bsft.sln.');
    }
    const onDisk = new Set(repo.projects.map((p) => p.csproj));
    for (const entry of solution) {
      if (!onDisk.has(entry) && !existsSync(join(repo.root, entry))) report('solution', entry, 'is listed in Bsft.sln but does not exist on disk.');
    }
  } else {
    skipped.push('solution (no Bsft.sln)');
  }

  // --- tiers: Foundation and framework ----------------------------------------------------------
  // PC 1: the tier is the version property a package's project file names. A reference from a
  // Foundation package to a framework one is what would stop the export from building.
  for (const p of src) {
    if (p.tier === null) {
      report('tier-marker', p.csproj, `declares ${p.version === null ? 'no <Version>' : `<Version>${p.version}</Version>`}; a package versions itself from $(BsftVersion) or, when it is Foundation, $(BsftFoundationVersion).`);
    }
  }
  for (const p of repo.projects) {
    if (p.declaredTargetFrameworks !== null) {
      report('tier-target', p.csproj, `names its target frameworks (${p.declaredTargetFrameworks}); Directory.Build.props derives them from the tier.`);
    }
  }
  for (const p of src.filter((q) => q.tier === 'foundation')) {
    for (const r of p.references) {
      const target = srcByName.get(r);
      if (target && target.tier !== 'foundation') report('tier-reference', p.csproj, `is Foundation but references ${r}, which is not.`);
    }
  }

  if (foundationSolution) {
    const byCsproj = new Map(repo.projects.map((p) => [p.csproj, p]));
    const listed = new Set(foundationSolution);
    const members = [];
    for (const entry of foundationSolution) {
      const p = byCsproj.get(entry);
      if (p) members.push(p);
      else report('foundation-solution', entry, 'is listed in Bsft.Foundation.sln but is not a project under src/, samples/ or tests/.');
    }
    const memberNames = new Set(members.map((p) => p.name));
    for (const p of members) {
      for (const r of p.references) {
        if (!memberNames.has(r)) report('foundation-solution', p.csproj, `is in Bsft.Foundation.sln and references ${r}, which is not.`);
      }
    }
    const testsByName = new Map(repo.projects.filter((p) => p.kind === 'tests').map((p) => [p.name, p]));
    for (const p of src) {
      const isFoundation = p.tier === 'foundation';
      for (const q of [p, testsByName.get(`${p.name}.Tests`)].filter(Boolean)) {
        if (isFoundation && !listed.has(q.csproj)) report('foundation-solution', q.csproj, `is missing from Bsft.Foundation.sln, although ${p.name} is Foundation.`);
        if (!isFoundation && listed.has(q.csproj)) report('foundation-solution', q.csproj, `is listed in Bsft.Foundation.sln, although ${p.name} is not Foundation.`);
      }
    }
  } else {
    skipped.push('foundation-solution (no Bsft.Foundation.sln)');
  }

  // --- namespace root and namespace = folder ------------------------------------------------------
  for (const p of repo.projects) {
    if (p.declaredRootNamespace !== null && p.declaredRootNamespace !== p.root) {
      report('namespace-root', p.csproj, `declares RootNamespace ${p.declaredRootNamespace}; the root is ${p.root}.`);
    }
    for (const f of p.files) {
      const expected = [p.root, ...f.segments].join('.');
      checkFileNamespace(f, expected, report);
    }
  }
  // tests/Shared is not a project: its files are linked into test projects (PC 1) and keep the
  // namespace of their own folder under the tests root, `Bsft.Tests.Shared`, wherever they are
  // linked. See the note on it in PC 1 ("Test-only code ... lives in tests/Shared/").
  for (const f of repo.looseFiles) {
    if (f.top === 'tests' && f.folder === 'Shared') {
      checkFileNamespace(f, 'Bsft.Tests.Shared', report);
    } else {
      report('namespace-folder', f.path, 'lies under no project, so nothing builds it and no namespace root applies.');
    }
  }

  // --- folder names -------------------------------------------------------------------------------
  const forbidden = new Set(FORBIDDEN_FOLDERS);
  for (const p of repo.projects) {
    const types = familyTypes(p.family);
    for (const folder of [...p.folders].sort()) {
      const name = folder.split('/').at(-1);
      if (forbidden.has(name)) report('forbidden-folder', `${p.dir}/${folder}`, `"${name}" is a role or structural name, not a feature.`);
      else if (types.has(name)) report('folder-shadows-type', `${p.dir}/${folder}`, `produces namespace ${p.root}.${folder.replaceAll('/', '.')}, which shadows the type ${name} of the same family.`);
    }
  }

  // --- nesting ------------------------------------------------------------------------------------
  for (const p of repo.projects) {
    for (const folder of [...p.folders].sort()) {
      let segments = folder.split('/');
      // A test project mirrors the package (PC 1) and adds Support/ and Fakes/ beside what it
      // mirrors; those do not count as a nesting level of the mirrored tree.
      if (p.kind === 'tests') segments = segments.filter((s) => !TEST_ONLY_FOLDERS.has(s));
      if (segments.length > 2) report('nesting', `${p.dir}/${folder}`, `is nested ${segments.length - 1} times below a feature; the limit is once.`);
    }
  }

  // --- one-file namespaces and small packages (code only; a test project mirrors instead) ---------
  for (const family of families.values()) {
    if (family.kind === 'tests') continue;
    const files = familyFiles(family);
    const perFolder = new Map();
    for (const f of files) perFolder.set(f.folder, (perFolder.get(f.folder) ?? 0) + 1);
    for (const [folder, count] of [...perFolder].sort()) {
      if (folder === '' || count !== 1) continue;
      if (EXEMPT_NAMESPACES.has(folder.split('/')[0])) continue;
      const ns = `${family.root}.${folder.replaceAll('/', '.')}`;
      report('one-file-namespace', ns, `holds one file (${files.find((f) => f.folder === folder).path}); merge it into the root or a neighbour.`);
    }

    const counted = files.filter((f) => f.segments[0] !== 'DependencyInjection');
    const features = [...new Set(files.map((f) => f.segments[0]).filter((s) => s !== undefined && s !== 'DependencyInjection'))].sort();
    if (counted.length <= SMALL_PACKAGE_MAX_FILES && features.length > 0) {
      report('small-package-flat', family.root, `has ${counted.length} file(s) besides DependencyInjection/ (at most ${SMALL_PACKAGE_MAX_FILES} stays flat) but folders ${features.join(', ')}.`);
    }
  }

  // --- DependencyInjection holds entry points and wiring-only helpers -----------------------------
  // "Nothing that runs at request time" is read from the role a type's name declares (PC 4 "Roles",
  // "Endpoints", "Handlers"): a middleware, filter, handler, endpoint class, controller, hub, store
  // or service is a component that runs, so it lives in the feature it serves. Everything else in
  // DependencyInjection/ is an entry point or a wiring-only helper (a registration marker, an
  // IConfigureOptions<> implementation, a registration builder, a startup check of the wiring).
  const RUNS_AT_REQUEST_TIME = /(?:Middleware|Filter|Handler|Endpoints|Controller|Hub|Store|Service)$/;
  // PC 4: an entry point on IServiceCollection is a `<Feature>ServiceCollectionExtensions`, on
  // IEndpointRouteBuilder an `<Feature>EndpointRouteBuilderExtensions`, on IApplicationBuilder an
  // `<Feature>ApplicationBuilderExtensions`.
  const ENTRY_POINT_CLASS = {
    IServiceCollection: 'ServiceCollectionExtensions',
    IEndpointRouteBuilder: 'EndpointRouteBuilderExtensions',
    IApplicationBuilder: 'ApplicationBuilderExtensions',
  };
  for (const p of code) {
    for (const f of p.files) {
      for (const m of f.extensionMethods) {
        const suffix = ENTRY_POINT_CLASS[m.extends];
        if (suffix && m.type !== null && !m.type.endsWith(suffix)) {
          report('entry-point-class', f.path, `${m.type}.${m.name} extends ${m.extends}, so its class is a <Feature>${suffix}.`);
        }
      }
      if (f.segments[0] !== 'DependencyInjection') continue;
      for (const t of f.types) {
        if (ENTRY_POINT.test(t.name)) continue;
        if (RUNS_AT_REQUEST_TIME.test(t.name)) report('di-contents', f.path, `declares ${t.kind} ${t.name}, a component that runs rather than wiring; it belongs in the feature it serves.`);
      }
    }
  }

  // --- AddBsft*/MapBsft*/UseBsft* live in <Package>.DependencyInjection ----------------------------
  for (const p of code) {
    const expected = `${p.root}.DependencyInjection`;
    for (const f of p.files) {
      if (f.extensionMethods.length === 0) continue;
      const ns = f.namespaces[0]?.name ?? '(global)';
      if (ns !== expected) {
        report('entry-point-namespace', f.path, `declares ${[...new Set(f.extensionMethods.map((m) => m.name))].join(', ')} in ${ns}; entry points live in ${expected}.`);
      }
    }
  }

  // --- Abstractions use the feature folders of their implementation --------------------------------
  const projectRoots = new Set(src.map((p) => p.root));
  for (const p of src.filter((q) => q.name.endsWith('.Abstractions'))) {
    const impl = srcByName.get(p.root);
    if (!impl) {
      report('abstractions-folders', p.dir, `abstracts ${p.root}, which is not a package under src/.`);
      continue;
    }
    for (const folder of [...p.folders].sort()) {
      if (impl.folders.has(folder)) continue;
      // The satellite-seam case of PC 2: the folder's namespace is the root of another package.
      if (projectRoots.has(`${p.root}.${folder.replaceAll('/', '.')}`)) continue;
      report('abstractions-folders', `${p.dir}/${folder}`, `is not a feature folder of ${impl.name}, nor the namespace of a satellite package.`);
    }
  }

  // --- test projects -------------------------------------------------------------------------------
  const tests = new Map(repo.projects.filter((p) => p.kind === 'tests').map((p) => [p.name, p]));
  for (const p of src) {
    if (CONTRACT_ONLY.has(p.name)) continue;
    if (!tests.has(`${p.name}.Tests`)) report('test-project', p.dir, `has no test project tests/${p.name}.Tests.`);
  }
  for (const t of tests.values()) {
    const pkg = srcByName.get(t.name.slice(0, -'.Tests'.length)) ?? byName.get(`samples/${t.name.slice(0, -'.Tests'.length)}`);
    if (!pkg) continue; // a test project of no package (a scenario suite) has nothing to mirror
    // What the test project mirrors: its package's family, plus any contract-only package (PC 1)
    // that package references, since that package's types are "exercised from the test project of
    // the implementation that consumes them".
    const mirrored = [pkg.family, ...pkg.references
      .filter((r) => CONTRACT_ONLY.has(r) && srcByName.has(r) && srcByName.get(r).family !== pkg.family)
      .map((r) => srcByName.get(r).family)];
    const pkgFolders = new Set(mirrored.flatMap((family) => [...familyFolders(family)]));
    // Where each type of those families is declared, by simple name; ambiguous names are dropped
    // rather than guessed.
    const typeFolder = new Map();
    for (const f of mirrored.flatMap(familyFiles)) {
      for (const ty of f.types) typeFolder.set(ty.name, typeFolder.has(ty.name) && typeFolder.get(ty.name) !== f.folder ? null : f.folder);
    }
    for (const folder of [...t.folders].sort()) {
      const kept = folder.split('/').filter((s) => !TEST_ONLY_FOLDERS.has(s));
      if (kept.length !== folder.split('/').length) {
        // Support/ or Fakes/ below a mirrored folder: the part before it has to mirror.
        const before = folder.split('/').slice(0, folder.split('/').findIndex((s) => TEST_ONLY_FOLDERS.has(s)));
        if (before.length > 0 && !pkgFolders.has(before.join('/'))) report('test-mirror', `${t.dir}/${folder}`, `has no counterpart ${before.join('/')} in ${pkg.name}.`);
        continue;
      }
      if (!pkgFolders.has(folder)) report('test-mirror', `${t.dir}/${folder}`, `has no counterpart in ${pkg.name}; a test project mirrors its package's folders (plus Support/ and Fakes/).`);
    }
    for (const f of t.files) {
      if (f.segments.some((s) => TEST_ONLY_FOLDERS.has(s))) continue;
      const stem = f.path.split('/').at(-1).replace(/\.cs$/, '');
      if (!stem.endsWith('Tests')) continue;
      const underTest = stem.slice(0, -'Tests'.length);
      const home = typeFolder.get(underTest);
      if (home === undefined || home === null) continue;
      if (home !== f.folder) report('test-mirror', f.path, `tests ${underTest}, which lives in ${home === '' ? 'the root' : home + '/'} of ${pkg.name}; the test belongs in the same folder.`);
    }
  }

  // --- EFCore satellites mirror the core package's features ----------------------------------------
  // What PC 2 decides for a large EFCore package: the persistence of a core feature sits in that
  // feature's folder ("`Messages/` holds `MessageEntity`, `MessageEntityConfiguration` and
  // `EFCoreMessageStore` together"), and entities several features share stay in the root beside
  // `I<Feature>DbContext`. So an entity or its configuration is either in the root or in a folder
  // named after a feature of the core package. A folder for a concern only the EF side has
  // (change tracking, an outbox, reconciliation) is not addressed by the sentence and not judged.
  for (const p of src.filter((q) => q.name.endsWith('.EFCore'))) {
    const core = srcByName.get(p.name.slice(0, -'.EFCore'.length));
    if (!core) continue;
    const counted = p.files.filter((f) => f.segments[0] !== 'DependencyInjection').length;
    if (counted <= SMALL_PACKAGE_MAX_FILES) continue; // small: the flatness check above applies
    const coreFolders = familyFolders(core.family);
    for (const f of p.files) {
      if (f.segments.length === 0 || EXEMPT_NAMESPACES.has(f.segments[0])) continue;
      if (coreFolders.has(f.folder)) continue;
      for (const t of f.types) {
        if (/(?:Entity|EntityConfiguration)$/.test(t.name)) report('satellite-mirror', f.path, `${t.name} sits in ${f.folder}/, which is not a feature of ${core.name}; persistence goes into the core feature's folder or the root.`);
      }
    }
  }

  // --- docs/packages.md ------------------------------------------------------------------------------
  if (packagesMd !== undefined) {
    const rows = new Set([...packagesMd.matchAll(/^\|\s*`(Bsft(?:\.[A-Za-z0-9_]+)*)`\s*\|/gm)].map((m) => m[1]));
    // PC 5: Bsft.Templates, the template pack under templates/, keeps its row like any package.
    const packages = new Set([...src.map((p) => p.name), ...(repo.templatePackages ?? [])]);
    for (const name of [...packages].sort()) if (!rows.has(name)) report('packages-md', 'docs/packages.md', `has no row for ${name}.`);
    for (const name of [...rows].sort()) if (!packages.has(name)) report('packages-md', 'docs/packages.md', `has a row for ${name}, which is not a package.`);
  } else {
    skipped.push('packages-md (no docs/packages.md)');
  }

  // --- one top-level type per file, named after it ------------------------------------------------
  for (const p of repo.projects) {
    for (const f of p.files) checkOneTypePerFile(f, report);
  }
  for (const f of repo.looseFiles) checkOneTypePerFile(f, report);

  // --- naming (docs/naming.md) --------------------------------------------------------------------
  runNamingChecks(repo, report);

  // --- graph checks -------------------------------------------------------------------------------
  if (graph) {
    runGraphChecks(repo, graph, report);
  } else {
    skipped.push('root-sink, diagnostics-sink, di-direction, cycles (no --graph given)');
  }

  violations.sort((a, b) => a.rule.localeCompare(b.rule) || a.where.localeCompare(b.where) || a.message.localeCompare(b.message));
  return { violations, skipped };
}

function checkFileNamespace(f, expected, report) {
  if (f.namespaces.length === 0) {
    // The one declaration C# itself keeps out of any namespace: the `partial class Program` a file
    // with top-level statements declares so that a test host can name the synthesized entry point.
    // It merges with a type the compiler generates in the global namespace and cannot move.
    const onlyProgram = f.types.length > 0 && f.types.every((t) => t.name === 'Program' && t.partial);
    if (f.types.length > 0 && !onlyProgram) report('namespace-folder', f.path, `declares types outside any namespace; expected namespace ${expected};.`);
    return;
  }
  if (f.namespaces.some((n) => !n.fileScoped)) report('namespace-folder', f.path, 'uses a block-scoped namespace; file-scoped namespaces only.');
  if (f.namespaces.length > 1) report('namespace-folder', f.path, `declares ${f.namespaces.length} namespaces; one per file.`);
  const actual = f.namespaces[0].name;
  if (actual !== expected) report('namespace-folder', f.path, `declares namespace ${actual}; its folder makes it ${expected}.`);
}

function checkOneTypePerFile(f, report) {
  if (f.types.length === 0) return;
  const stem = f.path.split('/').at(-1).replace(/\.cs$/, '');
  const names = [...new Set(f.types.map((t) => t.name))];
  if (names.length > 1) {
    report('one-type-per-file', f.path, `declares ${names.length} top-level types (${names.join(', ')}); one per file.`);
    return;
  }
  const [name] = names;
  if (name === 'Program' && f.types.every((t) => t.partial)) return; // see checkFileNamespace
  // A partial type split over several files names each file `<Type>.<Part>.cs`.
  if (stem === name || (f.types.every((t) => t.partial) && stem.startsWith(`${name}.`))) return;
  report('one-type-per-file', f.path, `declares ${name}; the file is named after its type (${name}.cs).`);
}

// Tarjan's strongly connected components over the namespace graph, iteratively.
export function stronglyConnected(nodes, edges) {
  const adjacency = new Map(nodes.map((n) => [n, []]));
  for (const [from, to] of edges) {
    if (!adjacency.has(from)) adjacency.set(from, []);
    if (!adjacency.has(to)) adjacency.set(to, []);
    if (from !== to) adjacency.get(from).push(to);
  }
  const index = new Map();
  const low = new Map();
  const onStack = new Set();
  const stack = [];
  const components = [];
  let counter = 0;
  for (const start of [...adjacency.keys()].sort()) {
    if (index.has(start)) continue;
    const work = [[start, 0]];
    index.set(start, counter); low.set(start, counter); counter++;
    stack.push(start); onStack.add(start);
    while (work.length > 0) {
      const frame = work.at(-1);
      const [node, k] = frame;
      const next = adjacency.get(node);
      if (k < next.length) {
        frame[1]++;
        const w = next[k];
        if (!index.has(w)) {
          index.set(w, counter); low.set(w, counter); counter++;
          stack.push(w); onStack.add(w);
          work.push([w, 0]);
        } else if (onStack.has(w)) {
          low.set(node, Math.min(low.get(node), index.get(w)));
        }
        continue;
      }
      work.pop();
      if (work.length > 0) {
        const parent = work.at(-1)[0];
        low.set(parent, Math.min(low.get(parent), low.get(node)));
      }
      if (low.get(node) === index.get(node)) {
        const component = [];
        let w;
        do { w = stack.pop(); onStack.delete(w); component.push(w); } while (w !== node);
        if (component.length > 1) components.push(component.sort());
      }
    }
  }
  return components;
}

function runGraphChecks(repo, graph, report) {
  const projects = new Map(repo.projects.map((p) => [p.name, p]));
  const split = (id) => {
    const bar = id.indexOf('|');
    return { project: id.slice(0, bar), namespace: id.slice(bar + 1) };
  };

  for (const edge of graph.edges) {
    const from = split(edge.sourceId);
    const to = split(edge.targetId);
    const source = projects.get(from.project);
    const target = projects.get(to.project);
    if (!source || !target || source.family !== target.family) continue;
    if (from.namespace === to.namespace) continue;
    const root = source.family.root;
    const feature = (ns) => (ns.startsWith(`${root}.`) ? ns.slice(root.length + 1).split('.')[0] : null);

    if (from.namespace === root && feature(to.namespace) !== null && feature(to.namespace) !== 'Diagnostics') {
      report('root-sink', `${root}`, `depends on ${to.namespace} (${edge.sourceId} -> ${edge.targetId}); the root is a sink.`);
    }
    if (feature(from.namespace) === 'Diagnostics' && feature(to.namespace) !== null && feature(to.namespace) !== 'Diagnostics') {
      report('diagnostics-sink', from.namespace, `depends on ${to.namespace} (${edge.sourceId} -> ${edge.targetId}); Diagnostics stays a sink.`);
    }
    // The host's own top-level statements (`<global namespace>` of a sample's Program.cs) are the
    // composition root that calls the entry points, not a feature of the package.
    if (feature(to.namespace) === 'DependencyInjection' && feature(from.namespace) !== 'DependencyInjection' && from.namespace !== '<global namespace>') {
      report('di-direction', from.namespace, `depends on ${to.namespace} (${edge.sourceId} -> ${edge.targetId}); DependencyInjection depends on the features, never the reverse.`);
    }
  }

  const nodes = graph.nodes.map((n) => n.id);
  const components = stronglyConnected(nodes, graph.edges.map((e) => [e.sourceId, e.targetId]));
  for (const component of components) {
    report('cycles', component[0], `is in a namespace cycle of ${component.length}: ${component.join(', ')}.`);
  }
}

export function readGraph(path) {
  const graph = JSON.parse(readFileSync(path, 'utf8'));
  if (!Array.isArray(graph.nodes) || !Array.isArray(graph.edges)) throw new Error(`${path} is not a CodeCharter graph overview export (no nodes/edges).`);
  if (graph.isTruncated) throw new Error(`${path} is truncated (${graph.truncationReason}); export it with --top 100000.`);
  return graph;
}
