// Prints the vtable slot index of every method of the named interfaces, read from the C vtable structs of a header.
// Usage: node vtable.mjs <WebView2.h> <Interface> [Interface...]
import { readFileSync } from 'node:fs';

const [header, ...names] = process.argv.slice(2);
const text = readFileSync(header, 'utf8');
for (const name of names) {
  const start = text.indexOf(`typedef struct ${name}Vtbl`);
  if (start < 0) { console.log(`${name}: not found`); continue; }
  const end = text.indexOf(`} ${name}Vtbl;`, start);
  const body = text.slice(start, end);
  const methods = [...body.matchAll(/\(\s*STDMETHODCALLTYPE\s*\*(\w+)\s*\)/g)].map(m => m[1]);
  console.log(`${name} (${methods.length} slots)`);
  methods.forEach((m, i) => console.log(`  ${String(i).padStart(3)} ${m}`));
}
