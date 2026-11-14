// Two headless editor clients with one shape: an LSP client (TypeScript 7's `tsc --lsp`) and a tsserver-protocol client (VS Code's bundled TypeScript).
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { readFileSync } from "node:fs";
import { pathToFileURL, fileURLToPath } from "node:url";
import type { Server } from "./locate.ts";

/** 1-based line and column, as editors show them. */
export interface Position { readonly line: number; readonly col: number; }
export interface Location { readonly file: string; readonly start: Position; readonly end: Position; }
export interface Diagnostic { readonly start: Position; readonly end: Position; readonly code: number; readonly message: string; readonly raw: unknown; }
export interface Edit { readonly file: string; readonly start: Position; readonly end: Position; readonly newText: string; }
export interface Fix { readonly title: string; readonly edits: readonly Edit[]; }
export interface Completion { readonly label: string; readonly kind: string; readonly insertText?: string; readonly detail?: string; readonly sortText?: string; }
export interface Rename { readonly canRename: boolean; readonly message?: string; readonly locations: readonly Location[]; }

export interface EditorClient {
  readonly description: string;
  open(file: string): Promise<void>;
  diagnostics(file: string): Promise<Diagnostic[]>;
  fixes(file: string, diagnostic: Diagnostic): Promise<Fix[]>;
  completions(file: string, at: Position): Promise<Completion[]>;
  details(file: string, at: Position, name: string): Promise<string>;
  hover(file: string, at: Position): Promise<string>;
  definition(file: string, at: Position): Promise<Location[]>;
  rename(file: string, at: Position, newName: string): Promise<Rename>;
  close(): Promise<void>;
}

/** Content-Length framed JSON messages on a stream. */
function readFrames(child: ChildProcessWithoutNullStreams, onMessage: (message: Record<string, unknown>) => void): void {
  let buffer = Buffer.alloc(0);
  child.stdout.on("data", (chunk: Buffer) => {
    buffer = Buffer.concat([buffer, chunk]);
    for (;;) {
      const headerEnd = buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;

      const header = buffer.subarray(0, headerEnd).toString("ascii");
      const length = Number(/Content-Length: (\d+)/i.exec(header)?.[1]);
      if (buffer.length < headerEnd + 4 + length) return;

      const body = buffer.subarray(headerEnd + 4, headerEnd + 4 + length).toString("utf8");
      buffer = buffer.subarray(headerEnd + 4 + length);
      onMessage(JSON.parse(body) as Record<string, unknown>);
    }
  });
}

function startProcess(server: Server, cwd: string): ChildProcessWithoutNullStreams {
  const child = spawn(server.command, [...server.args], { cwd, env: { ...process.env, ...server.env }, stdio: "pipe", windowsHide: true });
  child.stderr.on("data", () => { /* servers log here; ignored */ });
  return child;
}

function withTimeout<T>(promise: Promise<T>, what: string, ms = 30_000): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timeout: ${what}`)), ms);
    promise.then(v => { clearTimeout(timer); resolve(v); }, (e: unknown) => { clearTimeout(timer); reject(e as Error); });
  });
}

// ---------------------------------------------------------------- LSP

interface LspRange { readonly start: { line: number; character: number }; readonly end: { line: number; character: number }; }
const fromLsp = (p: { line: number; character: number }): Position => ({ line: p.line + 1, col: p.character + 1 });
const toLsp = (p: Position) => ({ line: p.line - 1, character: p.col - 1 });
const completionKinds = ["", "Text", "Method", "Function", "Constructor", "Field", "Variable", "Class", "Interface", "Module", "Property", "Unit", "Value", "Enum", "Keyword", "Snippet", "Color", "File", "Reference", "Folder", "EnumMember", "Constant", "Struct", "Event", "Operator", "TypeParameter"];

export async function startLsp(server: Server, root: string): Promise<EditorClient> {
  const child = startProcess(server, root);
  let seq = 0;
  const pending = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
  const pushed = new Map<string, unknown[]>();

  const send = (message: object): void => {
    const body = JSON.stringify({ jsonrpc: "2.0", ...message });
    child.stdin.write(`Content-Length: ${Buffer.byteLength(body)}\r\n\r\n${body}`);
  };
  const request = <T>(method: string, params: unknown): Promise<T> => withTimeout(new Promise<T>((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    send(params === undefined ? { id, method } : { id, method, params });
  }), method);
  const notify = (method: string, params?: unknown): void => send(params === undefined ? { method } : { method, params });

  readFrames(child, message => {
    if ("id" in message && "method" in message) {
      // A server-to-client request: answer with neutral values.
      const method = message["method"] as string;
      if (process.env["PROBE_DEBUG"]) console.error("server request", method, JSON.stringify(message["params"]));
      const items = (message["params"] as { items?: unknown[] } | undefined)?.items;
      const configuration = process.env["PROBE_TSGO_CONFIG"] ? JSON.parse(process.env["PROBE_TSGO_CONFIG"]) as unknown : null;
      send({ id: message["id"], result: method === "workspace/configuration" ? (items ?? []).map(() => configuration) : null });
      return;
    }

    if ("id" in message) {
      const waiter = pending.get(message["id"] as number);
      pending.delete(message["id"] as number);
      if (message["error"]) waiter?.reject(new Error(JSON.stringify(message["error"])));
      else waiter?.resolve(message["result"]);
      return;
    }

    if (message["method"] === "textDocument/publishDiagnostics") {
      const params = message["params"] as { uri: string; diagnostics: unknown[] };
      pushed.set(params.uri, params.diagnostics);
    }
  });

  const rootUri = pathToFileURL(root).href;
  const init = await request<{ serverInfo?: { name: string; version?: string } }>("initialize", {
    processId: process.pid,
    rootUri,
    workspaceFolders: [{ uri: rootUri, name: "ts-results" }],
    capabilities: {
      workspace: { configuration: true, workspaceFolders: true, workspaceEdit: { documentChanges: true } },
      textDocument: {
        synchronization: { didSave: true },
        completion: { completionItem: { snippetSupport: true, labelDetailsSupport: true, insertReplaceSupport: true, resolveSupport: { properties: ["documentation", "detail", "additionalTextEdits"] } }, contextSupport: true },
        hover: { contentFormat: ["markdown", "plaintext"] },
        definition: { linkSupport: true },
        rename: { prepareSupport: true },
        codeAction: { codeActionLiteralSupport: { codeActionKind: { valueSet: ["", "quickfix", "refactor", "source"] } }, resolveSupport: { properties: ["edit"] }, dataSupport: true },
        publishDiagnostics: { relatedInformation: true },
        diagnostic: { dynamicRegistration: false },
      },
    },
  });
  notify("initialized", {});

  const uri = (file: string) => pathToFileURL(file).href;
  let lastItems = new Map<string, unknown>();
  const toLocation = (l: { uri?: string; targetUri?: string; range?: LspRange; targetSelectionRange?: LspRange }): Location => {
    const range = (l.targetSelectionRange ?? l.range)!;
    return { file: fileURLToPath(l.targetUri ?? l.uri!), start: fromLsp(range.start), end: fromLsp(range.end) };
  };
  const toDiagnostic = (d: { range: LspRange; code?: number | string; message: string }): Diagnostic =>
    ({ start: fromLsp(d.range.start), end: fromLsp(d.range.end), code: Number(d.code), message: d.message, raw: d });
  const toEdits = (edit: { changes?: Record<string, { range: LspRange; newText: string }[]>; documentChanges?: { textDocument: { uri: string }; edits: { range: LspRange; newText: string }[] }[] } | undefined): Edit[] => {
    if (!edit) return [];
    const byUri = edit.changes ?? Object.fromEntries((edit.documentChanges ?? []).map(c => [c.textDocument.uri, c.edits]));
    return Object.entries(byUri).flatMap(([u, edits]) => edits.map(e => ({ file: fileURLToPath(u), start: fromLsp(e.range.start), end: fromLsp(e.range.end), newText: e.newText })));
  };

  return {
    description: `${server.name} ${server.version} (serverInfo: ${init.serverInfo?.name ?? "?"} ${init.serverInfo?.version ?? ""})`.trim(),

    async open(file) {
      notify("textDocument/didOpen", { textDocument: { uri: uri(file), languageId: file.endsWith(".tsx") ? "typescriptreact" : "typescript", version: 1, text: readFileSync(file, "utf8") } });
    },

    async diagnostics(file) {
      try {
        const report = await request<{ items?: { range: LspRange; code?: number; message: string }[] }>("textDocument/diagnostic", { textDocument: { uri: uri(file) } });
        return (report.items ?? []).map(toDiagnostic);
      } catch {
        for (let i = 0; i < 100 && !pushed.has(uri(file)); i++) await new Promise(r => setTimeout(r, 100));
        return ((pushed.get(uri(file)) ?? []) as { range: LspRange; code?: number; message: string }[]).map(toDiagnostic);
      }
    },

    async fixes(file, diagnostic) {
      const raw = diagnostic.raw as { range: LspRange };
      const actions = await request<({ title: string; edit?: Parameters<typeof toEdits>[0]; data?: unknown; command?: unknown; kind?: string })[] | null>("textDocument/codeAction", {
        textDocument: { uri: uri(file) }, range: raw.range, context: { diagnostics: [raw], triggerKind: 1 },
      });
      if (process.env["PROBE_DEBUG"]) console.error(JSON.stringify(actions).slice(0, 1500));
      const fixes: Fix[] = [];
      for (const action of actions ?? []) {
        let edit = action.edit;
        if (!edit && action.data !== undefined) edit = (await request<{ edit?: Parameters<typeof toEdits>[0] }>("codeAction/resolve", action)).edit;
        fixes.push({ title: action.title, edits: toEdits(edit) });
      }

      return fixes;
    },

    async completions(file, at) {
      const result = await request<{ items: { label: string; kind?: number; insertText?: string; textEdit?: { newText: string }; detail?: string; sortText?: string; labelDetails?: { detail?: string; description?: string } }[] } | { label: string }[] | null>(
        "textDocument/completion", { textDocument: { uri: uri(file) }, position: toLsp(at), context: { triggerKind: 1 } });
      const items = Array.isArray(result) ? result : result?.items ?? [];
      lastItems = new Map(items.map(i => [i.label, i]));
      return items.map(i => {
        const item = i as { label: string; kind?: number; insertText?: string; textEdit?: { newText: string }; detail?: string; sortText?: string; labelDetails?: { detail?: string; description?: string } };
        const insert = item.textEdit?.newText ?? item.insertText;
        const detail = item.detail ?? item.labelDetails?.description ?? item.labelDetails?.detail;
        return { label: item.label + (item.labelDetails?.detail ?? ""), kind: completionKinds[item.kind ?? 0] ?? String(item.kind), ...(insert !== undefined && insert !== item.label ? { insertText: insert } : {}), ...(detail !== undefined ? { detail } : {}), ...(item.sortText !== undefined ? { sortText: item.sortText } : {}) };
      });
    },

    async details(_file, _at, name) {
      const item = lastItems.get(name);
      if (!item) return "(no such item)";
      const resolved = await request<{ detail?: string; documentation?: string | { value: string } }>("completionItem/resolve", item);
      const docs = typeof resolved.documentation === "string" ? resolved.documentation : resolved.documentation?.value ?? "";
      return `${resolved.detail ?? ""}${docs ? `
---
${docs}` : ""}`;
    },

    async hover(file, at) {
      const result = await request<{ contents: { value: string } | string } | null>("textDocument/hover", { textDocument: { uri: uri(file) }, position: toLsp(at) });
      if (!result) return "(none)";
      return typeof result.contents === "string" ? result.contents : result.contents.value;
    },

    async definition(file, at) {
      const result = await request<unknown>("textDocument/definition", { textDocument: { uri: uri(file) }, position: toLsp(at) });
      const list = (Array.isArray(result) ? result : result ? [result] : []) as Parameters<typeof toLocation>[0][];
      return list.map(toLocation);
    },

    async rename(file, at, newName) {
      let prepared: unknown;
      try {
        prepared = await request<unknown>("textDocument/prepareRename", { textDocument: { uri: uri(file) }, position: toLsp(at) });
      } catch (e) {
        return { canRename: false, message: (e as Error).message, locations: [] };
      }

      if (!prepared) return { canRename: false, message: "prepareRename returned null", locations: [] };
      const edit = await request<Parameters<typeof toEdits>[0]>("textDocument/rename", { textDocument: { uri: uri(file) }, position: toLsp(at), newName });
      return { canRename: true, locations: toEdits(edit).map(e => ({ file: e.file, start: e.start, end: e.end })) };
    },

    async close() {
      try {
        await withTimeout(request("shutdown", undefined), "shutdown", 5000);
        notify("exit");
      } finally {
        await new Promise<void>(resolve => {
          const timer = setTimeout(() => { child.kill(); resolve(); }, 3000);
          child.once("exit", () => { clearTimeout(timer); resolve(); });
        });
      }
    },
  };
}

// ---------------------------------------------------------------- tsserver

interface TsSpan { readonly start: { line: number; offset: number }; readonly end: { line: number; offset: number }; }
const fromTs = (p: { line: number; offset: number }): Position => ({ line: p.line, col: p.offset });

export async function startTsserver(server: Server, root: string): Promise<EditorClient> {
  const child = startProcess(server, root);
  let seq = 0;
  const pending = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();

  readFrames(child, message => {
    if (message["type"] !== "response") return;
    const waiter = pending.get(message["request_seq"] as number);
    pending.delete(message["request_seq"] as number);
    if (message["success"] === false) waiter?.reject(new Error(String(message["message"])));
    else waiter?.resolve(message["body"]);
  });

  const request = <T>(command: string, args: unknown): Promise<T> => withTimeout(new Promise<T>((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    child.stdin.write(JSON.stringify({ seq: id, type: "request", command, arguments: args }) + "\n");
  }), command);

  // The preferences VS Code sends by default (typescript-language-features, defaults).
  await request("configure", {
    hostInfo: "vscode",
    preferences: {
      includeCompletionsForModuleExports: true,
      includeCompletionsWithInsertText: true,
      includeCompletionsWithSnippetText: true,
      includeCompletionsWithClassMemberSnippets: true,
      includeCompletionsWithObjectLiteralMethodSnippets: true,
      useLabelDetailsInCompletionEntries: true,
      allowIncompleteCompletions: true,
      allowRenameOfImportPath: true,
      providePrefixAndSuffixTextForRename: true,
      displayPartsForJSDoc: true,
      generateReturnInDocTemplate: true,
    },
  });

  const toEdits = (changes: { fileName: string; textChanges: (TsSpan & { newText: string })[] }[]): Edit[] =>
    changes.flatMap(c => c.textChanges.map(t => ({ file: c.fileName, start: fromTs(t.start), end: fromTs(t.end), newText: t.newText })));
  const status = await request<{ version: string }>("status", {}).catch(() => ({ version: "?" }));

  return {
    description: `${server.name} ${server.version} (status: ${status.version}, host ${server.command.endsWith("Code.exe") ? "VS Code Electron as Node" : "node"})`,

    async open(file) {
      await request("open", { file, projectRootPath: root, scriptKindName: file.endsWith(".tsx") ? "TSX" : "TS" }).catch(() => undefined);
    },

    async diagnostics(file) {
      const syntactic = await request<(TsSpan & { text: string; code: number })[]>("syntacticDiagnosticsSync", { file });
      const semantic = await request<(TsSpan & { text: string; code: number })[]>("semanticDiagnosticsSync", { file });
      return [...syntactic, ...semantic].map(d => ({ start: fromTs(d.start), end: fromTs(d.end), code: d.code, message: d.text, raw: d }));
    },

    async fixes(file, diagnostic) {
      const fixes = await request<{ description: string; fixName: string; changes: Parameters<typeof toEdits>[0] }[]>("getCodeFixes", {
        file, startLine: diagnostic.start.line, startOffset: diagnostic.start.col, endLine: diagnostic.end.line, endOffset: diagnostic.end.col, errorCodes: [diagnostic.code],
      });
      return fixes.map(f => ({ title: `${f.description} [${f.fixName}]`, edits: toEdits(f.changes) }));
    },

    async completions(file, at) {
      const info = await request<{ entries: { name: string; kind: string; insertText?: string; sortText?: string; labelDetails?: { detail?: string; description?: string } }[] } | undefined>("completionInfo", {
        file, line: at.line, offset: at.col, includeExternalModuleExports: false, includeInsertTextCompletions: true, triggerKind: 1,
      });
      return (info?.entries ?? []).map(e => ({
        label: e.name + (e.labelDetails?.detail ?? ""),
        kind: e.kind,
        ...(e.insertText !== undefined ? { insertText: e.insertText } : {}),
        ...(e.labelDetails?.description !== undefined ? { detail: e.labelDetails.description } : {}),
        ...(e.sortText !== undefined ? { sortText: e.sortText } : {}),
      }));
    },

    async details(file, at, name) {
      const details = await request<{ displayParts: { text: string }[]; documentation: { text: string }[] }[]>("completionEntryDetails", { file, line: at.line, offset: at.col, entryNames: [name] });
      const first = details[0];
      if (!first) return "(none)";
      const docs = first.documentation.map(p => p.text).join("");
      return `${first.displayParts.map(p => p.text).join("")}${docs ? `
---
${docs}` : ""}`;
    },

    async hover(file, at) {
      const info = await request<{ displayString: string; documentation: string | { text: string }[] } | undefined>("quickinfo", { file, line: at.line, offset: at.col }).catch(() => undefined);
      if (!info) return "(none)";
      const docs = typeof info.documentation === "string" ? info.documentation : info.documentation.map(p => p.text).join("");
      return docs ? `${info.displayString}\n---\n${docs}` : info.displayString;
    },

    async definition(file, at) {
      const result = await request<{ definitions: (TsSpan & { file: string })[] } | undefined>("definitionAndBoundSpan", { file, line: at.line, offset: at.col });
      return (result?.definitions ?? []).map(d => ({ file: d.file, start: fromTs(d.start), end: fromTs(d.end) }));
    },

    async rename(file, at) {
      const result = await request<{ info: { canRename: boolean; localizedErrorMessage?: string }; locs: { file: string; locs: TsSpan[] }[] }>("rename", { file, line: at.line, offset: at.col, findInStrings: false, findInComments: false });
      const locations = (result.locs ?? []).flatMap(f => f.locs.map(l => ({ file: f.file, start: fromTs(l.start), end: fromTs(l.end) })));
      return { canRename: result.info.canRename, ...(result.info.localizedErrorMessage !== undefined ? { message: result.info.localizedErrorMessage } : {}), locations };
    },

    async close() {
      child.stdin.write(JSON.stringify({ seq: ++seq, type: "request", command: "exit" }) + "\n");
      await new Promise<void>(resolve => {
        const timer = setTimeout(() => { child.kill(); resolve(); }, 3000);
        child.once("exit", () => { clearTimeout(timer); resolve(); });
      });
    },
  };
}
