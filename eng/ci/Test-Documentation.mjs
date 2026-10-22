import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';

// Validate authored guide links, not historical evidence transcripts, remote
// availability, Markdown anchors, or illustrative paths inside code fences.
const paths = execFileSync('git', ['ls-files', '-z'], { encoding: 'utf8' }).split('\0').filter(path =>
  ['README.md', 'AGENTS.md'].includes(path) || /^docs\/[^/]+\.md$/.test(path));
let links = 0;
for (const path of paths) {
  const content = readFileSync(path, 'utf8').replace(/^```[^\n]*\n[\s\S]*?^```\s*$/gm, '');
  for (const [, link] of content.matchAll(/\[[^\]]*\]\(([^\s)]+)\)/g)) {
    if (/^[a-z][a-z0-9+.-]*:/i.test(link) || link.startsWith('#')) continue;
    const target = decodeURIComponent(link.split('#')[0]);
    assert.ok(existsSync(resolve(dirname(path), target)), `${path}: missing local reference ${link}`);
    links++;
  }
}
console.log(`Validated ${links} local references in ${paths.length} authored guides.`);
