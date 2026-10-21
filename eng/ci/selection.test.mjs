import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import test from 'node:test';
import { audit, changedPaths, checks, classify, fullSelection, git, groups, selectEvent } from './selection.mjs';

const baseline = ['baseline', 'frontend'];
const development = [...baseline, 'development', 'production', 'packages'];
const cases = [
  ['documentation', ['README.md', 'docs/12-backend-updates.md'], []],
  ['historical evidence', ['docs/evidence/agent-skills-2026-10-20/examples/failure/MissingInput.proj'], []],
  ['skills', ['.agents/skills/spar-run-tests/SKILL.md', 'eng/agent-skills/cases.json'], []],
  ['frontend', ['fixtures/Spar.NativeSample/frontend/src/App.tsx'], development],
  ['minimal frontend', ['samples/Spar.NativeSample/frontend/src/App.tsx'], development],
  ['minimal contract', ['samples/Spar.NativeSample.Contracts/BackendContract.cs'], groups],
  ['frontend styling', ['fixtures/Spar.NativeSample/frontend/src/styles.css'], development],
  ['runtime', ['src/Sotsera.Spar.Windows/SparWindow.cs'], groups],
  ['contract', ['fixtures/Spar.NativeSample.Contracts/BackendContract.cs'], groups],
  ['generator runtime', ['tools/Spar.ContractGenerator/ClientRuntime.js'], groups],
  ['coordinator', ['src/Sotsera.Spar.Development/DevelopmentSession.cs'], development],
  ['editor', ['tools/Spar.VSCode/extension.cjs'], development],
  ['inspection exclusion', ['eng/Test-InspectionBoundary.ps1'], [...baseline, 'production', 'compatibility', 'packages']],
  ['packaging targets', ['build/Spar.BuildPackage.targets'], groups],
  ['package driver', ['eng/Pack-Local.ps1'], [...baseline, 'packages']],
  ['managed test', ['tests/Sotsera.Spar.Bridge.Tests/BridgeTests.cs'], baseline],
  ['native probe driver', ['eng/Test-NativeHost.ps1'], [...baseline, 'native', 'production', 'aot']],
  ['shared session helper', ['eng/Test-DevelopmentSession.ps1'], groups],
  ['shared config', ['Directory.Build.props'], groups],
  ['toolchain', ['global.json'], groups],
  ['frontend dependency', ['fixtures/Spar.NativeSample/frontend/package-lock.json'], groups],
  ['CI workflow', ['.github/workflows/managed-probe.yml'], groups],
  ['classifier', ['eng/ci/selection.mjs'], groups],
  ['unknown path', ['new-component/file.cs'], groups],
  ['executable added to skills', ['.agents/skills/new/helper.py'], groups],
  ['mixed documentation and runtime', ['README.md', 'src/Sotsera.Spar.Bridge/SparBridge.cs'], groups],
  ['mixed development and compatibility', ['tools/Spar.Dev/Program.cs', 'eng/Test-Compatibility.ps1'], [...baseline, 'development', 'production', 'compatibility', 'packages']],
];
for (const [name, paths, expected] of cases) {
  test(name, () => assert.deepEqual(classify(paths).groups, expected));
}

test('empty and invalid classifications select full coverage', () => {
  for (const paths of [[], undefined, ['../README.md'], [''], ['/README.md'], [null]]) {
    assert.deepEqual(classify(paths).groups, groups);
  }
});

test('explicit, tag and unknown events select full coverage', () => {
  for (const [name, event] of [['workflow_dispatch', {}], ['push', { ref: 'refs/tags/v0.1.0' }], ['schedule', {}]]) {
    assert.deepEqual(selectEvent(name, event).groups, groups);
  }
});

const root = resolve('artifacts/ci-tests');
mkdirSync(root, { recursive: true });

test('real Git ranges cover rename source/destination, merge base and every pushed commit', () => {
  const cwd = mkdtempSync(resolve(root, 'history-'));
  git(['init', '-b', 'main'], cwd);
  git(['config', 'user.name', 'CI selector fixture'], cwd);
  git(['config', 'user.email', 'fixture@example.invalid'], cwd);
  function commit(message) {
    git(['add', '.'], cwd);
    git(['-c', 'commit.gpgsign=false', 'commit', '-m', message], cwd);
    return git(['rev-parse', 'HEAD'], cwd);
  }
  writeFileSync(resolve(cwd, 'README.md'), 'initial\n');
  const initial = commit('initial');
  git(['switch', '-c', 'feature'], cwd);
  renameSync(resolve(cwd, 'README.md'), resolve(cwd, 'runtime.cs'));
  const renamed = commit('move to unknown runtime');
  assert.deepEqual(changedPaths(initial, renamed, cwd).sort(), ['README.md', 'runtime.cs']);
  assert.deepEqual(classify(changedPaths(initial, renamed, cwd)).groups, groups);
  writeFileSync(resolve(cwd, 'README.md'), 'later documentation\n');
  const head = commit('docs after runtime');
  assert.deepEqual(selectEvent('push', { ref: 'refs/heads/main', before: initial, after: head }, cwd).groups, groups);
  git(['switch', 'main'], cwd);
  writeFileSync(resolve(cwd, 'only-on-base.cs'), 'base change\n');
  const baseHead = commit('advance base');
  const pr = selectEvent('pull_request', { pull_request: { base: { sha: baseHead }, head: { sha: head } } }, cwd);
  assert.equal(pr.base, initial);
  assert.ok(!pr.paths.includes('only-on-base.cs'));
  assert.ok(pr.paths.includes('runtime.cs'));
  assert.throws(() => selectEvent('push', { ref: 'refs/heads/main', before: '0'.repeat(40), after: head }, cwd));
  assert.throws(() => selectEvent('push', { ref: 'refs/heads/main', before: 'a'.repeat(40), after: head }, cwd));
});

test('selector CLI falls back on malformed event JSON and emits every group', () => {
  const cwd = mkdtempSync(resolve(root, 'fallback-'));
  const event = resolve(cwd, 'event.json');
  const output = resolve(cwd, 'output.txt');
  writeFileSync(event, '{broken');
  execFileSync(process.execPath, [resolve('eng/ci/Select-Checks.mjs')], {
    cwd, env: { ...process.env, GITHUB_EVENT_PATH: event, GITHUB_OUTPUT: output },
  });
  const selection = JSON.parse(readFileSync(resolve(cwd, 'artifacts/ci/selection.json'), 'utf8'));
  assert.deepEqual(selection.groups, groups);
  for (const group of groups) assert.ok(readFileSync(output, 'utf8').includes(`${group}=true\n`));
});

test('gate rejects failure, cancellation, skipped or missing selected checks, even with a success conclusion', () => {
  const selection = fullSelection('test');
  const steps = Object.fromEntries(Object.keys(checks).map(id => [id, { outcome: 'success' }]));
  assert.ok(audit(selection, steps).every(result => result.passed));
  for (const id of Object.keys(checks)) {
    for (const outcome of ['failure', 'cancelled', 'skipped', undefined]) {
      const broken = { ...steps, [id]: { outcome, conclusion: 'success' } };
      assert.equal(audit(selection, broken).find(result => result.id === id).passed, false);
    }
  }
  const light = Object.fromEntries(['selection_tests', 'documentation', 'skills'].map(id => [id, { outcome: 'success' }]));
  assert.ok(audit(classify(['README.md']), light).every(result => result.passed));
});

test('gate CLI fails on a selected failure and missing selector, then recovers', () => {
  const cwd = mkdtempSync(resolve(root, 'gate-'));
  mkdirSync(resolve(cwd, 'artifacts/ci'), { recursive: true });
  writeFileSync(resolve(cwd, 'artifacts/ci/selection.json'), JSON.stringify(classify(['README.md'])));
  const steps = Object.fromEntries(['selection', 'selection_tests', 'documentation', 'skills'].map(id => [id, { outcome: 'success' }]));
  function run() {
    return spawnSync(process.execPath, [resolve('eng/ci/Complete-Checks.mjs')], {
      cwd, env: { ...process.env, CI_STEP_RESULTS: JSON.stringify(steps), GITHUB_STEP_SUMMARY: resolve(cwd, 'summary.md') },
    }).status;
  }
  assert.equal(run(), 0);
  steps.skills.outcome = 'failure';
  assert.equal(run(), 1);
  steps.skills.outcome = 'success';
  assert.equal(run(), 0);
  delete steps.selection;
  assert.equal(run(), 1);
});

test('workflow keeps each protected check wired to its audited selection and safe ordering', () => {
  const workflow = readFileSync('.github/workflows/managed-probe.yml', 'utf8');
  const blocks = workflow.split(/^      - /m).slice(1);
  for (const [id, group] of Object.entries(checks)) {
    const matching = blocks.filter(block => block.includes(`\n        id: ${id}\n`));
    assert.equal(matching.length, 1, `Missing/duplicate workflow step: ${id}`);
    assert.ok(!matching[0].includes('continue-on-error'), `${id} must propagate failure`);
    if (group === 'always') assert.ok(!matching[0].includes('\n        if:'));
    else assert.ok(matching[0].includes(`if: steps.selection.outputs.${group} != 'false'`), id);
  }
  assert.ok(workflow.includes('name: Windows managed probe'));
  assert.ok(workflow.includes('workflow_dispatch:'));
  assert.ok(!/paths(?:-ignore)?:/.test(workflow));
  assert.ok(workflow.indexOf('id: build\n') < workflow.indexOf('id: client\n'));
  assert.ok(workflow.indexOf('id: boundary\n') < workflow.indexOf('id: publish_probe\n'));
  assert.ok(workflow.includes('name: Audit all selected check results\n        if: always()'));
  assert.ok(workflow.includes('CI_STEP_RESULTS: ${{ toJSON(steps) }}'));
});
