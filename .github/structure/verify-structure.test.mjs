// Self-test of verify-structure.mjs (Node 24): `node --test '.github/structure/*.test.mjs'`.
//
// Each test builds a small repository in a temporary directory — one package with its
// `.Abstractions`, an EFCore satellite, their test projects, Bsft.sln and docs/packages.md — that
// conforms to every rule, then breaks exactly one thing and asserts that exactly the rule for it
// fires. A check that can never fire looks exactly like a clean tree, so every rule has a case here.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { execFileSync } from 'node:child_process';
import { readRepository, readSolution } from './model.mjs';
import { runChecks, stronglyConnected, SMALL_PACKAGE_MAX_FILES } from './checks.mjs';
import { readDeclarations, stripTrivia, stringLiterals } from './csharp.mjs';

const csproj = '<Project Sdk="Microsoft.NET.Sdk"></Project>\n';
// A package names its tier in <Version> (PC 1 "Tiers: Foundation and framework").
const versioned = (marker, inner = '') => `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>${marker}</Version></PropertyGroup>${inner}</Project>\n`;
const framework = (inner) => versioned('$(BsftVersion)', inner);
const foundation = (inner) => versioned('$(BsftFoundationVersion)', inner);
const type = (ns, decl) => `namespace ${ns};\n\n${decl}\n`;

// A conforming repository. Bsft.Shop has 14 files outside DependencyInjection/ (above the flat
// threshold), so it may have features; its EFCore satellite is large too. Bsft.Shop.Abstractions is
// the one Foundation package, so Bsft.Foundation.sln holds it and its test project.
function baseline() {
  const files = {
    'src/Bsft.Shop/Bsft.Shop.csproj': framework('<ItemGroup><ProjectReference Include="..\\Bsft.Shop.Abstractions\\Bsft.Shop.Abstractions.csproj" /></ItemGroup>'),
    'src/Bsft.Shop/BsftShopOptions.cs': type('Bsft.Shop', 'public sealed class BsftShopOptions { }'),
    'src/Bsft.Shop/ShopDefaults.cs': type('Bsft.Shop', 'public static class ShopDefaults { }'),
    'src/Bsft.Shop/DependencyInjection/ShopServiceCollectionExtensions.cs': type('Bsft.Shop.DependencyInjection',
      'public static class ShopServiceCollectionExtensions\n{\n    public static IServiceCollection AddBsftShop(this IServiceCollection services) => services;\n}'),
    'src/Bsft.Shop/DependencyInjection/ShopRegistrationMarker.cs': type('Bsft.Shop.DependencyInjection', 'internal sealed class ShopRegistrationMarker { }'),
    'src/Bsft.Shop/Diagnostics/ShopActivity.cs': type('Bsft.Shop.Diagnostics', 'internal static class ShopActivity { }'),
    'src/Bsft.Shop.Abstractions/Bsft.Shop.Abstractions.csproj': foundation(),
    'src/Bsft.Shop.Abstractions/IOrderStore.cs': type('Bsft.Shop', 'public interface IOrderStore { }'),
    'src/Bsft.Shop.Abstractions/Ordering/OrderPlaced.cs': type('Bsft.Shop.Ordering', 'public sealed record OrderPlaced(int Id);'),
    'src/Bsft.Shop.Abstractions/Payments/PaymentMethod.cs': type('Bsft.Shop.Payments', 'public enum PaymentMethod { Card }'),
    'src/Bsft.Shop.EFCore/Bsft.Shop.EFCore.csproj': framework(),
    'src/Bsft.Shop.EFCore/IShopDbContext.cs': type('Bsft.Shop.EFCore', 'public interface IShopDbContext { }'),
    'src/Bsft.Shop.EFCore/DependencyInjection/ShopEFCoreServiceCollectionExtensions.cs': type('Bsft.Shop.EFCore.DependencyInjection', 'public static class ShopEFCoreServiceCollectionExtensions { }'),
    'tests/Bsft.Shop.Tests/Bsft.Shop.Tests.csproj': csproj,
    'tests/Bsft.Shop.Tests/Ordering/OrderServiceTests.cs': type('Bsft.Shop.Tests.Ordering', 'public sealed class OrderServiceTests { }'),
    'tests/Bsft.Shop.Tests/Support/ShopFixture.cs': type('Bsft.Shop.Tests.Support', 'internal sealed class ShopFixture { }'),
    'tests/Bsft.Shop.Tests/Ordering/Fakes/FakeClock.cs': type('Bsft.Shop.Tests.Ordering.Fakes', 'internal sealed class FakeClock { }'),
    'tests/Bsft.Shop.Abstractions.Tests/Bsft.Shop.Abstractions.Tests.csproj': csproj,
    'tests/Bsft.Shop.Abstractions.Tests/Ordering/OrderPlacedTests.cs': type('Bsft.Shop.Abstractions.Tests.Ordering', 'public sealed class OrderPlacedTests { }'),
    'tests/Bsft.Shop.EFCore.Tests/Bsft.Shop.EFCore.Tests.csproj': csproj,
    'tests/Shared/RecordingLogger.cs': type('Bsft.Tests.Shared', 'internal sealed class RecordingLogger { }'),
  };
  for (const feature of ['Ordering', 'Payments']) {
    for (const name of ['Service', 'Endpoints', 'Handler', 'Request', 'Validator']) {
      files[`src/Bsft.Shop/${feature}/${feature}${name}.cs`] = type(`Bsft.Shop.${feature}`, `internal sealed class ${feature}${name} { }`);
    }
  }
  for (const name of ['Order', 'Payment']) {
    files[`src/Bsft.Shop.EFCore/${name}Entity.cs`] = type('Bsft.Shop.EFCore', `internal sealed class ${name}Entity { }`);
    files[`src/Bsft.Shop.EFCore/${name}EntityConfiguration.cs`] = type('Bsft.Shop.EFCore', `internal sealed class ${name}EntityConfiguration { }`);
  }
  for (const name of ['Store', 'Reader', 'Writer', 'Mapper', 'Sweeper', 'Archiver']) {
    files[`src/Bsft.Shop.EFCore/Ordering/EFCoreOrder${name}.cs`] = type('Bsft.Shop.EFCore.Ordering', `internal sealed class EFCoreOrder${name} { }`);
  }
  files['src/Bsft.Shop.EFCore/Ordering/LineItemEntity.cs'] = type('Bsft.Shop.EFCore.Ordering', 'internal sealed class LineItemEntity { }');
  files['src/Bsft.Shop.EFCore/Ordering/LineItemEntityConfiguration.cs'] = type('Bsft.Shop.EFCore.Ordering', 'internal sealed class LineItemEntityConfiguration { }');
  files['docs/packages.md'] = '| Package | Documented in | Entry point |\n| --- | --- | --- |\n| `Bsft.Shop` | x | `AddBsftShop` |\n| `Bsft.Shop.Abstractions` | x | — |\n| `Bsft.Shop.EFCore` | x | `AddBsftShopEFCore` |\n';
  return files;
}

function solutionFor(files) {
  const lines = Object.keys(files).filter((p) => p.endsWith('.csproj'))
    .map((p) => `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "${p.split('/').at(-1).replace('.csproj', '')}", "${p.replaceAll('/', '\\')}", "{00000000-0000-0000-0000-000000000000}"\nEndProject`);
  return `Microsoft Visual Studio Solution File, Format Version 12.00\n${lines.join('\n')}\n`;
}

// The Foundation manifest of a tree: every Foundation package under src/ and its test project.
function foundationSolutionFor(files) {
  const packages = Object.keys(files)
    .filter((p) => p.startsWith('src/') && p.endsWith('.csproj') && files[p]?.includes('$(BsftFoundationVersion)'))
    .map((p) => p.split('/')[1]);
  const listed = Object.fromEntries(Object.keys(files)
    .filter((p) => p.endsWith('.csproj') && packages.some((name) => p === `src/${name}/${name}.csproj` || p === `tests/${name}.Tests/${name}.Tests.csproj`))
    .map((p) => [p, files[p]]));
  return solutionFor(listed);
}

function run(files, { solution, foundation: foundationSln, graph } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'verify-structure-'));
  try {
    for (const [path, content] of Object.entries(files)) {
      if (content === null) continue;
      mkdirSync(dirname(join(root, path)), { recursive: true });
      writeFileSync(join(root, path), content);
    }
    writeFileSync(join(root, 'Bsft.sln'), solution ?? solutionFor(files));
    // null leaves the manifest out, which the foundation-solution rule reports as not checked.
    if (foundationSln !== null) writeFileSync(join(root, 'Bsft.Foundation.sln'), foundationSln ?? foundationSolutionFor(files));
    const repo = readRepository(root);
    const packagesMd = files['docs/packages.md'] ? readFileSync(join(root, 'docs/packages.md'), 'utf8') : undefined;
    return runChecks(repo, {
      solution: readSolution(root, 'Bsft.sln'),
      foundationSolution: readSolution(root, 'Bsft.Foundation.sln'),
      packagesMd,
      graph,
    }).violations;
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const rules = (violations) => [...new Set(violations.map((v) => v.rule))].sort();
const only = (violations, rule) => {
  assert.deepEqual(rules(violations), [rule], JSON.stringify(violations, null, 1));
};
const withFiles = (changes) => ({ ...baseline(), ...changes });

test('the baseline repository conforms to every rule', () => {
  assert.deepEqual(run(baseline()), []);
});

test('solution: a project missing from Bsft.sln, and an entry missing on disk', () => {
  const files = baseline();
  const listed = { ...files };
  delete listed['tests/Bsft.Shop.EFCore.Tests/Bsft.Shop.EFCore.Tests.csproj'];
  only(run(files, { solution: solutionFor(listed) }), 'solution');
  only(run(files, { solution: solutionFor({ ...files, 'src/Bsft.Gone/Bsft.Gone.csproj': '' }) }), 'solution');
});

test('namespace-root: a RootNamespace that is not the assembly name', () => {
  only(run(withFiles({ 'src/Bsft.Shop/Bsft.Shop.csproj': '<Project><PropertyGroup><RootNamespace>Shop</RootNamespace><Version>$(BsftVersion)</Version></PropertyGroup></Project>' })), 'namespace-root');
});

test('namespace-folder: a namespace that is not the folder, a block namespace, a file of no project', () => {
  only(run(withFiles({ 'src/Bsft.Shop/Ordering/OrderingService.cs': type('Bsft.Shop', 'internal sealed class OrderingService { }') })), 'namespace-folder');
  only(run(withFiles({ 'src/Bsft.Shop/ShopDefaults.cs': 'namespace Bsft.Shop\n{\n    public static class ShopDefaults { }\n}\n' })), 'namespace-folder');
  only(run(withFiles({ 'src/Loose.cs': type('Bsft', 'internal sealed class Loose { }') })), 'namespace-folder');
  // The .Abstractions root drops the suffix; the test project of an .Abstractions package does not.
  only(run(withFiles({ 'src/Bsft.Shop.Abstractions/IOrderStore.cs': type('Bsft.Shop.Abstractions', 'public interface IOrderStore { }') })), 'namespace-folder');
});

test('namespace-folder: the partial Program of top-level statements stays global', () => {
  assert.deepEqual(run(withFiles({ 'src/Bsft.Shop/Program.cs': 'var x = 1;\n\npublic partial class Program;\n' })).filter((v) => v.rule === 'namespace-folder'), []);
});

test('forbidden-folder: a role folder in src and in tests', () => {
  only(run(withFiles({
    'src/Bsft.Shop/Services/ShopService.cs': type('Bsft.Shop.Services', 'internal sealed class ShopService { }'),
    'src/Bsft.Shop/Services/ShopPricing.cs': type('Bsft.Shop.Services', 'internal sealed class ShopPricing { }'),
  })), 'forbidden-folder');
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/Ordering/Handlers/OrderingHandlerTests.cs': type('Bsft.Shop.Tests.Ordering.Handlers', 'public sealed class OrderingHandlerTests { }') })).filter((v) => v.rule !== 'test-mirror'), 'forbidden-folder');
});

test('folder-shadows-type: a folder named like a type of the family', () => {
  const v = run(withFiles({ 'src/Bsft.Shop.Abstractions/Ordering.cs': type('Bsft.Shop', 'public sealed class Ordering { }') }));
  only(v, 'folder-shadows-type');
});

test('nesting: a feature nested twice', () => {
  const files = withFiles({});
  for (const n of ['A', 'B', 'C']) files[`src/Bsft.Shop/Ordering/Returns/Refunds/Refund${n}.cs`] = type('Bsft.Shop.Ordering.Returns.Refunds', `internal sealed class Refund${n} { }`);
  only(run(files).filter((v) => v.rule !== 'one-file-namespace'), 'nesting');
});

test('nesting: Support/ and Fakes/ below a mirrored test folder do not count', () => {
  assert.deepEqual(run(withFiles({ 'tests/Bsft.Shop.Tests/Ordering/Support/Deep.cs': type('Bsft.Shop.Tests.Ordering.Support', 'internal sealed class Deep { }') })), []);
});

test('one-file-namespace: a feature of one file, counted across the .Abstractions pair', () => {
  only(run(withFiles({ 'src/Bsft.Shop/Shipping/ShippingService.cs': type('Bsft.Shop.Shipping', 'internal sealed class ShippingService { }') })), 'one-file-namespace');
  // Ordering has five files in the implementation and one in the contract: not a one-file namespace.
  // Diagnostics holds one file and is exempt; the root may hold one.
  assert.deepEqual(run(baseline()).filter((v) => v.rule === 'one-file-namespace'), []);
});

test(`small-package-flat: ${SMALL_PACKAGE_MAX_FILES} files or fewer and a folder`, () => {
  const files = {
    'src/Bsft.Tiny/Bsft.Tiny.csproj': framework(),
    'src/Bsft.Tiny/DependencyInjection/TinyServiceCollectionExtensions.cs': type('Bsft.Tiny.DependencyInjection', 'public static class TinyServiceCollectionExtensions { }'),
    'tests/Bsft.Tiny.Tests/Bsft.Tiny.Tests.csproj': csproj,
  };
  for (const n of ['A', 'B']) files[`src/Bsft.Tiny/Parts/Part${n}.cs`] = type('Bsft.Tiny.Parts', `internal sealed class Part${n} { }`);
  const v = run({ ...files, 'docs/packages.md': '| `Bsft.Tiny` | x | x |\n' });
  only(v, 'small-package-flat');
});

test('di-contents: a component that runs, in DependencyInjection/', () => {
  only(run(withFiles({ 'src/Bsft.Shop/DependencyInjection/ShopMiddleware.cs': type('Bsft.Shop.DependencyInjection', 'internal sealed class ShopMiddleware { }') })), 'di-contents');
});

test('entry-point-class: an AddBsft* on IServiceCollection in a class not named for it', () => {
  only(run(withFiles({
    'src/Bsft.Shop/DependencyInjection/ShopExtensions.cs': type('Bsft.Shop.DependencyInjection',
      'public static class ShopExtensions\n{\n    public static IServiceCollection AddBsftShopExtras(this IServiceCollection services) => services;\n}'),
  })), 'entry-point-class');
});

test('entry-point-namespace: an AddBsft* outside <Package>.DependencyInjection', () => {
  only(run(withFiles({
    'src/Bsft.Shop/ShopHttpClientBuilderExtensions.cs': type('Bsft.Shop',
      'public static class ShopHttpClientBuilderExtensions\n{\n    public static IHttpClientBuilder AddBsftShopClient(this IHttpClientBuilder builder) => builder;\n}'),
  })), 'entry-point-namespace');
});

test('abstractions-folders: a contract folder the implementation lacks, unless it names a satellite', () => {
  const files = withFiles({});
  for (const n of ['A', 'B']) files[`src/Bsft.Shop.Abstractions/Reading/Reader${n}.cs`] = type('Bsft.Shop.Reading', `public interface Reader${n} { }`);
  only(run(files), 'abstractions-folders');
  // PC 2: a folder named after a satellite package holds that satellite's seam.
  const seam = withFiles({ 'src/Bsft.Shop.Abstractions/EFCore/IShopSeam.cs': type('Bsft.Shop.EFCore', 'public interface IShopSeam { }') });
  assert.deepEqual(run(seam).filter((v) => v.rule === 'abstractions-folders'), []);
});

test('test-project: a package without tests/<Package>.Tests', () => {
  const files = baseline();
  delete files['tests/Bsft.Shop.EFCore.Tests/Bsft.Shop.EFCore.Tests.csproj'];
  only(run(files), 'test-project');
});

test('test-mirror: a test folder the package lacks, and a test outside its type\'s folder', () => {
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/Acceptance/WalkthroughTests.cs': type('Bsft.Shop.Tests.Acceptance', 'public sealed class WalkthroughTests { }') })), 'test-mirror');
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/PaymentsServiceTests.cs': type('Bsft.Shop.Tests', 'public sealed class PaymentsServiceTests { }') })), 'test-mirror');
});

test('satellite-mirror: an entity of a large EFCore package outside the root and the core features', () => {
  only(run(withFiles({
    'src/Bsft.Shop.EFCore/Archive/ArchivedOrderEntity.cs': type('Bsft.Shop.EFCore.Archive', 'internal sealed class ArchivedOrderEntity { }'),
    'src/Bsft.Shop.EFCore/Archive/ArchivedOrderEntityConfiguration.cs': type('Bsft.Shop.EFCore.Archive', 'internal sealed class ArchivedOrderEntityConfiguration { }'),
  })), 'satellite-mirror');
});

test('packages-md: a missing row and a row for no package', () => {
  const files = baseline();
  only(run({ ...files, 'docs/packages.md': files['docs/packages.md'].replace(/^\| `Bsft.Shop.EFCore`.*\n/m, '') }), 'packages-md');
  only(run({ ...files, 'docs/packages.md': `${files['docs/packages.md']}| \`Bsft.Gone\` | x | x |\n` }), 'packages-md');
});

test('tier-marker: a package whose <Version> names neither tier', () => {
  only(run(withFiles({ 'src/Bsft.Shop.EFCore/Bsft.Shop.EFCore.csproj': versioned('1.0.0') })), 'tier-marker');
  only(run(withFiles({ 'src/Bsft.Shop.EFCore/Bsft.Shop.EFCore.csproj': csproj })), 'tier-marker');
  // A test project or a sample carries no tier of its own.
  assert.deepEqual(run(baseline()).filter((v) => v.rule === 'tier-marker'), []);
});

test('tier-target: a package, a test project or a sample that names its target frameworks', () => {
  only(run(withFiles({ 'src/Bsft.Shop.Abstractions/Bsft.Shop.Abstractions.csproj': foundation('<PropertyGroup><TargetFrameworks>net8.0</TargetFrameworks></PropertyGroup>') })), 'tier-target');
  only(run(withFiles({ 'src/Bsft.Shop/Bsft.Shop.csproj': framework('<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="..\\Bsft.Shop.Abstractions\\Bsft.Shop.Abstractions.csproj" /></ItemGroup>') })), 'tier-target');
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/Bsft.Shop.Tests.csproj': '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>\n' })), 'tier-target');
  assert.deepEqual(run(baseline()).filter((v) => v.rule === 'tier-target'), []);
});

test('tier-reference: a Foundation package referencing a framework package; the reverse passes', () => {
  const upward = withFiles({
    'src/Bsft.Shop.Abstractions/Bsft.Shop.Abstractions.csproj': foundation('<ItemGroup><ProjectReference Include="..\\Bsft.Shop.EFCore\\Bsft.Shop.EFCore.csproj" /></ItemGroup>'),
  });
  // Without the manifest, so that only the package rule speaks.
  only(run(upward, { foundation: null }), 'tier-reference');
  // The baseline has Bsft.Shop (framework) referencing Bsft.Shop.Abstractions (Foundation).
  assert.deepEqual(run(baseline()).filter((v) => v.rule === 'tier-reference'), []);
});

test('foundation-solution: a missing member, a framework member, an entry of no project, and a reference out of it', () => {
  const files = baseline();
  const onlyAbstractions = { 'src/Bsft.Shop.Abstractions/Bsft.Shop.Abstractions.csproj': files['src/Bsft.Shop.Abstractions/Bsft.Shop.Abstractions.csproj'] };
  // The Foundation package's test project is missing.
  only(run(files, { foundation: solutionFor(onlyAbstractions) }), 'foundation-solution');
  // A framework package and its test project are listed.
  only(run(files, { foundation: solutionFor({ ...files, 'docs/packages.md': undefined }) }), 'foundation-solution');
  // An entry that is no project.
  only(run(files, { foundation: `${foundationSolutionFor(files)}${solutionFor({ 'src/Bsft.Gone/Bsft.Gone.csproj': '' }).split('\n').slice(1).join('\n')}` }), 'foundation-solution');
  // A Foundation test project reaching past the manifest to a framework package.
  only(run(withFiles({
    'tests/Bsft.Shop.Abstractions.Tests/Bsft.Shop.Abstractions.Tests.csproj': '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="..\\..\\src\\Bsft.Shop\\Bsft.Shop.csproj" /></ItemGroup></Project>\n',
  })), 'foundation-solution');
});

test('one-type-per-file: two types, and a file not named after its type; partial parts pass', () => {
  only(run(withFiles({ 'src/Bsft.Shop/ShopDefaults.cs': type('Bsft.Shop', 'public static class ShopDefaults { }\n\npublic enum ShopMode { A }') })), 'one-type-per-file');
  only(run(withFiles({ 'src/Bsft.Shop/ShopDefaults.cs': type('Bsft.Shop', 'public static class ShopConstants { }') })), 'one-type-per-file');
  assert.deepEqual(run(withFiles({ 'src/Bsft.Shop/ShopDefaults.Routes.cs': type('Bsft.Shop', 'public static partial class ShopDefaults { }') })), []);
});

// --- graph checks --------------------------------------------------------------------------------

const edge = (from, to) => ({ sourceId: from, targetId: to, weight: 1 });
const graphOf = (...edges) => ({
  nodes: [...new Set(edges.flatMap((e) => [e.sourceId, e.targetId]))].map((id) => ({ id })),
  edges,
});

test('root-sink: the root depends on a feature of its own package, not on Diagnostics', () => {
  only(run(baseline(), { graph: graphOf(edge('Bsft.Shop|Bsft.Shop', 'Bsft.Shop|Bsft.Shop.Ordering')) }), 'root-sink');
  // The same namespace reached in the family's .Abstractions assembly counts too.
  only(run(baseline(), { graph: graphOf(edge('Bsft.Shop|Bsft.Shop', 'Bsft.Shop.Abstractions|Bsft.Shop.Payments')) }), 'root-sink');
  assert.deepEqual(run(baseline(), { graph: graphOf(edge('Bsft.Shop|Bsft.Shop', 'Bsft.Shop|Bsft.Shop.Diagnostics')) }), []);
  // Another package's namespace is not a feature of this one.
  assert.deepEqual(run(baseline(), { graph: graphOf(edge('Bsft.Shop.EFCore|Bsft.Shop.EFCore', 'Bsft.Shop|Bsft.Shop.Ordering')) }), []);
});

test('diagnostics-sink: Diagnostics depends on a feature', () => {
  only(run(baseline(), { graph: graphOf(edge('Bsft.Shop|Bsft.Shop.Diagnostics', 'Bsft.Shop|Bsft.Shop.Ordering')) }), 'diagnostics-sink');
});

test('di-direction: a feature depends on DependencyInjection; the host\'s global namespace may', () => {
  only(run(baseline(), { graph: graphOf(edge('Bsft.Shop|Bsft.Shop.Ordering', 'Bsft.Shop|Bsft.Shop.DependencyInjection')) }), 'di-direction');
  assert.deepEqual(run(baseline(), { graph: graphOf(edge('Bsft.Shop|<global namespace>', 'Bsft.Shop|Bsft.Shop.DependencyInjection')) }), []);
});

test('cycles: two features that need each other', () => {
  only(run(baseline(), {
    graph: graphOf(edge('Bsft.Shop|Bsft.Shop.Ordering', 'Bsft.Shop|Bsft.Shop.Payments'), edge('Bsft.Shop|Bsft.Shop.Payments', 'Bsft.Shop|Bsft.Shop.Ordering')),
  }), 'cycles');
});

test('stronglyConnected finds each component once and ignores self-loops', () => {
  const components = stronglyConnected(['a', 'b', 'c', 'd', 'e'], [['a', 'b'], ['b', 'c'], ['c', 'a'], ['d', 'd'], ['d', 'e'], ['e', 'a']]);
  assert.deepEqual(components, [['a', 'b', 'c']]);
});

// --- naming (docs/naming.md) ----------------------------------------------------------------------

const shopType = (name, decl) => ({ [`src/Bsft.Shop/Ordering/${name}.cs`]: type('Bsft.Shop.Ordering', decl) });

test('name-acronym: a two-letter acronym written Pascal, in a type and in a folder', () => {
  only(run(withFiles(shopType('OrderingAiScore', 'internal sealed class OrderingAiScore { }'))), 'name-acronym');
  only(run(withFiles({ 'src/Bsft.Shop.EFCore/Ordering/EfCoreOrderArchive.cs': type('Bsft.Shop.EFCore.Ordering', 'internal sealed class EfCoreOrderArchive { }') })), 'name-acronym');
  only(run(withFiles({
    'src/Bsft.Shop/Ordering/Ui/OrderingPanel.cs': type('Bsft.Shop.Ordering.Ui', 'internal sealed class OrderingPanel { }'),
    'src/Bsft.Shop/Ordering/Ui/OrderingPanelLayout.cs': type('Bsft.Shop.Ordering.Ui', 'internal sealed class OrderingPanelLayout { }'),
  })), 'name-acronym');
  // Written as the rule writes them, the acronyms pass.
  assert.deepEqual(run(withFiles(shopType('OrderingAIScore', 'internal sealed class OrderingAIScore { }'))), []);
});

test('name-abbreviation: a banned abbreviation; EventArgs and whole words pass', () => {
  only(run(withFiles(shopType('OrderingConfig', 'internal sealed class OrderingConfig { }'))), 'name-abbreviation');
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/Ordering/Fakes/FakeOrgDirectory.cs': type('Bsft.Shop.Tests.Ordering.Fakes', 'internal sealed class FakeOrgDirectory { }') })), 'name-abbreviation');
  assert.deepEqual(run(withFiles(shopType('OrderingChangedEventArgs', 'internal sealed class OrderingChangedEventArgs { }'))), []);
  assert.deepEqual(run(withFiles(shopType('OrderingRefresh', 'internal sealed class OrderingRefresh { }'))), []);
});

test('name-spelling: British spelling, an open compound, and Log In as two words', () => {
  only(run(withFiles(shopType('OrderCancelled', 'internal sealed record OrderCancelled;'))), 'name-spelling');
  only(run(withFiles(shopType('OrderingUsername', 'internal sealed record OrderingUsername;'))), 'name-spelling');
  only(run(withFiles(shopType('OrderingLogInRequest', 'internal sealed record OrderingLogInRequest;'))), 'name-spelling');
  assert.deepEqual(run(withFiles(shopType('OrderingLoginRequest', 'internal sealed record OrderingLoginRequest;'))), []);
});

test('name-type-suffix: banned suffixes by kind, and the Prune vocabulary; documented exceptions pass', () => {
  only(run(withFiles(shopType('OrderingHelper', 'internal static class OrderingHelper { }'))), 'name-type-suffix');
  only(run(withFiles(shopType('OrderingManager', 'internal sealed class OrderingManager { }'))), 'name-type-suffix');
  only(run(withFiles(shopType('OrderingFlags', '[Flags]\ninternal enum OrderingFlags { None = 0 }'))), 'name-type-suffix');
  only(run(withFiles({ 'src/Bsft.Shop.EFCore/Ordering/EFCoreOrderSweeper.cs': null, 'src/Bsft.Shop.EFCore/Ordering/EFCoreOrderPruner.cs': type('Bsft.Shop.EFCore.Ordering', 'internal sealed class EFCoreOrderPruner { }') })), 'name-type-suffix');
  // A class may be called ...Flag (a feature flag is a thing); only an enum may not.
  assert.deepEqual(run(withFiles(shopType('OrderingFlag', 'internal sealed class OrderingFlag { }'))), []);
  assert.deepEqual(run(withFiles(shopType('IFeatureManager', 'public interface IFeatureManager { }'))), []);
  // Tests are not the vocabulary: a test double may say what it pleases about itself.
  assert.deepEqual(run(withFiles({ 'tests/Bsft.Shop.Tests/Ordering/Fakes/FakeOrderManager.cs': type('Bsft.Shop.Tests.Ordering.Fakes', 'internal sealed class FakeOrderManager { }') })), []);
});

test('name-options-validator: an IValidateOptions<> implementation not named Validator', () => {
  only(run(withFiles({ 'src/Bsft.Shop/BsftShopOptionsValidation.cs': type('Bsft.Shop', 'internal sealed class BsftShopOptionsValidation : IValidateOptions<BsftShopOptions> { }') })), 'name-options-validator');
  assert.deepEqual(run(withFiles({ 'src/Bsft.Shop/BsftShopOptionsValidator.cs': type('Bsft.Shop', 'internal sealed class BsftShopOptionsValidator : IValidateOptions<BsftShopOptions> { }') })), []);
});

test('name-hosted-service: a hosted service named Service or Job; HostedService and Worker pass', () => {
  only(run(withFiles(shopType('OrderingExpiryService', 'internal sealed class OrderingExpiryService : BackgroundService { }'))), 'name-hosted-service');
  only(run(withFiles(shopType('OrderingExpiryJob', 'internal sealed class OrderingExpiryJob : Microsoft.Extensions.Hosting.IHostedService { }'))), 'name-hosted-service');
  assert.deepEqual(run(withFiles(shopType('OrderingExpiryWorker', 'internal sealed class OrderingExpiryWorker : BackgroundService { }'))), []);
  assert.deepEqual(run(withFiles(shopType('OrderingHostedService', 'internal sealed class OrderingHostedService : IHostedService { }'))), []);
});

test('name-duplicate: two public types of one name and arity in src; another arity passes', () => {
  only(run(withFiles({ 'src/Bsft.Shop/Payments/OrderPlaced.cs': type('Bsft.Shop.Payments', 'public sealed record OrderPlaced(int Id);') })), 'name-duplicate');
  assert.deepEqual(run(withFiles({ 'src/Bsft.Shop/Payments/OrderPlaced.cs': type('Bsft.Shop.Payments', 'public sealed record OrderPlaced<T>(T Id);') })), []);
  assert.deepEqual(run(withFiles({ 'src/Bsft.Shop/Payments/OrderPlaced.cs': type('Bsft.Shop.Payments', 'internal sealed record OrderPlaced(int Id);') })), []);
});

test('name-namespace-type: a type named like a segment of its namespace', () => {
  only(run(withFiles({ 'src/Bsft.Shop/Shop.cs': type('Bsft.Shop', 'public static class Shop { }') })), 'name-namespace-type');
});

test('name-parameter: a banned parameter name of a declared method; a lambda\'s passes', () => {
  only(run(withFiles(shopType('OrderingService', 'internal sealed class OrderingService\n{\n    public Task RunAsync(int id, CancellationToken ct = default) => Task.CompletedTask;\n}'))), 'name-parameter');
  only(run(withFiles(shopType('OrderingService', 'internal sealed class OrderingService\n{\n    public OrderingService(TimeProvider clock) { }\n}'))), 'name-parameter');
  assert.deepEqual(run(withFiles(shopType('OrderingService', 'internal sealed class OrderingService\n{\n    private readonly Func<CancellationToken, Task> _run = (CancellationToken ct) => Task.CompletedTask;\n    public void Map(IEndpointRouteBuilder routes) => routes.MapGet("/", (HttpContext http) => Results.Ok());\n    public Task RunAsync(CancellationToken cancellationToken) => _run(cancellationToken);\n}'))), []);
});

const shopCode = (body) => shopType('OrderingEndpoints', `internal static class OrderingEndpoints\n{\n${body}\n}`);

test('name-endpoint-name: WithName with a banned token; the canonical name and an interpolated one pass', () => {
  only(run(withFiles(shopCode('    public static void Map(IEndpointRouteBuilder e) => e.MapGet("/orders", () => 1).WithName("ListOrgOrders");'))), 'name-endpoint-name');
  only(run(withFiles(shopCode('    public static void Map(IEndpointRouteBuilder e) => e.MapGet("/orders", () => 1).WithName("ListAiOrders");'))), 'name-endpoint-name');
  assert.deepEqual(run(withFiles(shopCode('    public static void Map(IEndpointRouteBuilder e, string layer) => e.MapGet("/orders", () => 1).WithName("ListOrganizationOrders").WithName($"Get{layer}Order");'))), []);
});

test('name-route: a banned segment, closed form or parameter; lowercase acronyms and Microsoft defaults pass', () => {
  only(run(withFiles(shopCode('    public static void Map(IEndpointRouteBuilder e, string p) => e.MapGet($"{p}/{{orgUnitId:guid}}/orders", () => 1);'))), 'name-route');
  only(run(withFiles(shopCode('    public const string RoutePrefix = "/api/orgunits";'))), 'name-route');
  only(run(withFiles(shopCode('    [HttpGet("api/order-configs")]\n    public static int List() => 1;'))), 'name-route');
  assert.deepEqual(run(withFiles(shopCode('    public const string RoutePrefix = "/api/ai/organization-units";\n    public const string CallbackPath = "/signin-oidc";\n    public static void Map(IEndpointRouteBuilder e) => e.MapGet("/{organizationUnitId:guid}/orders/{*path}", () => 1);'))), []);
});

test('name-table: ToTable, HasDatabaseName and index constants; AI tables pass', () => {
  only(run(withFiles(shopCode('    public static void Configure(EntityTypeBuilder<Order> b) => b.ToTable("OrgOrders");'))), 'name-table');
  only(run(withFiles(shopCode('    public const string OrderIndexName = "IX_AiOrders_PlacedAt";'))), 'name-table');
  only(run(withFiles(shopCode('    public static void Configure(IndexBuilder b) => b.HasDatabaseName("IX_Orders_DedupKey");'))), 'name-table');
  assert.deepEqual(run(withFiles(shopCode('    public const string OrderIndexName = "IX_AIOrders_PlacedAt";\n    public static void Configure(EntityTypeBuilder<Order> b) => b.ToTable("OrganizationOrders");'))), []);
});

test('name-query-key: a bound key with a banned token; canonical keys pass', () => {
  only(run(withFiles(shopType('OrderingQueryParameters', 'public sealed record OrderingQueryParameters(\n    [property: FromQuery(Name = "dedupKey")] string? DeduplicationKey = null);'))), 'name-query-key');
  only(run(withFiles(shopType('OrderingQueryParameters', 'public sealed record OrderingQueryParameters(\n    [property: FromHeader(Name = "X-Org-Id")] string? Unit = null);'))), 'name-query-key');
  assert.deepEqual(run(withFiles(shopType('OrderingQueryParameters', 'public sealed record OrderingQueryParameters(\n    [property: FromQuery(Name = "organizationUnitId")] Guid? OrganizationUnitId = null,\n    [property: FromQuery(Name = "pageSize")] int? PageSize = null);'))), []);
});

test('name-log-placeholder: a placeholder with a banned token, in a call and in [LoggerMessage]; prose and holes pass', () => {
  only(run(withFiles(shopCode('    public static void Log(ILogger logger, int n) => logger.LogInformation("Pruned {PrunedCount} orders.", n);'))), 'name-log-placeholder');
  only(run(withFiles(shopCode('    [LoggerMessage(Level = LogLevel.Warning, Message = "Turn {ChatAiTurnNumber} failed.")]\n    public static partial void TurnFailed(ILogger logger, int turn);'))), 'name-log-placeholder');
  // The words around a placeholder are prose, and an interpolation hole is code, not a log property.
  assert.deepEqual(run(withFiles(shopCode('    public static void Log(ILogger logger, int n, string org) => logger.LogInformation($"Purged {n} orders of the org {org}; {{PurgedCount}}", n);'))), []);
});

test('name-contract-constant: permission, section, claim type and audit names; canonical values pass', () => {
  only(run(withFiles(shopCode('    public const string ManagePermission = "orgunits.manage";'))), 'name-contract-constant');
  only(run(withFiles(shopCode('    public const string SectionName = "Bsft:Shop:InboundDedup";'))), 'name-contract-constant');
  only(run(withFiles(shopCode('    internal const string EntityType = "AiOrder";'))), 'name-contract-constant');
  assert.deepEqual(run(withFiles(shopCode('    public const string ManagePermission = "organization-units.manage";\n    public const string ClaimType = "bsft:organization";\n    internal const string UsageSource = "ai-usage";\n    internal const string EntityType = "AIOrder";\n    internal const string Description = "org units, dedup";'))), []);
});

test('name-test-class: a test class with an underscore, and one not ending in Tests', () => {
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/Ordering/OrderServiceTests.cs': type('Bsft.Shop.Tests.Ordering', 'public sealed class OrderServiceTests { }'),
    'tests/Bsft.Shop.Tests/Ordering/OrderService_Placing_Tests.cs': type('Bsft.Shop.Tests.Ordering', 'public sealed class OrderService_Placing_Tests\n{\n    [Fact]\n    public void Places() { }\n}') })), 'name-test-class');
  only(run(withFiles({ 'tests/Bsft.Shop.Tests/Ordering/PlacingAnOrderScenario.cs': type('Bsft.Shop.Tests.Ordering', 'public sealed class PlacingAnOrderScenario\n{\n    [Theory]\n    [InlineData(1)]\n    public void Places(int n) { }\n}') })), 'name-test-class');
  // A helper type without tests is not a test class.
  assert.deepEqual(run(withFiles({ 'tests/Bsft.Shop.Tests/Support/OrderScenario.cs': type('Bsft.Shop.Tests.Support', 'internal sealed class OrderScenario { }') })), []);
});

// --- the C# reader ---------------------------------------------------------------------------------

test('stripTrivia blanks comments and literals but keeps offsets and the code in interpolation holes', () => {
  const source = 'var a = "}{"; // {\nvar b = $"x{Call("}")}y"; /* { */ var c = \'{\';\nvar d = """\n{\n""";\nvar e = @"a""{";';
  const stripped = stripTrivia(source);
  assert.equal(stripped.length, source.length);
  assert.equal((stripped.match(/[{}]/g) ?? []).join(''), '');
  assert.match(stripped, /Call\(/);
});

test('stringLiterals reads values with escapes undone and holes masked, at offsets of the stripped code', () => {
  const source = 'var a = $"{p}/{{id:guid}}/x"; // "no"\nLog("A {B}" + @"d""e"); var q = $"x{Call("in")}y"; var r = """raw""";';
  const literals = stringLiterals(source);
  assert.deepEqual(literals.map((l) => [l.value, l.interpolated]), [
    ['\u0000/{id:guid}/x', true], ['A {B}', false], ['d"e', false], ['x\u0000y', true], ['in', false], ['raw', false],
  ]);
  assert.match(stripTrivia(source).slice(0, literals[1].start), /Log\($/);
});

test('readDeclarations reads top-level types only, with modifiers, spans and entry points', () => {
  const source = [
    'using System;',
    'namespace Bsft.Shop.DependencyInjection;',
    '',
    '/// <summary>Doc with { brace.</summary>',
    '[Obsolete("x { y")]',
    'public static class ShopServiceCollectionExtensions',
    '{',
    '    private sealed class Nested { }',
    '    public static IServiceCollection AddBsftShop(this IServiceCollection services) => services;',
    '    public static T MapBsftShop<T>(this T builder) where T : class => builder;',
    '}',
    '',
    'file sealed record Local(int X);',
    'internal delegate void Callback(int value);',
  ].join('\n');
  const d = readDeclarations(source);
  assert.deepEqual(d.namespaces.map((n) => [n.name, n.fileScoped]), [['Bsft.Shop.DependencyInjection', true]]);
  assert.deepEqual(d.types.map((t) => [t.kind, t.name, t.public]), [['class', 'ShopServiceCollectionExtensions', true], ['record', 'Local', false], ['delegate', 'Callback', false]]);
  assert.deepEqual(d.extensionMethods, [
    { name: 'AddBsftShop', extends: 'IServiceCollection', type: 'ShopServiceCollectionExtensions' },
    { name: 'MapBsftShop', extends: 'T', type: 'ShopServiceCollectionExtensions' },
  ]);
  assert.match(source.slice(d.types[0].start, d.types[0].end), /^\s*\/\/\/ <summary>[\s\S]*\}$/);
});

test('the command line exits 1 on a violation and 0 on a conforming tree', () => {
  const root = mkdtempSync(join(tmpdir(), 'verify-structure-cli-'));
  try {
    const files = baseline();
    for (const [path, content] of Object.entries(files)) {
      mkdirSync(dirname(join(root, path)), { recursive: true });
      writeFileSync(join(root, path), content);
    }
    writeFileSync(join(root, 'Bsft.sln'), solutionFor(files));
    const script = new URL('../verify-structure.mjs', import.meta.url).pathname;
    execFileSync(process.execPath, [script, '--root', root], { stdio: 'pipe' });
    writeFileSync(join(root, 'src/Bsft.Shop/ShopDefaults.cs'), type('Bsft.Wrong', 'public static class ShopDefaults { }'));
    assert.throws(() => execFileSync(process.execPath, [script, '--root', root], { stdio: 'pipe' }), (e) => e.status === 1 && /\[namespace-folder\]/.test(e.stderr.toString()));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
