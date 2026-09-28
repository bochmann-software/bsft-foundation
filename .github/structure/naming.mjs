// The naming checks of verify-structure.mjs (Node 24). Each check enforces a rule of docs/naming.md
// ("N"), quoted in NAMING_RULES below; nothing here is a rule of its own.
//
// What they read is what the structure model already holds: project names, folder segments (which
// are the namespace segments, because namespace = folder is enforced beside them), the top-level
// types of every file with their declaration header, and the file's code with comments and strings
// blanked. Member names are not read: the model has no member declarations, and a regex over member
// declarations would be a parser this repository does not want. Parameters are read only where the
// question is narrow and the shape is unambiguous (N6.1, a typed parameter of a declared method).
//
// Written-out contracts (N7.4) are string literals, read from the file's text by stringLiterals and
// placed by the stripped code in front of them: a literal that is the direct argument of `WithName`,
// a `Map*` call, `[Route]`/`[Http*]`, `ToTable`/`HasDatabaseName`, `From*(Name = ...)` or a logging
// call, or the value of a `const string` whose name says what it holds. A literal that reaches such a
// call through a variable or a helper of the package's own is not read.
//
// A token is a word of an identifier, split the way docs/naming.md splits it:
// `[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|\d+`. `Refresh` does not contain the token `Ref`.

import { stringLiterals } from './csharp.mjs';

export const NAMING_RULES = {
  'name-acronym': {
    cite: 'naming.md N1.2',
    text: 'A two-letter acronym is all caps (`AI`, `UI`, `IP`, `EF`, `IO`, `MQ`). ... Product names keep their own spelling: `EFCore` from `EF`; `RabbitMQ` ...',
  },
  'name-abbreviation': {
    cite: 'naming.md N2.1',
    text: 'No abbreviations or contractions. ... Banned, with the replacement: `Org`→`Organization`, `Config`/`Cfg`→`Configuration`, `Dedup`→`Deduplication`, `Seq`→`Sequence`, `Ref`→`Reference`, `Stats`→`Statistics`, ...',
  },
  'name-spelling': {
    cite: 'naming.md N1.4, N1.5',
    text: 'Closed compounds are written as the FDG table writes them: `Canceled`, `UserName`, `WhiteSpace`, `SignIn`/`SignOut` ... American English spelling.',
  },
  'name-type-suffix': {
    cite: 'naming.md N4.2, N4.5, N4.6, N5.7',
    text: '`Delegate`, `Enum`, `Ex`, `Flag`/`Flags`, `Impl` and `New` are never used. ... `Manager` is not a role. ... `Helper`, `Util` and `Utils` are banned. ... `Details` is banned (use `Info`). ... `Prune` ... banned.',
  },
  'name-options-validator': {
    cite: 'naming.md N4.5',
    text: '`Validator` implements `IValidateOptions<T>` or validates a value.',
  },
  'name-hosted-service': {
    cite: 'naming.md N4.7',
    text: 'A hosted service is never named `*Job`, nor `*Service` unless it is a `*HostedService`: `Service` is the orchestration role (N4.5) and `Job` is a scheduled `IJob`.',
  },
  'name-duplicate': {
    cite: 'naming.md N2.5',
    text: 'No two public types share a simple name within the framework.',
  },
  'name-namespace-type': {
    cite: 'naming.md N3.4',
    text: 'No namespace segment equals a type name inside it.',
  },
  'name-parameter': {
    cite: 'naming.md N6.1',
    text: 'Canonical parameter names for framework types: `CancellationToken` `cancellationToken`, `IConfiguration` `configuration`, `TimeProvider` `timeProvider`, `ClaimsPrincipal` `principal`, `HttpContext` `httpContext`, `IServiceProvider` `serviceProvider`. Lambda parameters are exempt, except `sp`.',
  },
  'name-endpoint-name': {
    cite: 'naming.md N7.4',
    text: 'In a major release a written-out contract follows the glossary like any identifier: an endpoint name (`WithName`) is PascalCase and uses the canonical terms (N1, N2).',
  },
  'name-route': {
    cite: 'naming.md N7.4',
    text: 'Route segments are lowercase kebab-case and route parameters camelCase, both in the canonical terms; a lowercase word writes an acronym in lowercase (`/api/ai/`).',
  },
  'name-table': {
    cite: 'naming.md N7.4',
    text: 'Table and index names (`ToTable`, `HasDatabaseName`, `*TableName`/`*IndexName` constants) are PascalCase in the canonical terms.',
  },
  'name-query-key': {
    cite: 'naming.md N7.4',
    text: 'A query-string, route or header key bound by name (`[FromQuery(Name = ...)]` and its siblings) uses the canonical terms, camelCase for a query or route key.',
  },
  'name-log-placeholder': {
    cite: 'naming.md N7.4',
    text: 'A placeholder of a log message template is a log property: PascalCase in the canonical terms.',
  },
  'name-contract-constant': {
    cite: 'naming.md N7.4',
    text: 'A constant that holds a permission string, an error code, a configuration section, a claim type, an audit entity type or action, a scope kind or a route name uses the canonical terms in the casing of its kind.',
  },
  'name-test-class': {
    cite: 'naming.md N8.1',
    text: 'A test class is `<TypeUnderTest>Tests` or `<TypeUnderTest><Aspect>Tests`, with no underscore.',
  },
};

// N1.2: the two-letter acronyms the framework writes, as they must not be written.
const PASCAL_ACRONYMS = new Set(['Ai', 'Ui', 'Ip', 'Ef', 'Mq', 'Io']);

// N2.1: the banned abbreviations, each with its replacement.
const ABBREVIATIONS = new Map([
  ['Org', 'Organization'], ['Config', 'Configuration'], ['Cfg', 'Configuration'],
  ['Dedup', 'Deduplication'], ['Dedupe', 'Deduplication'], ['Seq', 'Sequence'], ['Ref', 'Reference'],
  ['Stats', 'Statistics'], ['Dest', 'Destination'], ['Src', 'Source'], ['Param', 'Parameter'],
  ['Params', 'Parameters'], ['Arg', 'Argument'], ['Args', 'Arguments'], ['Expr', 'Expression'],
  ['Ctor', 'Constructor'], ['Dir', 'Directory or Direction'], ['Cred', 'Credential'], ['Msg', 'Message'],
  ['Ctx', 'Context'], ['Mgr', 'Manager'], ['Spec', 'Specification or Request'],
]);

// N1.4 and N1.5: spellings that have one canonical form.
const SPELLINGS = new Map([
  ['Cancelled', 'Canceled'], ['Cancelling', 'Canceling'], ['Canceller', 'Canceler'],
  ['Username', 'UserName'], ['Whitespace', 'WhiteSpace'], ['Signin', 'SignIn'], ['Signout', 'SignOut'],
  ['Behaviour', 'Behavior'], ['Behaviours', 'Behaviors'], ['Honoured', 'Honored'], ['Labelled', 'Labeled'],
]);

// N4.2, N4.5, N4.6 and N5.7: suffixes (and one vocabulary token) a type in shipped code never ends in
// or carries, by the kind of type CA1711 reserves them for. Checked on src/ and samples/; a test's
// own helper types are not a vocabulary.
const BANNED_SUFFIXES = {
  any: ['Impl', 'Helper', 'Helpers', 'Util', 'Utils', 'Utility', 'Manager', 'StartupCheck', 'Details', 'Ex', 'New'],
  enum: ['Enum', 'Flag', 'Flags'],
  delegate: ['Delegate'],
};
const BANNED_TYPE_TOKENS = new Map([['Prune', 'Purge'], ['Pruner', 'Sweeper'], ['Pruning', 'Purging']]);

// The documented exceptions, each with its reason (docs/naming.md says where it is written down).
const SUFFIX_EXCEPTIONS = new Map([
  // N4.5: the one Manager, mirroring Microsoft.FeatureManagement's IFeatureManager.
  ['IFeatureManager', 'mirrors Microsoft.FeatureManagement'],
  ['FeatureManager', 'mirrors Microsoft.FeatureManagement'],
  // N2.3: the WebAuthn specification names the byte "flags" (authenticator data, section 6.1).
  ['AuthenticatorDataFlags', 'WebAuthn names it'],
]);

// N6.1: the banned name of a parameter of each framework type.
const PARAMETER_NAMES = new Map([
  ['CancellationToken', { name: 'cancellationToken', banned: ['ct', 'token', 'cancel', 'cancellation'] }],
  ['IConfiguration', { name: 'configuration', banned: ['config', 'cfg'] }],
  ['TimeProvider', { name: 'timeProvider', banned: ['time', 'clock'] }],
  ['ClaimsPrincipal', { name: 'principal', banned: ['user'] }],
  ['HttpContext', { name: 'httpContext', banned: ['http', 'ctx'] }],
  ['IServiceProvider', { name: 'serviceProvider', banned: ['sp'] }],
]);

const TOKEN = /[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+|\d+/g;
export const tokens = (identifier) => identifier.match(TOKEN) ?? [];

// The findings of the three token rules for one PascalCase name (a project segment, a folder, a
// type). Returns [rule, message] pairs.
export function tokenFindings(name) {
  const found = [];
  const words = tokens(name);
  for (let k = 0; k < words.length; k++) {
    const w = words[k];
    if (PASCAL_ACRONYMS.has(w)) found.push(['name-acronym', `"${name}" writes the acronym ${w} as ${w.toUpperCase()}.`]);
    // `EventArgs` is the suffix CA1711 prescribes for an EventArgs subclass, not an abbreviation.
    if (ABBREVIATIONS.has(w) && !(w === 'Args' && words[k - 1] === 'Event')) {
      found.push(['name-abbreviation', `"${name}" abbreviates ${ABBREVIATIONS.get(w)} as ${w}.`]);
    }
    if (SPELLINGS.has(w)) found.push(['name-spelling', `"${name}" spells ${w}; the canonical form is ${SPELLINGS.get(w)}.`]);
    if (w === 'Log' && (words[k + 1] === 'In' || words[k + 1] === 'Out')) {
      found.push(['name-spelling', `"${name}" writes Log${words[k + 1]} as two words; it is Log${words[k + 1].toLowerCase()} (or SignIn/SignOut).`]);
    }
  }
  return found;
}

// The type arity a declaration header states: `record RankedItem<T>` has one.
function arity(type) {
  const m = new RegExp(`\\b${type.name}\\s*<`).exec(type.header);
  if (!m) return 0;
  let depth = 0;
  let commas = 0;
  for (let k = m.index + m[0].length - 1; k < type.header.length; k++) {
    const c = type.header[k];
    if (c === '<') depth++;
    else if (c === '>') { depth--; if (depth === 0) break; }
    else if (c === ',' && depth === 1) commas++;
  }
  return commas + 1;
}

// The base list of a declaration header: what follows the `:` after the name and its parameters.
function baseList(type) {
  const header = type.header;
  let depth = 0;
  for (let k = header.indexOf(type.name) + type.name.length; k < header.length; k++) {
    const c = header[k];
    if (c === '<' || c === '(') depth++;
    else if (c === '>' || c === ')') depth--;
    else if (c === ':' && depth === 0) return header.slice(k + 1).replace(/\bwhere\b[\s\S]*$/, '');
  }
  return '';
}

const HOSTED = /(?:^|[\s,.])(?:BackgroundService|IHostedService|IHostedLifecycleService)\b/;
const OPTIONS_VALIDATOR = /(?:^|[\s,.])IValidateOptions\s*</;

// Words that can precede `(` without the parentheses being a parameter list of a declaration.
const NOT_A_DECLARATION = new Set([
  'if', 'while', 'for', 'foreach', 'switch', 'catch', 'using', 'lock', 'return', 'await', 'async',
  'static', 'when', 'nameof', 'typeof', 'sizeof', 'default', 'new', 'base', 'this', 'fixed',
  'checked', 'unchecked', 'in', 'is', 'and', 'or', 'not', 'with', 'throw', 'yield', 'else', 'do',
  'params', 'out', 'ref', 'delegate', 'sealed', 'var', 'select', 'where', 'let', 'on', 'equals',
]);

// Typed parameters of the parameter lists of declared methods, constructors and local functions:
// the `(` follows an identifier (possibly a generic one). A lambda's list follows `(`, `,`, `=` or a
// keyword and is not read; a call has no typed arguments, so it never matches.
export function declaredParameters(code) {
  const found = [];
  for (let i = 0; i < code.length; i++) {
    if (code[i] !== '(') continue;
    let j = i - 1;
    while (j >= 0 && /\s/.test(code[j])) j--;
    if (code[j] === '>') {
      let depth = 0;
      for (; j >= 0; j--) {
        if (code[j] === '>') depth++;
        else if (code[j] === '<') { depth--; if (depth === 0) { j--; break; } }
      }
      while (j >= 0 && /\s/.test(code[j])) j--;
    }
    let k = j;
    while (k >= 0 && /\w/.test(code[k])) k--;
    const before = code.slice(k + 1, j + 1);
    if (!/^[A-Za-z_]\w*$/.test(before) || NOT_A_DECLARATION.has(before)) continue;
    let depth = 0;
    let end = i;
    for (; end < code.length; end++) {
      if (code[end] === '(') depth++;
      else if (code[end] === ')') { depth--; if (depth === 0) break; }
    }
    const inner = code.slice(i + 1, end);
    const parts = [];
    let from = 0;
    depth = 0;
    for (let q = 0; q < inner.length; q++) {
      const c = inner[q];
      if ('([{<'.includes(c)) depth++;
      else if (')]}>'.includes(c)) depth--;
      else if (c === ',' && depth === 0) { parts.push(inner.slice(from, q)); from = q + 1; }
    }
    parts.push(inner.slice(from));
    for (const part of parts) {
      const m = /^\s*(?:\[[^\]]*\]\s*)*(?:(?:this|ref|out|in|params|scoped)\s+)*([\w.]+)\??\s+([A-Za-z_]\w*)\s*(?:=[\s\S]*)?$/.exec(part);
      if (m) found.push({ method: before, type: m[1].split('.').at(-1), name: m[2], offset: i });
    }
  }
  return found;
}

const lineOf = (code, offset) => code.slice(0, offset).split('\n').length;

// --- written-out contracts (N7.4) ------------------------------------------------------------------

// The contract vocabulary adds the Prune family (N5.7) to what tokenFindings reports for a type.
const CONTRACT_TOKENS = new Map([...BANNED_TYPE_TOKENS, ['Pruned', 'Purged'], ['Prunes', 'Purges']]);

// Lowercase closed forms that no word split finds: `orgunits` is not the token `Org`.
const LOWER_CLOSED = new Map([
  ['orgunit', 'organization-unit'], ['orgunits', 'organization-units'], ['orgs', 'organizations'],
  ['configs', 'configurations'], ['specs', 'specifications'],
]);

// Written-out names a standard or Microsoft fixes (N2.3), exempt as whole values or segments.
const FIXED_CONTRACT_WORDS = new Set([
  // Microsoft.AspNetCore.Authentication.OpenIdConnect's default callback paths.
  'signin-oidc', 'signout-oidc', 'signout-callback-oidc',
]);

const capitalize = (word) => word.charAt(0).toUpperCase() + word.slice(1);

// The findings for one word of a written-out name: a PascalCase or camelCase identifier, or one
// lowercase word of a kebab, dotted or snake name. A lowercase word writes an acronym in lowercase
// (N1.2), so `ai` is not a finding; `Ai` inside a Pascal or camel word is.
export function contractWordFindings(word) {
  if (!word || !/[A-Za-z]/.test(word) || /^[A-Z0-9]+$/.test(word)) return [];
  const lower = /^[a-z0-9]+$/.test(word);
  if (lower && LOWER_CLOSED.has(word)) return [`"${word}" is written ${LOWER_CLOSED.get(word)}.`];
  const found = tokenFindings(capitalize(word))
    .filter(([rule]) => !(lower && rule === 'name-acronym'))
    .map(([, message]) => message);
  for (const w of tokens(capitalize(word))) {
    if (CONTRACT_TOKENS.has(w)) found.push(`"${word}" says ${w}; the vocabulary is ${CONTRACT_TOKENS.get(w)}.`);
  }
  return found;
}

// The findings for a whole written-out value, split into words at every separator a contract uses.
export function contractValueFindings(value, separators = /[\s.:_\-\/]+/) {
  const found = [];
  for (const word of value.split(separators)) found.push(...contractWordFindings(word.replaceAll('\u0000', '')));
  return found;
}

// The findings for a route template: literal segments are words, `{name:constraint}` a camelCase key.
export function routeFindings(route) {
  const found = [];
  for (const segment of route.split('/')) {
    if (segment === '' || segment.includes('\u0000') && !/[A-Za-z]/.test(segment.replaceAll('\u0000', ''))) continue;
    for (const part of segment.split('\u0000')) {
      if (FIXED_CONTRACT_WORDS.has(part)) continue;
      const parameter = /^\{\*{0,2}([A-Za-z_]\w*)[^}]*\}$/.exec(part);
      if (parameter) { found.push(...contractWordFindings(parameter[1])); continue; }
      found.push(...contractValueFindings(part.replace(/\{[^}]*\}/g, ' '), /[\s.\-~]+/));
    }
  }
  return found;
}

// The call or attribute a literal is an argument of, read backwards through the stripped code:
// { name, index, argument } where `argument` is a named argument (`Name = `) or null.
export function enclosingCall(code, offset) {
  let depth = 0;
  let index = 0;
  let j = offset - 1;
  const argumentStart = j;
  for (; j >= 0; j--) {
    const c = code[j];
    if (c === ')' || c === ']') depth++;
    else if (c === '(' || c === '[') {
      if (depth === 0) break;
      depth--;
    } else if (depth === 0 && c === ',') index++;
    else if (depth === 0 && (c === ';' || c === '{' || c === '}')) return null;
  }
  if (j < 0 || code[j] !== '(') return null;
  const inside = code.slice(j + 1, argumentStart + 1);
  const named = /(?:^|,)\s*([A-Za-z_]\w*)\s*=\s*[^,]*$/.exec(inside);
  let k = j - 1;
  while (k >= 0 && /\s/.test(code[k])) k--;
  if (code[k] === '>') {
    let d = 0;
    for (; k >= 0; k--) {
      if (code[k] === '>') d++;
      else if (code[k] === '<') { d--; if (d === 0) { k--; break; } }
    }
  }
  const end = k + 1;
  while (k >= 0 && /\w/.test(code[k])) k--;
  return { name: code.slice(k + 1, end), index, argument: named ? named[1] : null };
}

const MAP_CALL = /^Map(?:Get|Post|Put|Patch|Delete|Methods|Group|Fallback|Hub|HealthChecks)?$/;
const ROUTE_ATTRIBUTE = /^(?:Route|Http(?:Get|Post|Put|Patch|Delete|Head|Options))$/;
const LOG_CALL = /^(?:Log(?:Trace|Debug|Information|Warning|Error|Critical)?|BeginScope|Define|DefineScope|LoggerMessage)$/;
const ROUTE_CONSTANT = /(?:Route|RoutePrefix|Prefix|Path|Pattern|Template)$/;
const TABLE_CONSTANT = /(?:TableName|IndexName)$/;
const CONTRACT_CONSTANT = /(?:Permission|ErrorCode|ErrorKind|SectionName|ClaimType|EntityType|EntityName|Entity|AuditAction|ScopeKind|RouteName|MetadataKey|Metadata)$/;
const PLACEHOLDER = /\{@?([A-Za-z_]\w*)(?:[,:][^{}]*)?\}/g;

// Reads the written-out contracts of one file of shipped code and reports each word that breaks
// N7.4, naming the rule of the contract's kind.
export function contractFindings(file) {
  const found = [];
  for (const literal of stringLiterals(file.text)) {
    const before = file.code.slice(Math.max(0, literal.start - 200), literal.start);
    const constant = /\bconst\s+string\s+([A-Za-z_]\w*)\s*=\s*$/.exec(before);
    const call = constant ? null : enclosingCall(file.code, literal.start);
    const line = lineOf(file.code, literal.start);
    const add = (rule, what, messages) => {
      for (const message of messages) found.push({ rule, line, message: `${what} "${literal.value.replaceAll('\u0000', '{…}')}": ${message}` });
    };

    if (constant) {
      const name = constant[1];
      if (ROUTE_CONSTANT.test(name) && literal.value.includes('/')) add('name-route', `route constant ${name}`, routeFindings(literal.value));
      else if (TABLE_CONSTANT.test(name)) add('name-table', `constant ${name}`, contractValueFindings(literal.value, /_+/));
      else if (CONTRACT_CONSTANT.test(name) && !FIXED_CONTRACT_WORDS.has(literal.value)) add('name-contract-constant', `constant ${name}`, contractValueFindings(literal.value));
      continue;
    }
    if (!call) continue;
    if (call.name === 'WithName' && call.index === 0 && !literal.interpolated) add('name-endpoint-name', 'endpoint name', contractWordFindings(literal.value));
    else if (MAP_CALL.test(call.name) && call.index === 0) add('name-route', `route of ${call.name}`, routeFindings(literal.value));
    else if (ROUTE_ATTRIBUTE.test(call.name) && (call.index === 0 && !call.argument || call.argument === 'Template')) add('name-route', `route of [${call.name}]`, routeFindings(literal.value));
    else if ((call.name === 'ToTable' || call.name === 'HasDatabaseName') && call.index === 0) add('name-table', call.name, contractValueFindings(literal.value, /_+/));
    else if (/^From(?:Query|Route|Header|Form)$/.test(call.name) && call.argument === 'Name') add('name-query-key', `[${call.name}] key`, contractValueFindings(literal.value, /-+/));
    else if (LOG_CALL.test(call.name)) {
      for (const m of literal.value.matchAll(PLACEHOLDER)) {
        for (const message of contractWordFindings(m[1])) found.push({ rule: 'name-log-placeholder', line, message: `log property {${m[1]}}: ${message}` });
      }
    }
  }
  return found;
}

export function runNamingChecks(repo, report) {
  const shipped = (p) => p.kind === 'src' || p.kind === 'samples';

  // --- tokens in project names and folders (= namespace segments) ---------------------------------
  for (const p of repo.projects) {
    for (const segment of p.name.split('.')) {
      for (const [rule, message] of tokenFindings(segment)) report(rule, p.csproj, `project ${p.name}: ${message}`);
    }
    for (const folder of p.folders) {
      const name = folder.split('/').at(-1);
      for (const [rule, message] of tokenFindings(name)) report(rule, `${p.dir}/${folder}`, `folder ${message}`);
    }
  }

  const publicByName = new Map();
  for (const p of repo.projects) {
    for (const f of p.files) {
      for (const t of f.types) {
        // --- tokens in type names ------------------------------------------------------------------
        for (const [rule, message] of tokenFindings(t.name)) report(rule, f.path, `${t.kind} ${message}`);

        // --- N3.4: a type named like a segment of its own namespace ------------------------------------
        if (t.namespace && t.namespace.split('.').includes(t.name)) {
          report('name-namespace-type', f.path, `${t.kind} ${t.name} is named like a segment of its namespace ${t.namespace}.`);
        }

        if (!shipped(p)) continue;

        // --- suffixes and vocabulary of shipped types ---------------------------------------------------
        if (!SUFFIX_EXCEPTIONS.has(t.name)) {
          const words = tokens(t.name);
          const suffix = [...BANNED_SUFFIXES.any, ...(BANNED_SUFFIXES[t.kind] ?? [])]
            .find((s) => words.length > tokens(s).length && words.slice(-tokens(s).length).join('') === s);
          if (suffix) report('name-type-suffix', f.path, `${t.kind} ${t.name} ends in ${suffix}.`);
        }
        for (const w of tokens(t.name)) {
          if (BANNED_TYPE_TOKENS.has(w)) report('name-type-suffix', f.path, `${t.kind} ${t.name} says ${w}; the vocabulary is ${BANNED_TYPE_TOKENS.get(w)}.`);
        }

        // --- roles read from the base list ---------------------------------------------------------------
        const bases = baseList(t);
        if (t.kind === 'class' && OPTIONS_VALIDATOR.test(bases) && !t.name.endsWith('Validator')) {
          report('name-options-validator', f.path, `${t.name} implements IValidateOptions<>, so it is a <Options>Validator.`);
        }
        if (t.kind === 'class' && HOSTED.test(bases)
          && (t.name.endsWith('Job') || (t.name.endsWith('Service') && !t.name.endsWith('HostedService')))) {
          report('name-hosted-service', f.path, `${t.name} is a hosted service named like a ${t.name.endsWith('Job') ? 'scheduled job' : 'use-case service'}; it is a Worker, Sweeper or Bootstrapper.`);
        }

        if (p.kind === 'src' && t.public) {
          const key = `${t.name}\`${arity(t)}`;
          if (!publicByName.has(key)) publicByName.set(key, []);
          publicByName.get(key).push({ path: f.path, namespace: t.namespace });
        }
      }

      // --- N7.4: written-out contracts of shipped code ------------------------------------------------
      if (shipped(p)) {
        for (const finding of contractFindings(f)) report(finding.rule, `${f.path}:${finding.line}`, finding.message);
      }

      // --- N6.1: parameters of declared methods in shipped code --------------------------------------
      if (shipped(p)) {
        for (const parameter of declaredParameters(f.code)) {
          const rule = PARAMETER_NAMES.get(parameter.type);
          if (rule && rule.banned.includes(parameter.name)) {
            report('name-parameter', `${f.path}:${lineOf(f.code, parameter.offset)}`, `${parameter.method} names its ${parameter.type} parameter "${parameter.name}"; it is "${rule.name}".`);
          }
        }
      }

      // --- N8.1: test classes -------------------------------------------------------------------------
      if (p.kind === 'tests') {
        for (const t of f.types) {
          if (t.kind !== 'class') continue;
          const body = f.code.slice(t.start, t.end);
          if (!/\[\s*(?:Fact|Theory|SkippableFact|SkippableTheory)\b/.test(body)) continue;
          if (!t.name.endsWith('Tests') || t.name.includes('_')) {
            report('name-test-class', f.path, `test class ${t.name} is not <TypeUnderTest>Tests or <TypeUnderTest><Aspect>Tests without underscores.`);
          }
        }
      }
    }
  }

  // --- N2.5: public types of src/ with the same name and arity in two namespaces ------------------
  for (const [key, declarations] of publicByName) {
    const namespaces = [...new Set(declarations.map((d) => d.namespace))];
    if (namespaces.length > 1) {
      report('name-duplicate', declarations[0].path, `public type ${key.replace(/`0$/, '')} is declared in ${namespaces.length} namespaces: ${namespaces.join(', ')}.`);
    }
  }
}
