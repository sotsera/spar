const vscode = require("vscode");
const { spawn } = require("node:child_process");
const readline = require("node:readline");
const path = require("node:path");

let owner;

exports.activate = (context) => {
  const output = vscode.window.createOutputChannel("Spar development");
  const events = new vscode.EventEmitter();
  const diagnostics = vscode.languages.createDiagnosticCollection("Spar build");
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left);
  status.command = "spar.restart";
  status.tooltip = "Restart the Spar backend after retiring its current work. Use the Spar command for coordinated restart.";
  const send = (command) => {
    if (owner && !owner.process.stdin.destroyed) owner.process.stdin.write(JSON.stringify(command) + "\n");
  };
  const stop = () => send({ command: "stop", force: true });
  const launch = async (frame) => {
    const current = owner;
    if (!current) return;
    current.generation = frame.generation;
    current.session = frame.session;
    current.retiring = false;
    // Capabilities are delivered only over the private stdin/stdout owner channel.
    const configuration = {
      name: "Spar native application", type: "dotnet", request: "launch",
      projectPath: frame.project, args: frame.arguments, env: frame.environment,
      sparSession: frame.session, sparGeneration: frame.generation,
      sparEnvironment: frame.environment, sparArguments: frame.arguments,
    };
    if (!await vscode.debug.startDebugging(current.folder, configuration) && owner === current) stop();
  };
  context.subscriptions.push(output, events, status, diagnostics,
    vscode.debug.registerDebugConfigurationProvider("coreclr", {
      resolveDebugConfigurationWithSubstitutedVariables(folder, configuration) {
        if (configuration.sparSession) {
          if (!owner || owner.session !== configuration.sparSession || owner.generation !== configuration.sparGeneration) return undefined;
          configuration.env = { ...configuration.env, ...configuration.sparEnvironment };
          configuration.args = configuration.sparArguments;
        }
        return configuration;
      },
    }),
    vscode.debug.registerDebugConfigurationProvider("spar", {
      resolveDebugConfiguration(folder, configuration) {
        if (owner) throw new Error("Stop the current Spar session before starting another.");
        const root = folder.uri.fsPath.replace(/^[a-z]:/, (drive) => drive.toUpperCase());
        const options = configuration.spar;
        const coordinator = path.resolve(root, options.coordinator);
        const settings = path.resolve(root, options.settings);
        const args = [settings, "--debugger", "--owner-stdin"];
        if (options.probe) args.push("--probe");
        if (options.inspect) args.push("--inspect");
        const process = spawn(coordinator, args, { cwd: root, windowsHide: true, stdio: "pipe" });
        const current = { process, folder, generation: 0, retiring: false, debug: undefined };
        owner = current;
        status.text = "Spar: starting";
        status.show();
        process.stdin.on("error", () => {});
        process.stderr.on("data", (text) => output.append(text.toString()));
        process.once("error", (error) => {
          if (owner === current) owner = undefined;
          status.hide();
          output.appendLine(error.message);
          output.show(true);
        });
        process.once("close", (code) => {
          if (owner === current) owner = undefined;
          status.hide();
          if (current.debug) vscode.debug.stopDebugging(current.debug);
          events.fire({ kind: "coordinator-exited", code });
        });
        readline.createInterface({ input: process.stdout }).on("line", (line) => {
          try {
            const event = JSON.parse(line);
            if (event.kind === "debug-launch") {
              launch(event).catch((error) => {
                output.appendLine(error.message);
                output.show(true);
                if (owner === current) stop();
              });
              return;
            }
            if (event.kind === "backend-restarting") current.retiring = true;
            if (event.kind === "source-selected") diagnostics.clear();
            if (event.kind === "build-diagnostic") {
              const match = /^(.*)\((\d+),(\d+)\): error (\w+): (.*)$/.exec(event.detail || "");
              if (match) {
                const uri = vscode.Uri.file(match[1]);
                const location = new vscode.Position(Math.max(0, Number(match[2]) - 1), Math.max(0, Number(match[3]) - 1));
                const diagnostic = new vscode.Diagnostic(new vscode.Range(location, location), match[5], vscode.DiagnosticSeverity.Error);
                diagnostic.code = match[4];
                diagnostic.source = "Spar build";
                diagnostics.set(uri, [...(diagnostics.get(uri) || []).slice(-15), diagnostic]);
              }
            }
            if (event.kind === "application-ready") status.text = "Spar: ready";
            if (event.kind === "backend-restarting") status.text = "Spar: restarting";
            if (event.kind === "backend-stopped") status.text = "Spar: build failed";
            if (event.kind === "debugger-diagnostic") status.text = "Spar: edit rejected";
            if (event.kind === "backend-updated") status.text = "Spar: updated";
            if (event.kind === "session-failed") output.show(true);
            output.appendLine(line);
            events.fire(event);
          } catch (error) { output.appendLine("Invalid coordinator event: " + error.message); stop(); }
        });
        // This F5 entry is a launcher. The authenticated handoff starts an ordinary C# session.
        // Resolving a dotnet configuration here would depend on other providers' registration order.
        return undefined;
      },
    }),
    vscode.debug.registerDebugAdapterTrackerFactory("*", {
      createDebugAdapterTracker(session) {
        if (!session.configuration.sparSession) return;
        let exited = false;
        return { onWillReceiveMessage(message) {
          if (message.type === "request" && ["disconnect", "terminate", "restart"].includes(message.command)
              && !exited && owner?.debug?.id === session.id && !owner.retiring) stop();
        }, onDidSendMessage(message) {
          if (message.type !== "event") return;
          if (message.event === "exited") exited = true;
          if (!owner || owner.session !== session.configuration.sparSession) return;
          if (message.event === "exited" && owner.debug?.id === session.id) owner.debugExited = true;
          if (message.event === "stopped" || message.event === "continued") {
            send({ command: "debugger-state", generation: session.configuration.sparGeneration,
              detail: message.event === "stopped" ? "paused" : "running" });
          }
        } };
      },
    }),
    vscode.debug.onDidStartDebugSession((session) => {
      if (owner && owner.session === session.configuration.sparSession && owner.generation === session.configuration.sparGeneration) {
        owner.debug = session;
        owner.debugExited = false;
      }
    }),
    vscode.debug.onDidTerminateDebugSession((session) => {
      if (!owner || owner.debug?.id !== session.id || owner.generation !== session.configuration.sparGeneration) return;
      send({ command: "debugger-state", generation: session.configuration.sparGeneration, detail: "ended" });
      if (!owner.retiring && !owner.debugExited) stop();
    }),
    vscode.languages.onDidChangeDiagnostics(({ uris }) => {
      if (!owner) return;
      for (const uri of uris.slice(0, 16)) {
        for (const diagnostic of vscode.languages.getDiagnostics(uri).filter((item) => item.severity === vscode.DiagnosticSeverity.Error).slice(0, 4)) {
          const detail = `${uri.fsPath}:${diagnostic.range.start.line + 1}: ${diagnostic.code?.value ?? diagnostic.code ?? "error"}: ${diagnostic.message}`;
          send({ command: "debugger-diagnostic", generation: owner.generation,
            detail: detail.slice(0, 2048) });
        }
      }
    }),
    vscode.commands.registerCommand("spar.restart", () => send({ command: "restart" })),
    vscode.commands.registerCommand("spar.stop", stop),
  );
  return { onEvent: events.event, send };
};

exports.deactivate = () => {
  // EOF is the coordinator's explicit owner-loss cleanup path.
  owner?.process.stdin.end();
};
