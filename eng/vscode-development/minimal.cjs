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
    const inspect = async (operation, selector, text) => {
      const ready = events.findLast(item => item.kind === "application-ready");
      assert(ready, "Native document readiness required");
      const id = require("node:crypto").randomUUID();
      api.send({ command: "inspect", id, detail: operation, selector, text, timeoutMilliseconds: 5000,
        session: ready.session, generation: ready.generation, document: ready.document, contract: ready.contract });
      const result = await waitFor(() => events.find(item => item.correlation === id &&
        ["command-completed", "command-rejected"].includes(item.kind)), "inspection completion", 30);
      assert.equal(result.code, "ok", result.detail);
      return JSON.parse(result.detail).result;
    };
    await waitFor(() => events.some(item => item.kind === "application-ready"), "ordinary app readiness");
    await waitFor(async () => (await inspect("snapshot")).text.includes("Hello, Spar"), "typed greeting");
    const initial = events.findLast(item => item.kind === "application-ready");
    await inspect("click", "#local");
    await inspect("click", "#backend");
    await waitFor(async () => (await inspect("snapshot")).text.includes("Backend counter: 1"), "mutation completion");
    await new Promise(resolve => setTimeout(resolve, 2000));
    await edit(vscode.Uri.file(path.join(root, "backend/frontend/src/styles.css")), "#70e3bd", "#e9b44c");
    await edit(source, '"Hello, " + request.Name', '"Hello debugger, " + request.Name');
    await waitFor(async () => (await inspect("snapshot")).text.includes("Hello debugger, Spar"), "debugger-owned patch");
    const patched = await inspect("snapshot");
    assert(patched.text.includes("Backend counter: 1") && patched.text.includes("Local counter: 1"));
    assert.equal(events.findLast(item => item.kind === "application-ready").document, initial.document);
    assert(!events.some(item => item.kind === "ui-snapshot"), "No fixture reports");
    receipt.patched = patched;
    if (options.verifyRender) {
      fs.writeFileSync(path.join(root, "capture.json"), JSON.stringify({ window: events.findLast(item => item.kind === "window-created").window }));
      await waitFor(() => fs.existsSync(path.join(root, "capture.done")), "desktop capture", 90);
    }
    await edit(source, '"Hello debugger, " + request.Name', 'MissingGreetingSymbol + request.Name');
    await waitFor(async () => (await inspect("snapshot")).text.includes("CS0103"), "compiler diagnostic");
    await edit(source, 'MissingGreetingSymbol + request.Name', '"Hello corrected, " + request.Name');
    await waitFor(async () => (await inspect("snapshot")).text.includes("Hello corrected, Spar"), "correction patch");
    vscode.debug.addBreakpoints([breakpoint]);
    await vscode.commands.executeCommand("spar.restart");
    const restartedStop = await waitFor(() => stops.find(item => item.reason === "breakpoint" && item.session.configuration.sparGeneration === 2), "breakpoint after relaunch");
    vscode.debug.removeBreakpoints([breakpoint]);
    await restartedStop.session.customRequest("continue", { threadId: restartedStop.threadId });
    await waitFor(() => events.some(item => item.kind === "application-ready" && item.generation === 2), "restart readiness");
    await waitFor(async () => (await inspect("snapshot")).text.includes("Hello corrected, Spar"), "rebuilt greeting");
    assert((await inspect("snapshot")).text.includes("Backend counter: 0"));
    await vscode.debug.stopDebugging(vscode.debug.activeDebugSession);
    await waitFor(() => events.some(item => item.kind === "owned-processes-ended"), "owned process completion");
    await waitFor(() => events.some(item => item.kind === "coordinator-exited"), "coordinator exit");
    assert(!events.some(item => item.kind === "cleanup-failed"));
    assert.equal(events.find(item => item.kind === "coordinator-exited").code, 0);
    receipt.outcome = "passed";
    receipt.lane = "minimal-application-public-inspection";
    receipt.rendering = options.verifyRender ? "pixels-captured-review-required" : "not-exercised";
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
