const vscode = require("vscode");
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");

exports.run = async () => {
  const folder = vscode.workspace.workspaceFolders[0];
  const root = folder.uri.fsPath;
  const events = [];
  const stops = [];
  const receipt = { outcome: "running", vscode: vscode.version,
    csharp: vscode.extensions.getExtension("ms-dotnettools.csharp")?.packageJSON.version,
    devkit: vscode.extensions.getExtension("ms-dotnettools.csdevkit")?.packageJSON.version };
  const waitFor = async (predicate, description, seconds = 120) => {
    const deadline = Date.now() + seconds * 1000;
    while (Date.now() < deadline) {
      const result = await predicate();
      if (result) return result;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error("Timed out: " + description);
  };
  const source = vscode.Uri.file(path.join(root, "backend/SampleBackend.cs"));
  const edit = async (uri, before, after) => {
    const document = await vscode.workspace.openTextDocument(uri);
    const text = document.getText();
    assert(text.includes(before));
    const change = new vscode.WorkspaceEdit();
    change.replace(uri, new vscode.Range(document.positionAt(0), document.positionAt(text.length)), text.replace(before, after));
    assert(await vscode.workspace.applyEdit(change));
    assert(await document.save());
  };
  const snapshot = (generation = 1) => {
    const event = events.findLast((item) => item.kind === "ui-snapshot" && item.generation === generation);
    return event && JSON.parse(event.detail);
  };
  let api;
  const tracker = vscode.debug.registerDebugAdapterTrackerFactory("*", {
    createDebugAdapterTracker(session) {
      fs.appendFileSync(path.join(root, "debugger.ndjson"), JSON.stringify({ phase: "configuration", type: session.type,
        args: session.configuration.args, hasEnvironment: !!session.configuration.env?.SPAR_DEV_APP_PIPE }) + "\n");
      return { onDidSendMessage(message) {
      if (message.type !== "event") return;
      if (message.event === "stopped") stops.push({ session, ...message.body });
      if (["stopped", "exited", "output"].includes(message.event))
        fs.appendFileSync(path.join(root, "debugger.ndjson"), JSON.stringify(message) + "\n");
    } }; },
  });
  try {
    await vscode.extensions.getExtension("ms-dotnettools.csharp").activate();
    await vscode.extensions.getExtension("ms-dotnettools.csdevkit").activate();
    api = await vscode.extensions.getExtension("sotsera.spar-development").activate();
    api.onEvent((event) => {
      events.push(event);
      fs.appendFileSync(path.join(root, "coordinator.ndjson"), JSON.stringify(event) + "\n");
    });
    await waitFor(async () => (await vscode.tasks.fetchTasks({ type: "dotnet" })).length, "project system");
    const document = await vscode.workspace.openTextDocument(source);
    await vscode.window.showTextDocument(document);
    let symbols;
    for (let index = 0; index < 120; index++) {
      symbols = await vscode.commands.executeCommand("vscode.executeDocumentSymbolProvider", source);
      if (symbols?.length) break;
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
    assert(symbols?.length, "C# source loaded");
    const line = document.getText().split(/\r?\n/).findIndex((text) => text.includes('return Task.FromResult(new GreetingReply'));
    const breakpoint = new vscode.SourceBreakpoint(new vscode.Location(source, new vscode.Position(line, 0)));
    vscode.debug.addBreakpoints([breakpoint]);
    const options = JSON.parse(fs.readFileSync(path.join(root, "owner.json"), "utf8").replace(/^\uFEFF/, ""));
    await vscode.debug.startDebugging(folder, { name: "Spar integrated", type: "spar", request: "launch", spar: options });
    const stopped = await waitFor(() => stops.find((item) => item.reason === "breakpoint"), "native backend breakpoint");
    vscode.debug.removeBreakpoints([breakpoint]);
    await stopped.session.customRequest("continue", { threadId: stopped.threadId });
    await waitFor(() => snapshot(), "typed readiness");
    // Public typed readiness is not a C# Dev Kit Hot Reload initialization receipt.
    // Match the precursor's settling period; retain failures rather than retrying mutations.
    await new Promise((resolve) => setTimeout(resolve, 2000));
    api.send({ command: "probe", detail: "seed" });
    await waitFor(() => snapshot()?.counter === 2 && snapshot()?.local === 7, "state seed");
    const initial = snapshot();
    await edit(vscode.Uri.file(path.join(root, "backend/frontend/src/styles.css")), "#70e3bd", "#e9b44c");
    await waitFor(() => snapshot()?.color === "rgb(233, 180, 76)", "frontend HMR");
    await new Promise((resolve) => setTimeout(resolve, 2000));
    await edit(source, '"Hello, " + request.Name', '"Hello debugger, " + request.Name');
    await waitFor(() => snapshot()?.greeting === "Hello debugger, Spar" && snapshot()?.backendRevision > 0, "debugger-owned patch");
    assert.equal(snapshot().document, initial.document);
    assert.equal(snapshot().counter, 2);
    assert.equal(snapshot().local, 7);
    receipt.initial = initial;
    receipt.patched = snapshot();
    const window = events.findLast((item) => item.kind === "window-created").window;
    if (options.verifyRender) {
      fs.writeFileSync(path.join(root, "capture.json"), JSON.stringify({ window }));
      await waitFor(() => fs.existsSync(path.join(root, "capture.done")), "independent desktop capture", 90);
    }
    receipt.rendering = options.verifyRender ? "pixels-verified-review-capture" : "not-exercised";
    await edit(source, '"Hello debugger, " + request.Name', 'MissingGreetingSymbol + request.Name');
    await waitFor(() => snapshot()?.developmentStatus.includes("CS0103"), "source-linked compiler diagnostic in SPA");
    assert.equal(snapshot().greeting, "Hello debugger, Spar");
    await edit(source, 'MissingGreetingSymbol + request.Name', '"Hello corrected, " + request.Name');
    await waitFor(() => snapshot()?.greeting === "Hello corrected, Spar", "correction patch");
    api.send({ command: "probe", detail: "delay" });
    await waitFor(() => events.some((item) => item.kind === "backend-activity" && item.detail === "delay-entered"), "active mutation");
    await edit(source, ": InitialBackendBase, ISampleBackend", ": ReplacementBackendBase, ISampleBackend");
    receipt.unsupportedEdit = await waitFor(() => {
      // Evidence only. Product control never reads this private editor log format.
      const logs = path.join(root, "profile/logs");
      const name = fs.readdirSync(logs, { recursive: true }).find((file) => file.endsWith(".NET Hot Reload.log"));
      return name && fs.readFileSync(path.join(logs, name), "utf8").split(/\r?\n/).find((line) => line.includes("error ENC0014:"));
    }, "unsupported-edit diagnostic");
    assert(!events.some((item) => item.kind === "backend-activity" && item.detail === "delay-finished"), "Restart must begin during unfinished work");
    vscode.debug.addBreakpoints([breakpoint]);
    await vscode.commands.executeCommand("spar.restart");
    const restartedStop = await waitFor(() => stops.find((item) => item.reason === "breakpoint" && item.session.configuration.sparGeneration === 2), "breakpoint after debugger relaunch");
    vscode.debug.removeBreakpoints([breakpoint]);
    await restartedStop.session.customRequest("continue", { threadId: restartedStop.threadId });
    await waitFor(() => snapshot(2), "coordinated debugger relaunch");
    const finished = events.findIndex((item) => item.kind === "backend-activity" && item.detail === "delay-finished");
    const relaunched = events.findIndex((item) => item.kind === "debugger-process-owned" && item.generation === 2);
    assert(finished > 0 && finished < relaunched, "Retirement must finish before the next debugger process");
    assert.equal(snapshot(2).counter, 0);
    assert.equal(events.filter((item) => item.kind === "debugger-process-owned").length, 2);
    await edit(vscode.Uri.file(path.join(root, "contracts/BackendContract.cs")), "record CounterReply(int Value)", "record CounterReply(int Value, int Revision)");
    await waitFor(() => events.some((item) => item.kind === "backend-stopped" && item.code === "build_failed"), "contract rebuild failure stays stopped");
    const buildDiagnostic = await waitFor(() => vscode.languages.getDiagnostics(source).find((item) => item.source === "Spar build"), "build diagnostic with source location");
    await vscode.window.showTextDocument(source, { selection: buildDiagnostic.range });
    assert.equal(vscode.window.activeTextEditor.document.uri.toString(), source.toString());
    receipt.buildDiagnostic = { code: buildDiagnostic.code, line: buildDiagnostic.range.start.line + 1 };
    assert(!events.some((item) => item.kind === "contract-update-observed"), "Contracts must never enter debugger delta application");
    await edit(source, "new CounterReply(Interlocked.Add(ref _counter, request.Delta))", "new CounterReply(Interlocked.Add(ref _counter, request.Delta), 7)");
    await waitFor(() => snapshot(4), "corrected contract rebuild and relaunch");
    assert.notEqual(snapshot(4).contract, initial.contract);
    receipt.contractRestart = snapshot(4);
    await vscode.debug.stopDebugging(vscode.debug.activeDebugSession);
    await waitFor(() => events.some((item) => item.kind === "owned-processes-ended"), "actual process cleanup");
    await waitFor(() => events.some((item) => item.kind === "coordinator-exited"), "coordinator exit");
    assert(!events.some((item) => item.kind === "cleanup-failed"));
    assert.equal(events.find((item) => item.kind === "coordinator-exited").code, 0);
    const closeStart = events.length;
    await vscode.debug.startDebugging(folder, { name: "Spar native close", type: "spar", request: "launch", spar: options });
    await waitFor(() => events.slice(closeStart).some((item) => item.kind === "application-ready"), "second session readiness");
    const closingWindow = events.slice(closeStart).find((item) => item.kind === "window-created").window;
    fs.writeFileSync(path.join(root, "native-close.json"), JSON.stringify({ window: closingWindow }));
    await waitFor(() => events.slice(closeStart).some((item) => item.kind === "coordinator-exited"), "native close cleanup");
    assert.equal(events.slice(closeStart).find((item) => item.kind === "session-stopped").code, "graceful");
    assert.equal(events.slice(closeStart).find((item) => item.kind === "coordinator-exited").code, 0);
    receipt.nativeClose = "graceful";
    receipt.outcome = "passed";
  } catch (error) {
    receipt.outcome = "failed";
    receipt.error = error.stack;
    throw error;
  } finally {
    await vscode.debug.stopDebugging();
    api?.send({ command: "stop", force: true });
    if (api) await waitFor(() => events.some((item) => item.kind === "coordinator-exited"), "coordinator exit", 30).catch(() => {});
    tracker.dispose();
    receipt.events = events.length;
    fs.writeFileSync(path.join(root, "receipt.json"), JSON.stringify(receipt, null, 2));
  }
};
