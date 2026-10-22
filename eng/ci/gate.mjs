import { createHash } from 'node:crypto';
import { appendFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const catalogText = readFileSync(new URL('../test-suites.json', import.meta.url), 'utf8');
export const catalog = JSON.parse(catalogText);
export const catalogSha256 = createHash('sha256').update(catalogText).digest('hex');

export function audit(reports, jobs, source, runId, attempt) {
  const errors = [];
  if (jobs?.suites?.result !== 'success') errors.push('Suite jobs did not all succeed.');
  for (const [suite, definition] of Object.entries(catalog.suites)) {
    const matching = reports.filter(report => report.suite === suite);
    if (matching.length !== 1) {
      errors.push(`${suite}: expected exactly one report, got ${matching.length}.`);
      continue;
    }
    const report = matching[0];
    const reportAttempt = Number(report.attempt);
    if (report.schema !== 1 || report.source !== source || report.workingTreeDirty !== false || report.runId !== runId
      || !Number.isInteger(reportAttempt) || reportAttempt < 1 || reportAttempt > Number(attempt)
      || report.catalogSha256 !== catalogSha256 || report.passed !== true) {
      errors.push(`${suite}: failed report or source/run/catalog mismatch.`);
    }
    const checks = report.checks;
    if (!Array.isArray(checks) || checks.length !== definition.steps.length
      || definition.steps.some((step, index) => checks[index]?.id !== step.id
        || checks[index].outcome !== 'success' || !Number.isFinite(checks[index].elapsedMs)
        || checks[index].elapsedMs < 0)) {
      errors.push(`${suite}: missing, failed or unexpected checks.`);
    }
  }
  if (reports.some(report => !Object.hasOwn(catalog.suites, report.suite))) errors.push('Unexpected suite report.');
  return errors;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const directory = 'artifacts/ci/suites';
  let errors;
  let reports = [];
  try {
    reports = existsSync(directory) ? readdirSync(directory).filter(name => name.endsWith('.json'))
      .map(name => JSON.parse(readFileSync(resolve(directory, name), 'utf8'))) : [];
    errors = audit(reports, JSON.parse(process.env.CI_JOB_RESULTS ?? '{}'), process.env.GITHUB_SHA,
      process.env.GITHUB_RUN_ID, process.env.GITHUB_RUN_ATTEMPT);
  } catch (error) {
    errors = [`Cannot read suite results: ${error.message}`];
  }
  mkdirSync('artifacts/ci', { recursive: true });
  writeFileSync('artifacts/ci/results.json', JSON.stringify({ source: process.env.GITHUB_SHA, passed: !errors.length, errors, reports }, null, 2) + '\n');
  const summary = ['## Windows managed probe', '', ...reports.map(report =>
    `- ${report.suite}: ${report.passed ? 'passed' : 'failed'}`), '', ...errors.map(error => `- ${error}`), '',
  `Gate: **${errors.length ? 'failed' : 'passed'}**.`, ''].join('\n');
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
  console.log(summary);
  if (errors.length) process.exitCode = 1;
}
