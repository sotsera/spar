// Usage: node tools/vt.mjs <path to WebView2.h> <InterfaceName>... — prints each interface's C vtable with 0-based slots.
import fs from 'node:fs';
const h = fs.readFileSync(process.argv[2], 'utf8');
for (const name of process.argv.slice(3)) {
  const re = new RegExp(String.raw`typedef struct ${name}Vtbl\s*\{([\s\S]*?)\}\s*${name}Vtbl;`);
  const m = h.match(re);
  if (!m) { console.log(name, 'NOT FOUND'); continue; }
  const iid = h.match(new RegExp(String.raw`IID_${name} = \{(0x\w+),(0x\w+),(0x\w+),\{([^}]*)\}`));
  console.log('==', name, iid ? iid.slice(1).join(',') : '');
  const ms = [...m[1].matchAll(/DECLSPEC_XFGVIRT\((\w+),\s*(\w+)\)([\s\S]*?)\);/g)];
  ms.forEach((x, i) => {
    if (x[1] === 'IUnknown') return;
    const sig = x[3].replace(/\s+/g, ' ').replace(/^.*?This,?/, '').trim();
    console.log(`  ${String(i).padStart(3)} ${x[1]}::${x[2]} (${sig})`);
  });
}
