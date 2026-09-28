// A deliberately small reader for C# source files (Node 24), used by verify-structure.mjs.
//
// It answers four questions about a file and nothing else: which namespaces it declares, which
// types it declares at the top level (outside any other type), whether each is public or partial,
// and which public extension methods named AddBsft*/MapBsft*/UseBsft* it declares. It is not a
// parser. It is exact for the shapes these questions need because it removes every comment, string
// and character literal first — including interpolated and raw strings, whose braces would
// otherwise throw the nesting depth off — and then reads only the declarations at the depth of the
// namespace.

// Blanks every comment and every string or character literal, keeping the code around them (and the
// code of interpolation holes) intact and every character at its offset: the result has the length
// and the line breaks of the input, so an offset found in it is an offset in the source. Preprocessor
// lines are blanked too: an `#if` branch is still code, and both branches are read as such.
export function stripTrivia(text) {
  const out = text.split('');
  const n = text.length;
  const blank = (from, to) => {
    for (let k = from; k < to && k < n; k++) if (out[k] !== '\n' && out[k] !== '\r') out[k] = ' ';
  };

  // Reads a string literal starting at `start` (at its first prefix character) and returns the index
  // after it. The literal is blanked; its interpolation holes are read as code and left in place.
  function readString(start) {
    let j = start;
    let dollars = 0;
    let verbatim = false;
    while (text[j] === '$' || text[j] === '@') {
      if (text[j] === '$') dollars++;
      else verbatim = true;
      j++;
    }
    let quotes = 0;
    while (text[j + quotes] === '"') quotes++;
    let segment = start;
    if (quotes >= 3) {
      // Raw string literal: three or more quotes, closed by as many.
      j += quotes;
      const close = '"'.repeat(quotes);
      while (j < n) {
        if (text.startsWith(close, j)) { blank(segment, j + quotes); return j + quotes; }
        if (dollars > 0 && text.startsWith('{'.repeat(dollars), j) && text[j + dollars] !== '{') {
          blank(segment, j + dollars);
          j = readHole(j + dollars, dollars);
          segment = j - dollars;
          continue;
        }
        j++;
      }
      blank(segment, j);
      return j;
    }
    j++; // opening quote
    while (j < n) {
      const c = text[j];
      if (verbatim) {
        if (c === '"') {
          if (text[j + 1] === '"') { j += 2; continue; }
          blank(segment, j + 1);
          return j + 1;
        }
      } else {
        if (c === '\\') { j += 2; continue; }
        if (c === '"') { blank(segment, j + 1); return j + 1; }
        if (c === '\n') { blank(segment, j); return j; } // unterminated; do not swallow the file
      }
      if (dollars > 0 && c === '{') {
        if (text[j + 1] === '{') { j += 2; continue; }
        blank(segment, j + 1);
        j = readHole(j + 1, 1);
        segment = j - 1;
        continue;
      }
      j++;
    }
    blank(segment, j);
    return j;
  }

  // Reads the code of an interpolation hole up to and including its closing brace(s).
  function readHole(start, closers) {
    let depth = 0;
    let j = start;
    while (j < n) {
      const c = text[j];
      if (c === '"' || ((c === '$' || c === '@') && /^[$@]*"/.test(text.slice(j, j + 4)))) { j = readString(j); continue; }
      if (c === '\'') { j = readChar(j); continue; }
      if (c === '/' && text[j + 1] === '/') { const e = text.indexOf('\n', j); blank(j, e < 0 ? n : e); j = e < 0 ? n : e; continue; }
      if (c === '/' && text[j + 1] === '*') { const e = text.indexOf('*/', j + 2); const end = e < 0 ? n : e + 2; blank(j, end); j = end; continue; }
      if (c === '(' || c === '[' || c === '{') depth++;
      if (c === ')' || c === ']') depth--;
      if (c === '}') {
        if (depth === 0) return j + closers;
        depth--;
      }
      j++;
    }
    return j;
  }

  function readChar(start) {
    let j = start + 1;
    while (j < n && text[j] !== '\'' && text[j] !== '\n') j += text[j] === '\\' ? 2 : 1;
    blank(start, j + 1);
    return j + 1;
  }

  let i = 0;
  let lineStart = true;
  while (i < n) {
    const c = text[i];
    if (lineStart && c === '#') {
      const e = text.indexOf('\n', i);
      const end = e < 0 ? n : e;
      blank(i, end);
      i = end;
      continue;
    }
    if (c === '\n') { lineStart = true; i++; continue; }
    if (c !== ' ' && c !== '\t' && c !== '\r') lineStart = false;
    if (c === '/' && text[i + 1] === '/') { const e = text.indexOf('\n', i); const end = e < 0 ? n : e; blank(i, end); i = end; continue; }
    if (c === '/' && text[i + 1] === '*') { const e = text.indexOf('*/', i + 2); const end = e < 0 ? n : e + 2; blank(i, end); i = end; continue; }
    if (c === '"' || ((c === '$' || c === '@') && /^[$@]*"/.test(text.slice(i, i + 4)))) { i = readString(i); continue; }
    if (c === '\'') { i = readChar(i); continue; }
    i++;
  }
  return out.join('');
}

const MODIFIERS = new Set([
  'public', 'internal', 'private', 'protected', 'file', 'static', 'sealed', 'abstract', 'partial',
  'readonly', 'ref', 'unsafe', 'new', 'required',
]);

// Removes leading attribute lists (`[Foo(...)]`, possibly several) from a declaration header.
function dropAttributes(header) {
  let s = header.trimStart();
  while (s.startsWith('[')) {
    let depth = 0;
    let k = 0;
    for (; k < s.length; k++) {
      if (s[k] === '[') depth++;
      else if (s[k] === ']') { depth--; if (depth === 0) break; }
    }
    s = s.slice(k + 1).trimStart();
  }
  return s;
}

// Classifies one top-level chunk (the text between two `;`/`{`/`}` at namespace depth).
function classify(header) {
  const s = dropAttributes(header);
  const words = s.split(/\s+/);
  const modifiers = [];
  let k = 0;
  while (k < words.length && MODIFIERS.has(words[k])) modifiers.push(words[k++]);
  const rest = words.slice(k).join(' ');
  let m = /^record\s+(?:class|struct)\s+([A-Za-z_]\w*)/.exec(rest)
    ?? /^(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)/.exec(rest);
  if (m) {
    const kind = /^(\w+)/.exec(rest)[1];
    return { name: m[1], kind, modifiers };
  }
  m = /^delegate\s+[\s\S]*?([A-Za-z_]\w*)\s*(?:<[^()]*>)?\s*\(/.exec(rest);
  if (m) return { name: m[1], kind: 'delegate', modifiers };
  return null;
}

// Reads a file's declarations: { namespaces: [{ name, fileScoped }], types: [...], extensionMethods }.
// Each type carries its span in the source (`start` at the end of the previous declaration, so its
// doc comment and attributes are inside; `end` after its closing brace or semicolon).
export function readDeclarations(text) {
  const code = stripTrivia(text.replace(/^\uFEFF/, ' '));
  const namespaces = [];
  const types = [];
  const extensionMethods = [];

  const nsDepthStack = [0];
  let depth = 0;
  let chunkStart = 0;
  // One entry per open brace: 'ns', a type entry (top-level type body) or 'other'.
  const braces = [];

  // Parentheses and brackets are tracked so that a brace inside an attribute argument or a lambda
  // passed to a call does not end a declaration header early.
  let parens = 0;
  for (let i = 0; i < code.length; i++) {
    const c = code[i];
    if (c === '(' || c === '[') { parens++; continue; }
    if (c === ')' || c === ']') { parens--; continue; }
    if (parens > 0) continue;
    if (c !== '{' && c !== '}' && c !== ';') continue;
    const header = code.slice(chunkStart, i).trim();
    const headerStart = chunkStart;
    chunkStart = i + 1;
    const topLevel = depth === nsDepthStack[nsDepthStack.length - 1] && braces.every((b) => b === 'ns');

    if (c === '}') {
      const open = braces.pop();
      depth--;
      if (open === 'ns') nsDepthStack.pop();
      else if (typeof open === 'object') open.end = i + 1;
      continue;
    }

    let type = null;
    if (topLevel) {
      const ns = /^namespace\s+([A-Za-z_][\w.]*)$/.exec(dropAttributes(header));
      if (ns) {
        const parent = braces.length > 0 ? namespaces.at(-1)?.name : null;
        namespaces.push({ name: parent && c === '{' ? `${parent}.${ns[1]}` : ns[1], fileScoped: c === ';', end: i + 1 });
        if (c === '{') {
          braces.push('ns');
          depth++;
          nsDepthStack.push(depth);
        }
        continue;
      }
      const found = header ? classify(header) : null;
      if (found) {
        type = {
          name: found.name,
          kind: found.kind,
          public: found.modifiers.includes('public'),
          partial: found.modifiers.includes('partial'),
          static: found.modifiers.includes('static'),
          namespace: namespaces.at(-1)?.name ?? null,
          header: dropAttributes(header).replace(/\s+/g, ' '),
          start: headerStart,
          end: i + 1,
        };
        types.push(type);
      }
    }

    if (c === '{') {
      braces.push(type ?? 'other');
      depth++;
    }
  }

  // Public extension methods named like a DI entry point, with the type they extend and the
  // top-level type declaring them. Read from the whole stripped text: the method's own
  // `public static` and the `this` on its first parameter identify it.
  const ext = /\bpublic\s+static\s+(?:[\w<>\[\],.?]+\s+)+?((?:Add|Map|Use)Bsft\w*)\s*(?:<[^()]*>)?\s*\(\s*this\s+([\w.]+)/g;
  for (const m of code.matchAll(ext)) {
    const owner = types.find((t) => t.start <= m.index && m.index < t.end);
    extensionMethods.push({ name: m[1], extends: m[2].split('.').at(-1), type: owner?.name ?? null });
  }

  return { namespaces, types, extensionMethods, code };
}

// Every string literal of a file, for the checks that read written-out names (routes, table names,
// log templates): [{ start, end, value, interpolated }]. `start` is the offset of the literal's
// first character (its `$`/`@` prefix, if any), so `stripTrivia(text).slice(0, start)` is the code
// in front of it. `value` is the literal's text with its escapes undone and each interpolation hole
// replaced by U+0000, so a hole never reads as part of a name. Literals inside holes are listed too.
export function stringLiterals(text) {
  const found = [];
  const n = text.length;

  function readString(start) {
    let j = start;
    let dollars = 0;
    let verbatim = false;
    while (text[j] === '$' || text[j] === '@') {
      if (text[j] === '$') dollars++;
      else verbatim = true;
      j++;
    }
    let quotes = 0;
    while (text[j + quotes] === '"') quotes++;
    let value = '';
    if (quotes >= 3) {
      j += quotes;
      const close = '"'.repeat(quotes);
      while (j < n) {
        if (text.startsWith(close, j)) { j += quotes; break; }
        if (dollars > 0 && text.startsWith('{'.repeat(dollars), j) && text[j + dollars] !== '{') {
          value += '\u0000';
          j = readHole(j + dollars, dollars);
          continue;
        }
        value += text[j++];
      }
      found.push({ start, end: j, value, interpolated: dollars > 0 });
      return j;
    }
    j++;
    while (j < n) {
      const c = text[j];
      if (verbatim) {
        if (c === '"') {
          if (text[j + 1] === '"') { value += '"'; j += 2; continue; }
          j++;
          break;
        }
      } else {
        if (c === '\\') { value += text[j + 1] ?? ''; j += 2; continue; }
        if (c === '"') { j++; break; }
        if (c === '\n') break;
      }
      if (dollars > 0 && c === '{') {
        if (text[j + 1] === '{') { value += '{'; j += 2; continue; }
        value += '\u0000';
        j = readHole(j + 1, 1);
        continue;
      }
      if (dollars > 0 && c === '}' && text[j + 1] === '}') { value += '}'; j += 2; continue; }
      value += c;
      j++;
    }
    found.push({ start, end: j, value, interpolated: dollars > 0 });
    return j;
  }

  function readHole(start, closers) {
    let depth = 0;
    let j = start;
    while (j < n) {
      const c = text[j];
      if (c === '"' || ((c === '$' || c === '@') && /^[$@]*"/.test(text.slice(j, j + 4)))) { j = readString(j); continue; }
      if (c === '\'') { j = readChar(j); continue; }
      if (c === '(' || c === '[' || c === '{') depth++;
      if (c === ')' || c === ']') depth--;
      if (c === '}') {
        if (depth === 0) return j + closers;
        depth--;
      }
      j++;
    }
    return j;
  }

  function readChar(start) {
    let j = start + 1;
    while (j < n && text[j] !== '\'' && text[j] !== '\n') j += text[j] === '\\' ? 2 : 1;
    return j + 1;
  }

  let i = 0;
  let lineStart = true;
  while (i < n) {
    const c = text[i];
    if (lineStart && c === '#') { const e = text.indexOf('\n', i); i = e < 0 ? n : e; continue; }
    if (c === '\n') { lineStart = true; i++; continue; }
    if (c !== ' ' && c !== '\t' && c !== '\r') lineStart = false;
    if (c === '/' && text[i + 1] === '/') { const e = text.indexOf('\n', i); i = e < 0 ? n : e; continue; }
    if (c === '/' && text[i + 1] === '*') { const e = text.indexOf('*/', i + 2); i = e < 0 ? n : e + 2; continue; }
    if (c === '"' || ((c === '$' || c === '@') && /^[$@]*"/.test(text.slice(i, i + 4))) && !/[\w]/.test(text[i - 1] ?? ' ')) { i = readString(i); continue; }
    if (c === '\'') { i = readChar(i); continue; }
    i++;
  }
  return found.sort((a, b) => a.start - b.start);
}
