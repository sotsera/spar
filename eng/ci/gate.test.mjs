import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, copyFileSync } from 'node:fs';
import { resolve } from 'node:path';
import test from 'node:test';
import { audit, catalog, catalogSha256 } from './gate.mjs';

const source = 'a'.repeat(40);
const jobs = { suites: { result: 'success' } };
const reports = () => Object.entries(catalog.suites).map(([suite, definition]) => ({
  schema: 1, suite, source, workingTreeDirty: false, runId: '42', attempt: '1', catalogSha256, passed: true,
  checks: definition.steps.map(step => ({ id: step.id, outcome: 'success', elapsedMs: 1 })),
}));
const check = (values, outcomes = jobs) => audit(values, outcomes, source, '42', '1');

test('all fixed suites and their checks must pass', () => {
  assert.deepEqual(Object.keys(catalog.suites), ['fast', 'processes', 'development', 'delivery', 'packages']);
  assert.deepEqual(check(reports()), []);
  // Rerunning failed jobs preserves successful jobs from an earlier attempt of the same run/source.
  assert.deepEqual(audit(reports(), jobs, source, '42', '2'), []);
  for (const suite of reports()) {
    assert.ok(check(reports().filter(report => report.suite !== suite.suite)).length);
    assert.ok(check([...reports(), suite]).length);
    for (const outcome of ['failure', 'cancelled', 'skipped', 'not-run', undefined]) {
      for (let index = 0; index < suite.checks.length; index++) {
        const values = reports();
        values.find(report => report.suite === suite.suite).checks[index].outcome = outcome;
        assert.ok(check(values).length, `${suite.suite}/${index}: ${outcome}`);
      }
    }
  }
  for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
    assert.ok(check(reports(), { suites: { result } }).length);
  }
});

test('success flags cannot hide stale, malformed or incomplete reports', () => {
  for (const mutation of [
    report => { report.passed = false; },
    report => { report.source = 'b'.repeat(40); },
    report => { report.workingTreeDirty = true; },
    report => { report.catalogSha256 = 'old'; },
    report => { report.runId = '41'; },
    report => { report.attempt = '2'; },
    report => { report.checks.pop(); },
    report => { report.checks[0].elapsedMs = null; },
    report => { report.checks[0].id = 'unexpected'; },
  ]) {
    const values = reports();
    mutation(values[0]);
    assert.ok(check(values).length);
  }
  assert.ok(check([...reports(), { suite: 'unknown' }]).length);
});

const root = resolve('artifacts/ci-gate-tests');
mkdirSync(root, { recursive: true });
test('gate executable rejects missing/malformed artifacts and a failed child', () => {
  const cwd = mkdtempSync(resolve(root, 'gate-'));
  const directory = resolve(cwd, 'artifacts/ci/suites');
  mkdirSync(directory, { recursive: true });
  const run = () => spawnSync(process.execPath, [resolve('eng/ci/gate.mjs')], {
    cwd, env: { ...process.env, GITHUB_SHA: source, GITHUB_RUN_ID: '42', GITHUB_RUN_ATTEMPT: '1',
      CI_JOB_RESULTS: JSON.stringify(jobs) },
  }).status;
  assert.equal(run(), 1);
  for (const report of reports()) writeFileSync(resolve(directory, `${report.suite}.json`), JSON.stringify(report));
  assert.equal(run(), 0);
  const broken = reports()[0];
  broken.checks[0].outcome = 'failure';
  writeFileSync(resolve(directory, `${broken.suite}.json`), JSON.stringify(broken));
  assert.equal(run(), 1);
  writeFileSync(resolve(directory, `${broken.suite}.json`), '{broken');
  assert.equal(run(), 1);
});

test('suite entry point propagates a real child failure and leaves later checks unexecuted', () => {
  const cwd = mkdtempSync(resolve(root, 'runner-'));
  mkdirSync(resolve(cwd, 'eng'));
  copyFileSync(resolve('eng/Test.ps1'), resolve(cwd, 'eng/Test.ps1'));
  const fixture = { schema: 1, suites: { fast: { steps: [
    { id: 'failure', command: 'node', arguments: ['-e', 'process.exit(23)'] },
    { id: 'must-not-run', command: 'node', arguments: ['-e', 'process.exit(0)'] },
  ] } } };
  writeFileSync(resolve(cwd, 'eng/test-suites.json'), JSON.stringify(fixture));
  const git = args => {
    const result = spawnSync('git', args, { cwd, encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
  };
  git(['init']);
  git(['add', '.']);
  git(['-c', 'user.name=Gate fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgsign=false', 'commit', '-m', 'fixture']);
  const result = spawnSync('pwsh', ['-NoProfile', '-File', 'eng/Test.ps1'], { cwd, encoding: 'utf8' });
  assert.notEqual(result.status, 0, result.stdout + result.stderr);
  const report = JSON.parse(readFileSync(resolve(cwd, 'artifacts/ci/suite-fast.json'), 'utf8').replace(/^\uFEFF/, ''));
  assert.equal(report.passed, false);
  assert.deepEqual(report.checks.map(item => item.outcome), ['failure', 'not-run']);
  assert.match(result.stderr, /exit 23/);
});

test('workflow runs the fixed catalog and always audits it under the protected name', () => {
  const workflow = readFileSync('.github/workflows/managed-probe.yml', 'utf8');
  assert.ok(workflow.includes('suite: [fast, processes, development, delivery, packages]'));
  assert.ok(workflow.includes('fail-fast: false'));
  assert.ok(workflow.includes('name: Windows managed probe\n'));
  assert.ok(workflow.includes('needs: [suites]\n    if: always()'));
  assert.ok(workflow.includes('CI_JOB_RESULTS: ${{ toJSON(needs) }}'));
  assert.ok(!/paths(?:-ignore)?:|Select-Checks|continue-on-error/.test(workflow));
  const steps = catalog.suites.delivery.steps.map(step => step.id);
  assert.ok(steps.indexOf('production-boundary') < steps.indexOf('publish-probe'));
  for (const definition of Object.values(catalog.suites)) {
    assert.ok(definition.steps.length > 0);
    assert.equal(new Set(definition.steps.map(step => step.id)).size, definition.steps.length);
  }
});
