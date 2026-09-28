// Reads a repository into the model verify-structure.mjs checks (Node 24). No network, no build:
// the model is the files on disk, so a file excluded from compilation is read like any other.

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, relative, sep, basename, dirname } from 'node:path';
import { readDeclarations } from './csharp.mjs';

const SKIP_DIRS = new Set(['bin', 'obj', '.git', '.vs', 'node_modules', 'TestResults']);
export const PROJECT_ROOTS = ['src', 'samples', 'tests'];

const toPosix = (p) => p.split(sep).join('/');

function walk(dir, out = { files: [], dirs: [] }) {
  for (const entry of readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (SKIP_DIRS.has(entry.name) || entry.name.startsWith('.')) continue;
      out.dirs.push(full);
      walk(full, out);
    } else if (entry.isFile()) {
      out.files.push(full);
    }
  }
  return out;
}

// The namespace root section 3 prescribes: the assembly name, without a trailing `.Abstractions`.
export function expectedRootNamespace(assemblyName) {
  return assemblyName.endsWith('.Abstractions') ? assemblyName.slice(0, -'.Abstractions'.length) : assemblyName;
}

function xmlValue(xml, element) {
  const m = new RegExp(`<${element}>\\s*([^<]*?)\\s*</${element}>`).exec(xml);
  return m ? m[1] : null;
}

// PC 1 "Tiers: Foundation and framework": the tier of a package is the version property its project
// file names, and nothing else. A src project naming neither has no tier (null), which the
// tier-marker rule reports.
export const TIER_VERSIONS = {
  foundation: '$(BsftFoundationVersion)',
  framework: '$(BsftVersion)',
};

export function tierOf(version) {
  for (const [tier, marker] of Object.entries(TIER_VERSIONS)) if (version === marker) return tier;
  return null;
}

export function readRepository(root) {
  const projects = [];
  const looseFiles = [];

  for (const top of PROJECT_ROOTS) {
    const topDir = join(root, top);
    if (!existsSync(topDir)) continue;
    const { files } = walk(topDir);
    const csprojs = files.filter((f) => f.endsWith('.csproj'));
    const projectDirs = csprojs.map((p) => dirname(p));

    for (const csproj of csprojs) {
      const dir = dirname(csproj);
      const xml = readFileSync(csproj, 'utf8');
      const name = basename(csproj, '.csproj');
      const assemblyName = xmlValue(xml, 'AssemblyName') ?? name;
      const version = xmlValue(xml, 'Version');
      projects.push({
        name,
        kind: top,
        dir: toPosix(relative(root, dir)),
        csproj: toPosix(relative(root, csproj)),
        assemblyName,
        declaredRootNamespace: xmlValue(xml, 'RootNamespace'),
        version,
        // PC 1: the target frameworks follow the tier and are derived in Directory.Build.props, so a
        // project file that names them is what the tier-target rule reports.
        declaredTargetFrameworks: xmlValue(xml, 'TargetFrameworks') ?? xmlValue(xml, 'TargetFramework'),
        // Only a package has a tier; a sample or a test project belongs to the Foundation by being
        // listed in Bsft.Foundation.sln, which the foundation-solution rule checks.
        tier: top === 'src' ? tierOf(version) : null,
        references: [...xml.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)].map((m) => basename(m[1].replaceAll('\\', '/'), '.csproj')),
        root: expectedRootNamespace(assemblyName),
        files: [],
        folders: new Set(),
      });
    }

    // Every .cs file belongs to the innermost project directory above it; one outside any project
    // (tests/Shared, linked into test projects) is kept apart.
    for (const file of files.filter((f) => f.endsWith('.cs'))) {
      const owner = projectDirs
        .filter((d) => file.startsWith(d + sep))
        .sort((a, b) => b.length - a.length)[0];
      const text = readFileSync(file, 'utf8');
      const decl = readDeclarations(text);
      const entry = { path: toPosix(relative(root, file)), text, ...decl };
      if (owner === undefined) {
        entry.folder = toPosix(relative(join(root, top), dirname(file)));
        entry.top = top;
        looseFiles.push(entry);
        continue;
      }
      const project = projects.find((p) => p.dir === toPosix(relative(root, owner)));
      const folder = toPosix(relative(owner, dirname(file)));
      entry.folder = folder;
      entry.segments = folder === '' ? [] : folder.split('/');
      entry.project = project;
      project.files.push(entry);
      for (let k = 1; k <= entry.segments.length; k++) project.folders.add(entry.segments.slice(0, k).join('/'));
    }
  }

  projects.sort((a, b) => a.dir.localeCompare(b.dir));

  // The template pack (PC 5) is a package without code: its project sits directly under templates/.
  const templatesDir = join(root, 'templates');
  const templatePackages = existsSync(templatesDir)
    ? readdirSync(templatesDir).filter((f) => f.endsWith('.csproj')).map((f) => basename(f, '.csproj')).sort()
    : [];
  return { root, projects, looseFiles, templatePackages };
}

// Solution entries: every project path the .sln names (folders excluded).
export function readSolution(root, slnName) {
  const path = join(root, slnName);
  if (!existsSync(path)) return null;
  const entries = [];
  for (const m of readFileSync(path, 'utf8').matchAll(/^Project\("\{[^}]+\}"\)\s*=\s*"[^"]*",\s*"([^"]+\.csproj)"/gm)) {
    entries.push(m[1].replaceAll('\\', '/'));
  }
  return entries;
}
