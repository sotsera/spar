import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, readdir, stat } from 'node:fs/promises';
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const skillsRoot = resolve(root, '.agents/skills');
const normalized = (text) => text.replaceAll('\r\n', '\n');
const digest = (text) => createHash('sha256').update(normalized(text), 'utf8').digest('hex');
const read = (path) => readFile(path, 'utf8');

// These instruction-only skills intentionally use scalar frontmatter. This is
// a constrained repository format check, not a general YAML parser or LLM grader.
function header(text) {
  const match = normalized(text).match(/^---\n([\s\S]*?)\n---\n/);
  assert.ok(match, 'Missing frontmatter');
  const fields = {};
  for (const line of match[1].split('\n')) {
    const pair = line.match(/^([a-z-]+): (.+)$/);
    assert.ok(pair, `Unsupported frontmatter syntax: ${line}`);
    assert.ok(!Object.hasOwn(fields, pair[1]), `Duplicate field: ${pair[1]}`);
    fields[pair[1]] = pair[2].startsWith('"') ? JSON.parse(pair[2]) : pair[2];
  }
  return fields;
}

function within(base, path) {
  const suffix = relative(base, path);
  assert.ok(suffix !== '..' && !suffix.startsWith(`..${sep}`) && !isAbsolute(suffix), `Path escapes ${base}`);
  return path;
}

async function checkLinks(path, text) {
  for (const [, link] of text.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)) {
    if (/^https?:\/\//.test(link) || link.startsWith('#')) continue;
    const target = within(root, resolve(dirname(path), link.split('#')[0]));
    assert.ok((await stat(target)).isFile(), `Missing local reference: ${target}`);
  }
}

const manifest = JSON.parse(await read(resolve(skillsRoot, 'UPSTREAM.json')));
assert.equal(manifest.schema, 1);
assert.equal(manifest.repository, 'https://github.com/dotnet/skills');
assert.match(manifest.revision, /^[0-9a-f]{40}$/);
const declared = manifest.skills.map((entry) => entry.name).sort();
assert.equal(new Set(declared).size, declared.length, 'Duplicate skill');
const installed = (await readdir(skillsRoot, { withFileTypes: true }))
  .filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort();
assert.deepEqual(installed, declared, 'Skill inventory differs from provenance');

const licensePath = within(skillsRoot, resolve(skillsRoot, manifest.license.localPath));
assert.equal(digest(await read(licensePath)), manifest.license.sha256, 'Upstream license changed');
for (const entry of manifest.skills) {
  assert.match(entry.name, /^spar-[a-z0-9]+(?:-[a-z0-9]+)*$/);
  assert.match(entry.sourcePath, /^plugins\/[a-z0-9-]+\/skills\/[a-z0-9-]+\/SKILL\.md$/);
  assert.match(entry.sourceSha256, /^[0-9a-f]{64}$/);
  assert.ok(entry.adaptation.trim(), 'Missing adaptation record');
  const path = resolve(skillsRoot, entry.name, 'SKILL.md');
  const text = await read(path);
  const fields = header(text);
  assert.deepEqual(Object.keys(fields).sort(), ['description', 'license', 'name']);
  assert.equal(fields.name, entry.name);
  assert.equal(fields.license, 'MIT');
  assert.ok(fields.description.length > 0 && fields.description.length <= 1024);
  await checkLinks(path, text);
}

const overlayPath = resolve(root, '.agents/skill-overlays/dotnet-test/run-tests.md');
const overlay = await read(overlayPath);
assert.deepEqual(header(overlay), { core: 'dotnet-test/run-tests', 'binding-revision': '1', mode: 'extend' });
await checkLinks(overlayPath, overlay);

const cases = JSON.parse(await read(resolve(root, 'eng/agent-skills/cases.json')));
assert.equal(new Set(cases.map((item) => item.id)).size, cases.length, 'Duplicate evaluation case');
const covered = new Set();
for (const item of cases) {
  assert.ok(item.prompt.trim() && item.checks.length > 0);
  for (const name of item.expectedSkills) {
    assert.ok(declared.includes(name), `Unknown expected skill: ${name}`);
    covered.add(name);
  }
}
assert.deepEqual([...covered].sort(), declared, 'A skill has no positive evaluation case');
assert.ok(cases.some((item) => item.expectedSkills.length === 0), 'No dormancy cases');

// Optional offline verification against a checkout of the pinned upstream source.
// CI validates the retained records without fetching or executing third-party tools.
if (process.argv[2]) {
  const upstream = resolve(process.argv[2]);
  for (const entry of manifest.skills) {
    const path = within(upstream, resolve(upstream, entry.sourcePath));
    assert.equal(digest(await read(path)), entry.sourceSha256, `Upstream mismatch: ${entry.name}`);
  }
  const sourceLicense = within(upstream, resolve(upstream, manifest.license.sourcePath));
  assert.equal(digest(await read(sourceLicense)), manifest.license.sha256);
}

console.log(`Validated ${declared.length} skills, provenance, license, local links, runner overlay, and ${cases.length} case definitions.`);
console.log('This structural check does not execute prompts or prove automatic skill activation.');
