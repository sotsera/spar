const vscode = require("vscode");
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");

exports.run = async () => {
  const folder = vscode.workspace.workspaceFolders[0];
  const root = folder.uri.fsPath;
  const samples = [];
  const stops = [];
  const exits = [];
  const sessions = new Map();
  const buffers = new Map();
  const logPath = path.join(root, "debugger.ndjson");
  const receipt = {
    vscode: vscode.version,
    csharp: vscode.extensions.getExtension("ms-dotnettools.csharp")?.packageJSON.version,
    devkit: vscode.extensions.getExtension("ms-dotnettools.csdevkit")?.packageJSON.version,
    outcome: "running",
  };
  const log = (event) => fs.appendFileSync(logPath, JSON.stringify({ at: new Date().toISOString(), ...event }) + "\n");
  const bounded = async (operation, description, milliseconds = 30_000) => {
    let timer;
    try {
      return await Promise.race([
        operation,
        new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("Timed out: " + description)), milliseconds); }),
      ]);
    } finally {
      clearTimeout(timer);
    }
  };
  const waitFor = async (predicate, description, milliseconds = 75_000) => {
    const deadline = Date.now() + milliseconds;
    while (Date.now() < deadline) {
      const value = predicate();
      if (value) return value;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error("Timed out: " + description + "; see " + logPath);
  };
  const findSample = (predicate) => samples.findLast(predicate);
  const tracker = vscode.debug.registerDebugAdapterTrackerFactory("*", {
    createDebugAdapterTracker(session) {
      sessions.set(session.id, session);
      return {
        onDidSendMessage(message) {
          if (message.type !== "event") return;
          if (["stopped", "process", "terminated", "exited"].includes(message.event)) {
            log({ session: session.id, event: message.event, body: message.body });
          }
          if (message.event === "stopped") stops.push({ session, ...message.body });
          if (message.event === "exited") exits.push({ session: session.id, ...message.body });
          if (message.event !== "output") return;
          const text = message.body?.output || "";
          log({ session: session.id, event: "output", category: message.body?.category, text });
          const lines = ((buffers.get(session.id) || "") + text).split(/\r?\n/);
          buffers.set(session.id, lines.pop());
          for (const line of lines) {
            if (!line.trim().startsWith("{")) continue;
            try {
              const sample = JSON.parse(line.trim());
              if (Number.isInteger(sample.processId) && sample.instanceId) samples.push(sample);
            } catch { /* debugger output need not be JSON */ }
          }
        },
      };
    },
  });
  const greetingUri = vscode.Uri.file(path.join(root, "GreetingProvider.cs"));
  const breakpoint = new vscode.SourceBreakpoint(new vscode.Location(greetingUri, new vscode.Position(6, 0)));
  const configuration = {
    name: "Spar debugger validation",
    type: "dotnet",
    request: "launch",
    // VS Code lowercases drive letters in file URIs; keep the build and project-system paths identical.
    projectPath: path.join(root, "Spar.HotReloadProbe.csproj").replace(/^[a-z]:/, (drive) => drive.toUpperCase()),
  };
  const replace = async (uri, oldText, newText) => {
    log({ phase: "edit", file: path.basename(uri.fsPath), oldText, newText });
    const document = await vscode.workspace.openTextDocument(uri);
    const source = document.getText();
    assert(source.includes(oldText), "Fixture text to replace must exist");
    const edit = new vscode.WorkspaceEdit();
    edit.replace(uri, new vscode.Range(document.positionAt(0), document.positionAt(source.length)), source.replace(oldText, newText));
    assert(await vscode.workspace.applyEdit(edit));
    assert(await bounded(document.save(), "save fixture"));
    log({ phase: "saved", file: path.basename(uri.fsPath) });
  };
  try {
    await bounded(vscode.extensions.getExtension("ms-dotnettools.csharp").activate(), "C# activation");
    await bounded(vscode.extensions.getExtension("ms-dotnettools.csdevkit").activate(), "Dev Kit activation");
    // Wait for the real project system before opening C# files as loose file-based programs.
    let projectTasks = [];
    const projectDeadline = Date.now() + 60_000;
    do {
      projectTasks = await bounded(vscode.tasks.fetchTasks({ type: "dotnet" }), "project tasks");
      if (projectTasks.length) break;
      await new Promise((resolve) => setTimeout(resolve, 250));
    } while (Date.now() < projectDeadline);
    log({ phase: "project tasks", names: projectTasks.map((task) => task.name) });
    assert(projectTasks.length, "Dev Kit must discover the fixture project");
    await vscode.window.showTextDocument(greetingUri);
    // Activation returns before the project/language services have finished loading.
    const servicesDeadline = Date.now() + 75_000;
    let symbols;
    do {
      symbols = await vscode.commands.executeCommand("vscode.executeDocumentSymbolProvider", greetingUri);
      if (symbols?.length) break;
      await new Promise((resolve) => setTimeout(resolve, 250));
    } while (Date.now() < servicesDeadline);
    assert(symbols?.length, "C# language services must load the fixture");
    assert.equal(vscode.workspace.getConfiguration("csharp.experimental.debug").get("hotReload"), true);
    vscode.debug.addBreakpoints([breakpoint]);
    assert(await bounded(vscode.debug.startDebugging(folder, configuration), "debugger launch", 60_000), "Debugger launch must be accepted");
    const stopped = await waitFor(() => stops.find((event) => event.reason === "breakpoint"), "real backend breakpoint");
    receipt.breakpointHit = true;
    vscode.debug.removeBreakpoints([breakpoint]);
    await stopped.session.customRequest("continue", { threadId: stopped.threadId });
    const before = await waitFor(() => findSample((sample) => sample.counter >= 3), "initial debugger samples");
    receipt.before = before;
    await new Promise((resolve) => setTimeout(resolve, 2_000));

    await replace(greetingUri, "Hello from Spar v1", "Hello from the VS Code debugger");
    const after = await waitFor(
      () => findSample((sample) => sample.greeting === "Hello from the VS Code debugger"),
      "saved method-body edit applied by the debugger",
    );
    assert.equal(after.processId, before.processId);
    assert.equal(after.instanceId, before.instanceId);
    assert(after.counter > before.counter);
    receipt.after = after;
    // A patch can execute before the update handler runs. Observe each outcome separately.
    const callback = await waitFor(
      () => findSample((sample) => sample.greeting === after.greeting && sample.update.revision > before.update.revision),
      "metadata update callback after changed behavior",
    );
    receipt.callback = callback;
    receipt.metadataCallbacksObserved = true;

    await replace(greetingUri, 'return "Hello from the VS Code debugger";', "return ;");
    await waitFor(
      () => vscode.languages.getDiagnostics(greetingUri).some((diagnostic) => diagnostic.severity === vscode.DiagnosticSeverity.Error),
      "compiler diagnostic for invalid source",
    );
    const diagnosticAt = new Date().toISOString();
    const duringError = await waitFor(() => findSample((sample) => sample.observedAt > diagnosticAt), "live sample after compile failure");
    assert.equal(duringError.greeting, after.greeting);
    assert.equal(duringError.processId, before.processId);
    assert.equal(duringError.instanceId, before.instanceId);
    assert.equal(duringError.update.revision, callback.update.revision);
    receipt.duringError = duringError;
    await replace(greetingUri, "return ;", 'return "Hello after debugger correction";');
    const corrected = await waitFor(
      () => findSample((sample) => sample.greeting === "Hello after debugger correction"),
      "debugger compile-error recovery",
    );
    assert.equal(corrected.instanceId, before.instanceId);
    receipt.corrected = corrected;

    const stateUri = vscode.Uri.file(path.join(root, "ProbeState.cs"));
    await replace(stateUri, "class ProbeState : InitialProbeBase", "class ProbeState : ReplacementProbeBase");
    const rudeDiagnostic = await waitFor(
      () => {
        // Acceptance evidence only: Dev Kit writes rude edits to its output log rather than DAP output.
        // This file format is not a product control/status API.
        const logs = path.join(root, "..", "profile", "logs");
        const file = fs.readdirSync(logs, { recursive: true }).find((name) => name.endsWith(".NET Hot Reload.log"));
        if (!file) return undefined;
        return fs.readFileSync(path.join(logs, file), "utf8").split(/\r?\n/).find((line) => line.includes("error ENC0014:"));
      },
      "unsupported-edit diagnostic",
    );
    receipt.unsupportedEdit = { code: "ENC0014", diagnosticLogLine: rudeDiagnostic };

    // Public Stop/Start APIs exercise an explicit relaunch; automatic rude-edit restart is not promised.
    await bounded(vscode.debug.stopDebugging(stopped.session), "debugger Stop");
    await waitFor(() => !vscode.debug.activeDebugSession, "debugger Stop", 20_000);
    assert(await bounded(vscode.debug.startDebugging(folder, configuration), "debugger relaunch", 60_000), "Debugger relaunch must be accepted");
    const relaunched = await waitFor(() => findSample((sample) => sample.instanceId !== before.instanceId), "new application after relaunch");
    assert.notEqual(relaunched.processStartedAtUtc, before.processStartedAtUtc);
    receipt.relaunched = relaunched;
    receipt.outcome = "passed";
  } catch (error) {
    receipt.outcome = "failed";
    receipt.error = String(error.stack || error);
    throw error;
  } finally {
    vscode.debug.removeBreakpoints([breakpoint]);
    fs.writeFileSync(path.join(root, "receipt.json"), JSON.stringify(receipt, null, 2) + "\n");
    try {
      await bounded(vscode.debug.stopDebugging(), "debugger teardown");
      await waitFor(() => exits.length === sessions.size, "all debugger processes report exit", 20_000);
      receipt.exits = exits;
      fs.writeFileSync(path.join(root, "receipt.json"), JSON.stringify(receipt, null, 2) + "\n");
    } catch (error) {
      receipt.outcome = "failed";
      receipt.cleanupError = String(error.stack || error);
      fs.writeFileSync(path.join(root, "receipt.json"), JSON.stringify(receipt, null, 2) + "\n");
      throw error;
    } finally {
      tracker.dispose();
    }
  }
};
