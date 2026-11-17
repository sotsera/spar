// Compares the package Spar wrote with what tsc wrote from its typed twin (TypeScriptTwin.cs): each file is parsed
// and printed again by TypeScript, without comments, so only the code itself is compared, not its layout.
// Usage: node compare.cjs <typescript package folder> <Spar's folder> <tsc's folder> <file>...
// Exit code 0 when every file matches; otherwise each difference is printed and the exit code is 1.
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const [typeScriptFolder, ours, theirs, ...files] = process.argv.slice(2);
const ts = require(typeScriptFolder);
const printer = ts.createPrinter({ removeComments: true, newLine: ts.NewLineKind.LineFeed });

/** The file as TypeScript prints it; declarations from tsc lose what Spar leaves out on purpose. */
function normalized(folder, file, fromTsc) {
  const text = fs.readFileSync(path.join(folder, file), "utf8");
  const kind = file.endsWith(".d.ts") ? ts.ScriptKind.TS : ts.ScriptKind.JS;
  let source = ts.createSourceFile(file, text, ts.ScriptTarget.ES2022, true, kind);
  if (fromTsc && file.endsWith(".d.ts")) {
    // shared.js exports its readers and writers to the contract modules only; Spar does not declare them, nor
    // import the SDK modules only those declarations would need. Nor the table that only @spar/app/testing reads.
    const kept = source.statements.filter(statement =>
      !ts.isFunctionDeclaration(statement)
      && !(ts.isImportDeclaration(statement) && statement.moduleSpecifier.text.startsWith("./sdk/"))
      && !(ts.isVariableStatement(statement) && statement.declarationList.declarations.some(d => d.name.getText() === "__sparTesting")));
    source = ts.factory.updateSourceFile(source, kept);
  }

  return tokens(printer.printFile(source));
}

/**
 * The code's tokens: the printer keeps some layout from the source (an object literal on one line or several, a lone
 * arrow parameter in parentheses or not), which says nothing about what the code does.
 */
function tokens(text) {
  const scanner = ts.createScanner(ts.ScriptTarget.ES2022, true, ts.LanguageVariant.Standard, text);
  const list = [];
  for (let kind = scanner.scan(); kind !== ts.SyntaxKind.EndOfFileToken; kind = scanner.scan()) {
    list.push(scanner.getTokenText());
  }

  // "(x) =>" and "x =>" are the same arrow function, and a trailing comma changes nothing.
  const simplified = [];
  for (let i = 0; i < list.length; i++) {
    if (list[i] === "," && [")", "]", "}"].includes(list[i + 1])) {
      continue;
    }

    if (list[i] === "(" && list[i + 2] === ")" && list[i + 3] === "=>" && /^[A-Za-z_$][\w$]*$/.test(list[i + 1])) {
      simplified.push(list[i + 1]);
      i += 2;
      continue;
    }

    simplified.push(list[i]);
  }

  return simplified;
}

let failed = false;
for (const file of files) {
  const expected = normalized(theirs, file, true);
  const actual = normalized(ours, file, false);
  const length = Math.max(expected.length, actual.length);
  for (let token = 0; token < length; token++) {
    if (expected[token] !== actual[token]) {
      failed = true;
      const from = Math.max(0, token - 12);
      console.log(`${file}: first difference at token ${token + 1}`);
      console.log(`  tsc:  ${expected.slice(from, token + 12).join(" ")}`);
      console.log(`  Spar: ${actual.slice(from, token + 12).join(" ")}`);
      break;
    }
  }
}

process.exit(failed ? 1 : 0);
