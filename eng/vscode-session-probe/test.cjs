const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');

exports.run = async () => {
  const folder = vscode.workspace.workspaceFolders[0];
  const root = folder.uri.fsPath;
  const config = JSON.parse(fs.readFileSync(path.join(root, 'probe.json'), 'utf8').replace(/^\uFEFF/, ''));
  const log = value => fs.appendFileSync(path.join(root, 'editor.jsonl'), JSON.stringify({
    at: new Date().toISOString(), vscode: vscode.version, ...value,
  }) + '\n');
  let execution;
  let ended = false;
  const subscription = vscode.tasks.onDidEndTask(event => {
    if (event.execution === execution) { ended = true; log({ kind: 'task-ended' }); }
  });
  const waitFor = async (predicate, description) => {
    const deadline = Date.now() + 90_000;
    while (!predicate()) {
      if (Date.now() > deadline) throw new Error('Timed out: ' + description);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
  };
  try {
    const args = [config.session, '--owner-stdin', '--events-file', config.events];
    if (!config.watchBackend) args.push('--no-build');
    const task = new vscode.Task({ type: 'spar-session-probe' }, folder, 'Spar session probe', 'Spar',
      new vscode.ProcessExecution(config.coordinator,
        args, { cwd: config.repository }), []);
    execution = await vscode.tasks.executeTask(task);
    log({ kind: 'task-started', scenario: config.scenario });
    // The external driver must retain actual process handles before the editor ends ownership.
    await waitFor(() => fs.existsSync(path.join(root, 'proceed')), 'external process identity observation');
    log({ kind: 'owner-action-requested', scenario: config.scenario });
    if (config.scenario === 'task-stop') {
      execution.terminate();
      await waitFor(() => ended, 'VS Code task termination');
      log({ kind: 'completed' });
    } else {
      await vscode.commands.executeCommand('workbench.action.closeWindow');
    }
  } catch (error) {
    log({ kind: 'failed', message: String(error.stack || error) });
    execution?.terminate();
    throw error;
  } finally {
    subscription.dispose();
  }
};
