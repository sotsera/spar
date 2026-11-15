// Reads the C# blocks of docs/app-model.md (or of the Markdown file given as the first argument) and checks every
// contract in them against the rules the document states, which the stand-ins cannot enforce by compiling:
//   - a command takes zero or one request, which is not a list or an array, then an optional CancellationToken;
//   - a command returns Task, Task<T>, ValueTask, ValueTask<T> or IAsyncEnumerable<T>, never a plain value;
//   - a contract's name is not a JavaScript reserved word or a subpath that @spar/app uses itself;
//   - no error case is called Cancelled, whose code would be the CallError kind `cancelled`.
// It matches text, not syntax trees: it relies on the document's layout (one signature per line) and stands in for
// the contract diagnostics only until the real analyzer exists. Exits 1 when a rule is broken.
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const checkRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const documentPath = process.argv[2] ?? join(checkRoot, "..", "..", "docs", "app-model.md");

const reservedWords = new Set([
  "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
  "enum", "export", "extends", "false", "finally", "for", "function", "if", "implements", "import", "in",
  "instanceof", "interface", "let", "new", "null", "package", "private", "protected", "public", "return", "static",
  "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield",
]);
const sparSubpaths = new Set(["sdk", "vite", "windows", "telemetry", "shared", "spar", "testing"]);
const collections = /\[\]\??$|^(?:IReadOnlyList|IList|List|IEnumerable|IReadOnlyCollection|ICollection|IReadOnlyDictionary|IDictionary|Dictionary|HashSet|ISet)</;
const returns = /^(?:Task|ValueTask)(?:<.+>)?$|^IAsyncEnumerable<.+>$/;

const lines = readFileSync(documentPath, "utf8").split(/\r?\n/);
const blocks = [];
for (let i = 0; i < lines.length; i++) {
  if (lines[i] !== "```csharp") continue;
  const end = lines.indexOf("```", i + 1);
  blocks.push({ start: i + 2, text: lines.slice(i + 1, end).join("\n") });
  i = end;
}

let contracts = 0;
let commands = 0;
let errorCases = 0;
let problems = 0;

function problem(line, text) {
  problems++;
  console.log(`FAIL doc line ${line}: ${text}`);
}

/** The text between the first `{` at or after `from` and its matching `}`. */
function body(text, from) {
  const open = text.indexOf("{", from);
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === "{") depth++;
    else if (text[i] === "}" && --depth === 0) return { open, text: text.slice(open + 1, i) };
  }
  throw new Error("Unbalanced braces");
}

/** Splits a parameter list on the commas that are not inside `<…>`. */
function parameters(list) {
  const parts = [];
  let depth = 0;
  let current = "";
  for (const ch of list) {
    if (ch === "<") depth++;
    if (ch === ">") depth--;
    if (ch === "," && depth === 0) {
      parts.push(current);
      current = "";
    } else current += ch;
  }
  if (current.trim() !== "") parts.push(current);
  // "[EnumeratorCancellation] CancellationToken cancellationToken" -> "CancellationToken"
  return parts.map(part => part.trim().replace(/^(?:\[[^\]]*\]\s*)+/, "").replace(/\s+\w+$/, ""));
}

/** The derived contract name: an interface drops one leading I, both forms drop a trailing Service. */
function contractName(kind, typeName) {
  let name = kind === "interface" && /^I[A-Z]/.test(typeName) ? typeName.slice(1) : typeName;
  if (name.endsWith("Service") && name !== "Service") name = name.slice(0, -"Service".length);
  return name;
}

const camel = name => name.replace(/^[A-Z]+(?=[A-Z][a-z]|$)|^[A-Z]/, start => start.toLowerCase());

for (const block of blocks) {
  const lineOf = index => block.start + block.text.slice(0, index).split("\n").length - 1;

  for (const match of block.text.matchAll(/\[SparContract(?:\("(\w+)"\))?\]\s*public\s+(sealed\s+class|interface)\s+(\w+)/g)) {
    contracts++;
    const kind = match[2] === "interface" ? "interface" : "class";
    const name = match[1] ?? camel(contractName(kind, match[3]));
    if (reservedWords.has(name.toLowerCase())) problem(lineOf(match.index), `contract ${match[3]} is named '${name}', a JavaScript reserved word`);
    if (sparSubpaths.has(name.toLowerCase())) problem(lineOf(match.index), `contract ${match[3]} is named '${name}', a subpath of @spar/app`);

    const contract = body(block.text, match.index + match[0].length);
    const signature = kind === "class"
      ? /\[SparCommand(?:\("(?<wire>\w+)"\))?\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:static\s+)?(?:async\s+)?(?<returns>.+?)\s+(?<method>\w+)\((?<parameters>[^)]*)\)/g
      : /^\s*(?:\[SparCommand(?:\("(?<wire>\w+)"\))?\]\s*)?(?<returns>.+?)\s+(?<method>\w+)\((?<parameters>[^)]*)\);/gm;

    for (const command of contract.text.matchAll(signature)) {
      commands++;
      const { wire, method, parameters: list } = command.groups;
      const line = lineOf(contract.open + 1 + command.index + command[0].lastIndexOf(`${method}(`));
      const where = `${name}.${wire ?? camel(method.replace(/Async$/, ""))}`;
      const returnType = command.groups.returns.trim();
      const types = parameters(list);
      const requests = types.filter(type => type !== "CancellationToken");
      const before = problems;

      if (!returns.test(returnType)) problem(line, `${where} returns '${returnType}': a command returns a Task, a ValueTask or an IAsyncEnumerable`);
      if (requests.length > 1) problem(line, `${where} takes ${requests.length} requests: a command takes zero or one`);
      if (types.includes("CancellationToken") && types.at(-1) !== "CancellationToken") problem(line, `${where}: the CancellationToken must be the last parameter`);
      for (const request of requests) {
        if (collections.test(request)) problem(line, `${where} takes '${request}': a request is one positional record or one scalar, never a list`);
      }

      if (problems === before) console.log(`ok   doc line ${String(line).padStart(4)}  ${where}(${requests.join(", ")}) -> ${returnType}`);
    }
  }

  for (const match of block.text.matchAll(/public\s+closed\s+record\s+(\w+)/g)) {
    const error = body(block.text, match.index + match[0].length);
    for (const errorCase of error.text.matchAll(/public\s+sealed\s+record\s+(\w+)/g)) {
      errorCases++;
      if (camel(errorCase[1]) !== "cancelled") continue;
      problem(lineOf(error.open + 1 + errorCase.index), `error case ${match[1]}.${errorCase[1]} has the code 'cancelled', the CallError kind of an aborted call`);
    }
  }
}

console.log(`${contracts} contracts, ${commands} commands, ${errorCases} error cases: ${problems === 0 ? "no rule broken" : `${problems} broken rules`}`);
if (problems > 0 || contracts === 0) process.exit(1);
