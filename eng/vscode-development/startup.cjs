const vscode = require("vscode");
const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");

exports.run = async () => {
  const folder = vscode.workspace.workspaceFolders[0];
  const root = folder.uri.fsPath;
  const configuration = JSON.parse(fs.readFileSync(path.join(root, "probe.json"), "utf8"));
  const receipt = { outcome: "running", vscode: vscode.version, cases: [], debugSessionsStarted: 0 };
  const events = [];
  const waitFor = async (predicate, description, seconds = 60) => {
    const deadline = Date.now() + seconds * 1000;
    while (!predicate()) {
      if (Date.now() >= deadline) throw new Error("Timed out: " + description);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  };
  const extension = vscode.extensions.getExtension("sotsera.spar-development");
  assert(extension, "Spar adapter was not loaded");
  const api = await extension.activate();
  const listener = api.onEvent((event) => events.push(event));
  const debuggerListener = vscode.debug.onDidStartDebugSession(() => receipt.debugSessionsStarted++);
  try {
    const cases = [
      { name: "missing-coordinator", coordinator: path.join(root, "absent.exe"), settings: configuration.missing },
      { name: "missing-configuration", settings: configuration.missing, code: "configuration_not_found" },
      { name: "malformed-configuration", settings: configuration.malformed, code: "configuration_invalid" },
      { name: "blocked-session-directory", settings: configuration.blocked, code: "session_directory_unavailable" },
    ];
    if (configuration.sessionFailure) cases.push({ name: "frontend-session-failure", settings: configuration.sessionFailure,
      sessionFailure: true, code: "frontend_start_failed" });
    for (const scenario of cases) {
      const first = events.length;
      await vscode.debug.startDebugging(folder, { type: "spar", request: "launch", name: "Spar startup failure",
        spar: { coordinator: scenario.coordinator || configuration.coordinator, settings: scenario.settings } });
      await waitFor(() => events.slice(first).some((event) => event.kind === "coordinator-exited"), scenario.name);
      const current = events.slice(first);
      const exited = current.find((event) => event.kind === "coordinator-exited");
      assert.notEqual(exited.code, 0);
      assert(!current.some((event) => event.kind === "application-ready"));
      assert.equal(current.some((event) => event.kind === "session-started"), !!scenario.sessionFailure);
      if (scenario.code) {
        const failure = current.find((event) => event.kind === (scenario.sessionFailure ? "session-failed" : "startup-error"));
        assert(failure);
        assert.equal(failure.code, scenario.code);
        if (scenario.sessionFailure) {
          assert.equal(failure.diagnostic.role, "frontend");
          assert.equal(failure.diagnostic.exitCode, 23);
          for (const log of [failure.diagnostic.stdoutLog, failure.diagnostic.stderrLog]) {
            assert(fs.existsSync(log));
            assert(failure.detail.includes(log));
          }
          assert(!JSON.stringify(current).includes("D2-private-child-output"));
          assert(current.some((event) => event.kind === "owned-processes-ended"));
        } else {
          assert(!("session" in failure) && !("generation" in failure));
          assert(failure.message.length <= 1024);
        }
      }
      receipt.cases.push({ name: scenario.name, code: scenario.code, exitCode: exited.code, passed: true });
    }
    assert.equal(receipt.debugSessionsStarted, 0);
    receipt.outcome = "passed";
    receipt.presentation = configuration.pauseForVisualReview ? "awaiting-visual-review" : "not-visually-reviewed";
    fs.writeFileSync(path.join(root, "receipt.json"), JSON.stringify(receipt, null, 2));
    if (configuration.pauseForVisualReview) {
      fs.writeFileSync(path.join(root, "ready-for-review"), "Output should have been revealed automatically.");
      await waitFor(() => fs.existsSync(path.join(root, "reviewed")), "actual Output panel visual review", 300);
      receipt.presentation = "output-panel-visually-reviewed";
    }
  } catch (error) {
    receipt.outcome = "failed";
    receipt.error = error.stack;
    throw error;
  } finally {
    fs.writeFileSync(path.join(root, "receipt.json"), JSON.stringify(receipt, null, 2));
    listener.dispose();
    debuggerListener.dispose();
    await vscode.commands.executeCommand("workbench.action.closeWindow");
  }
};
