import { execFileSync } from 'node:child_process';

export const groups = ['baseline', 'frontend', 'development', 'native', 'production', 'compatibility', 'packages', 'aot'];

// Each ID also names a workflow step. The final gate audits outcomes, not conclusions
// that could have been rewritten by continue-on-error.
export const checks = {
  selection_tests: 'always',
  documentation: 'always',
  skills: 'always',
  build: 'baseline',
  managed: 'baseline',
  framing: 'baseline',
  client: 'frontend',
  spa: 'frontend',
  notices: 'frontend',
  session: 'development',
  backend: 'development',
  project_launch: 'development',
  inspection: 'development',
  minimal: 'development',
  minimal_publish: 'production',
  minimal_consumer: 'packages',
  bridge: 'native',
  lifecycle: 'native',
  publish: 'production',
  boundary: 'production',
  production: 'production',
  compatibility: 'compatibility',
  pack: 'packages',
  consumer: 'packages',
  publish_probe: 'aot',
  copied_probe: 'aot',
};

const development = ['development', 'production', 'packages'];
const drivers = {
  'eng/Test-ProbeEvents.ps1': ['baseline'],
  'eng/Test-FrontendNotices.mjs': ['frontend', 'production', 'packages'],
  'eng/Test-BackendUpdates.ps1': ['development'],
  'eng/Test-ProjectLaunch.ps1': ['development'],
  'eng/Test-DevelopmentInspection.ps1': ['development'],
  'eng/Test-Bridge.ps1': ['native', 'aot'],
  'eng/Test-NativeHost.ps1': ['native', 'aot'],
  'eng/Test-Production.ps1': ['production', 'compatibility', 'packages'],
  'eng/Test-ProductionBoundary.ps1': ['production', 'compatibility', 'packages'],
  'eng/Test-InspectionBoundary.ps1': ['production', 'compatibility', 'packages'],
  'eng/Test-Compatibility.ps1': ['compatibility'],
  'eng/Test-ExternalConsumer.ps1': ['packages'],
  'eng/New-PackageConsumer.ps1': ['packages'],
  'eng/Test-MinimalApplication.ps1': development,
  'eng/Test-DevelopmentReadiness.ps1': development,
  'eng/Test-MinimalConsumer.ps1': ['packages'],
  'eng/Test-MinimalPublication.ps1': ['production', 'packages'],
  'eng/Pack-Local.ps1': ['packages'],
  'eng/Test-VSCodeProbe.ps1': development,
  'eng/Test-VSCodeSession.ps1': development,
  'eng/Test-VSCodeDevelopment.ps1': development,
};

export function fullSelection(reason, paths = []) {
  return { groups: [...groups], paths, reasons: [reason] };
}

export function classify(paths) {
  if (!Array.isArray(paths) || paths.length === 0) return fullSelection('No usable changed paths');
  const selected = new Set();
  const reasons = [];
  for (const path of paths) {
    let matched;
    if (typeof path !== 'string' || !path || path.includes('\\') || path.startsWith('/') || path.split('/').includes('..')) {
      return fullSelection('Invalid changed path');
    }
    if (/^(README\.md|AGENTS\.md|docs\/.*\.md|docs\/evidence\/.*|\.agents\/.*\.md|\.agents\/skills\/(UPSTREAM\.json|dotnet-skills-LICENSE\.txt)|eng\/agent-skills\/cases\.json|eng\/Test-AgentSkills\.mjs)$/.test(path)) {
      matched = [];
    } else if (/^(\.github\/|eng\/ci\/)/.test(path) || /(^|\/)(global\.json|Directory\.[^/]+|NuGet\.Config|nuget\.config|package(?:-lock)?\.json|packages\.lock\.json)$/.test(path)
      || /\.(csproj|props|targets|slnx)$/.test(path)) {
      matched = groups;
    } else if (Object.hasOwn(drivers, path)) {
      matched = drivers[path];
    } else if (/^(src\/Sotsera\.Spar\.Development\/|tools\/Spar\.(Dev|VSCode)\/|\.vscode\/|eng\/vscode-[^/]+\/)/.test(path)
      || /^(samples|fixtures)\/Spar\.NativeSample\/(spar\.dev\.json|Properties\/launchSettings\.json)$/.test(path)) {
      matched = development;
    } else if (/^(samples|fixtures)\/Spar\.NativeSample\/frontend\//.test(path)) {
      matched = development;
    } else if (/^(tests\/|experiments\/Spar\.HotReloadProbe\/|eng\/bridge-client-tests\/)/.test(path)) {
      matched = ['baseline', 'frontend'];
    } else if (/^(src\/Sotsera\.Spar\.(Windows|Bridge)\/|tools\/Spar\.ContractGenerator\/|tools\/Sotsera\.Spar\.Build\/|fixtures\/Spar\.NativeSample(?:\.Contracts)?\/|build\/|third-party\/|eng\/compatibility-probe\/|eng\/native-probe\/|eng\/development-probe\/)/.test(path)
      || ['LICENSE', 'eng/Read-ProbeEvents.ps1', 'eng/Test-DevelopmentSession.ps1', 'eng/Build-AssetBundle.ps1', 'eng/Write-PublishReceipt.ps1'].includes(path)) {
      matched = groups;
    } else {
      matched = groups;
      reasons.push(`Unknown path selects full coverage: ${path}`);
    }
    for (const group of matched) selected.add(group);
    reasons.push(`${path}: ${matched.length ? matched.join(', ') : 'lightweight'}`);
  }
  if (selected.size) {
    selected.add('baseline');
    selected.add('frontend');
  }
  // The probe publish can overwrite Release intermediates: always verify the
  // ordinary production boundary first, even for a probe-driver-only change.
  if (selected.has('aot')) selected.add('production');
  return { groups: groups.filter(group => selected.has(group)), paths, reasons };
}

export function git(args, cwd = process.cwd()) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 }).trimEnd();
}

export function changedPaths(base, head, cwd) {
  // --no-renames reports both names, including moves out of a lightweight area.
  return git(['diff', '--name-only', '-z', '--no-renames', base, head, '--'], cwd).split('\0').filter(Boolean);
}

export function selectEvent(eventName, event, cwd) {
  if (eventName === 'workflow_dispatch' || (eventName === 'push' && event.ref?.startsWith('refs/tags/'))) {
    return { ...fullSelection('Explicit full qualification'), event: eventName };
  }
  let base;
  let head;
  if (eventName === 'pull_request') {
    base = event.pull_request?.base?.sha;
    head = event.pull_request?.head?.sha;
  } else if (eventName === 'push' && event.ref === 'refs/heads/main') {
    base = event.before;
    head = event.after;
  } else {
    return { ...fullSelection(`Unsupported event: ${eventName}`), event: eventName };
  }
  if (![base, head].every(sha => typeof sha === 'string' && /^[a-f0-9]{40}$/.test(sha) && !/^0+$/.test(sha))) {
    throw new Error('Missing or invalid comparison commit');
  }
  if (eventName === 'pull_request') base = git(['merge-base', base, head], cwd);
  return { ...classify(changedPaths(base, head, cwd)), event: eventName, base, head };
}

export function audit(selection, steps) {
  if (!Array.isArray(selection.groups) || selection.groups.some(group => !groups.includes(group))) {
    throw new Error('Invalid selection groups');
  }
  return Object.entries(checks).map(([id, group]) => {
    const selected = group === 'always' || selection.groups.includes(group);
    const outcome = steps[id]?.outcome ?? 'missing';
    return { id, group, selected, outcome, passed: !selected || outcome === 'success' };
  });
}
