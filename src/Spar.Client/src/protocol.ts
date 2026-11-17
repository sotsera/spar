// Encoding C (plan section 2c, "The messages"): a JSON header, a TAB, then the payload's own JSON.
// The same rules as the C# codec, checked by the shared vectors in tests/conformance/bridge-messages.json.

export const protocolVersion = 1;

export type MessageType =
  | "hello"
  | "call"
  | "stream"
  | "credits"
  | "cancel"
  | "ready"
  | "closing-done"
  | "pong"
  | "csp"
  | "open"
  | "result"
  | "items"
  | "end"
  | "failure"
  | "event"
  | "closing"
  | "ping";

const knownTypes: ReadonlySet<string> = new Set<MessageType>([
  "hello", "call", "stream", "credits", "cancel", "ready", "closing-done", "pong", "csp",
  "open", "result", "items", "end", "failure", "event", "closing", "ping",
]);

/** One bridge message. Fields a type does not use are absent; the payload is located, never parsed. */
export interface BridgeMessage {
  readonly type: MessageType | "unknown";
  /** The type name as received, for an unknown type. */
  readonly typeName?: string;
  readonly id?: number;
  readonly protocol?: number;
  readonly types?: readonly string[];
  readonly contracts?: string;
  readonly doc?: string;
  readonly contract?: string;
  readonly command?: string;
  readonly traceparent?: string;
  readonly tracestate?: string;
  readonly credits?: number;
  readonly ok?: boolean;
  readonly dropped?: number;
  readonly kind?: string;
  readonly code?: string;
  readonly execution?: string;
  readonly itemsSent?: number;
  readonly name?: string;
  /** In `ping` and `pong`: the heartbeat's number, a safe integer from 1. */
  readonly seq?: number;
  /** The text after the TAB; empty when there is none. */
  readonly payload: string;
}

export type DecodeResult =
  | { readonly ok: true; readonly message: BridgeMessage }
  | { readonly ok: false; readonly reason: string; readonly id: number | undefined };

const maxTraceParentLength = 128;
const maxTraceStateLength = 8 * 1024;

/** Decodes one message, enforcing the shared rules; the id of a refusal is absent when it cannot be trusted. */
export function decode(text: string): DecodeResult {
  const tab = text.indexOf("\t");
  if (tab < 0) {
    return { ok: false, reason: "The message has no TAB after its header.", id: undefined };
  }

  const scan = scanHeader(text.slice(0, tab));
  if (typeof scan === "string") {
    return { ok: false, reason: scan, id: undefined };
  }

  const fields = readFields(scan.raw);
  const id = scan.duplicates.has("id") ? undefined : fields.id;
  const problem = scan.duplicates.size > 0
    ? `The header has '${[...scan.duplicates][0]}' twice.`
    : fields.problem ?? (fields.type === undefined ? "The header has no 'type'." : undefined);
  if (problem !== undefined) {
    return { ok: false, reason: problem, id };
  }

  const message = toMessage(fields, text.slice(tab + 1));
  const missing = missingField(message);
  if (missing !== undefined) {
    return { ok: false, reason: `A '${fields.type}' message needs '${missing}'.`, id };
  }

  return { ok: true, message };
}

/** Encodes one message with each type's fields in the canonical order the vectors check. */
export function encode(message: BridgeMessage): string {
  if (message.type === "unknown") {
    throw new Error("An unknown message cannot be encoded.");
  }

  const parts: string[] = [`"type":${JSON.stringify(message.type)}`];
  const add = (name: string, value: unknown): void => {
    if (value !== undefined) {
      parts.push(`${JSON.stringify(name)}:${JSON.stringify(value)}`);
    }
  };

  switch (message.type) {
    case "hello":
      add("protocol", message.protocol ?? protocolVersion);
      add("types", message.types ?? []);
      add("contracts", message.contracts);
      break;
    case "call":
    case "stream":
      add("id", requireId(message));
      add("contract", message.contract);
      add("command", message.command);
      if (message.type === "stream") {
        add("credits", message.credits ?? 0);
      }

      add("traceparent", message.traceparent);
      add("tracestate", message.tracestate);
      break;
    case "credits":
      add("id", requireId(message));
      add("credits", message.credits ?? 0);
      break;
    case "cancel":
      add("id", requireId(message));
      break;
    case "open":
      add("protocol", message.protocol ?? protocolVersion);
      add("doc", message.doc);
      add("contracts", message.contracts);
      break;
    case "result":
    case "end":
      add("id", requireId(message));
      add("ok", message.ok ?? false);
      break;
    case "items":
      add("id", requireId(message));
      add("dropped", message.dropped);
      break;
    case "failure":
      add("id", requireId(message));
      add("kind", message.kind);
      add("code", message.code);
      add("execution", message.execution);
      add("itemsSent", message.itemsSent);
      break;
    case "event":
      add("name", message.name);
      break;
    case "ping":
    case "pong":
      add("seq", requireSeq(message));
      break;
  }

  return `{${parts.join(",")}}\t${message.payload}`;
}

/** W3C trace context: `00-<32 hex>-<16 hex>-<2 hex>`, lower case, ids not all zero. */
export function isValidTraceParent(value: string): boolean {
  const match = /^([0-9a-f]{2})-([0-9a-f]{32})-([0-9a-f]{16})-([0-9a-f]{2})$/.exec(value);
  if (match === null) {
    return false;
  }

  const [, version, traceId, parentId] = match;
  return version !== "ff" && /[^0]/.test(traceId!) && /[^0]/.test(parentId!);
}

function requireSeq(message: BridgeMessage): number {
  if (message.seq === undefined) {
    throw new Error(`A '${message.type}' message needs a seq.`);
  }

  return message.seq;
}

function requireId(message: BridgeMessage): number {
  if (message.id === undefined) {
    throw new Error(`A '${message.type}' message needs an id.`);
  }

  return message.id;
}

interface HeaderScan {
  /** Each top-level key with its value's raw JSON text. */
  readonly raw: ReadonlyMap<string, string>;
  readonly duplicates: ReadonlySet<string>;
}

/**
 * Splits the header object into its top-level keys and raw values. `JSON.parse` keeps only the last of two equal
 * keys, so duplicates are found here; raw values also let integers be checked as written (`7.0` is not an id).
 */
function scanHeader(header: string): HeaderScan | string {
  let parsed: unknown;
  try {
    parsed = JSON.parse(header);
  } catch (error) {
    return `The header is not one JSON object: ${(error as Error).message}`;
  }

  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    return "The header is not a JSON object.";
  }

  const raw = new Map<string, string>();
  const duplicates = new Set<string>();
  let position = header.indexOf("{") + 1;
  while (true) {
    position = skipSpace(header, position);
    if (header[position] === "}") {
      break;
    }

    const keyEnd = endOfString(header, position);
    const key = JSON.parse(header.slice(position, keyEnd)) as string;
    position = skipSpace(header, keyEnd) + 1; // past ':'
    position = skipSpace(header, position);
    const valueEnd = endOfValue(header, position);
    if (raw.has(key)) {
      duplicates.add(key);
    }

    raw.set(key, header.slice(position, valueEnd));
    position = skipSpace(header, valueEnd);
    if (header[position] === ",") {
      position++;
    }
  }

  return { raw, duplicates };
}

function skipSpace(text: string, position: number): number {
  while (position < text.length && " \t\n\r".includes(text[position]!)) {
    position++;
  }

  return position;
}

/** The index after the string that starts at `position` (an opening quote). */
function endOfString(text: string, position: number): number {
  let index = position + 1;
  while (index < text.length && text[index] !== "\"") {
    index += text[index] === "\\" ? 2 : 1;
  }

  return index + 1;
}

/** The index after the JSON value that starts at `position`; the text is known to be valid JSON. */
function endOfValue(text: string, position: number): number {
  const first = text[position];
  if (first === "\"") {
    return endOfString(text, position);
  }

  if (first !== "{" && first !== "[") {
    let index = position;
    while (index < text.length && !",}] \t\n\r".includes(text[index]!)) {
      index++;
    }

    return index;
  }

  let depth = 0;
  let index = position;
  while (index < text.length) {
    const character = text[index]!;
    if (character === "\"") {
      index = endOfString(text, index);
      continue;
    }

    if (character === "{" || character === "[") {
      depth++;
    } else if (character === "}" || character === "]") {
      depth--;
      if (depth === 0) {
        return index + 1;
      }
    }

    index++;
  }

  return index;
}

interface Fields {
  problem?: string;
  type?: string;
  id?: number;
  protocol?: number;
  types?: string[];
  contracts?: string;
  doc?: string;
  contract?: string;
  command?: string;
  traceparent?: string;
  tracestate?: string;
  credits?: number;
  ok?: boolean;
  dropped?: number;
  kind?: string;
  code?: string;
  execution?: string;
  itemsSent?: number;
  name?: string;
  seq?: number;
}

/** Reads the known fields from their raw text, one by one; unknown fields are ignored. */
function readFields(raw: ReadonlyMap<string, string>): Fields {
  const fields: Fields = {};
  const fail = (problem: string): undefined => {
    fields.problem ??= problem;
    return undefined;
  };

  const text = (name: string): string | undefined => {
    const value = raw.get(name);
    if (value === undefined) {
      return undefined;
    }

    const parsed: unknown = JSON.parse(value);
    if (typeof parsed !== "string" || !parsed.isWellFormed()) {
      return fail(`'${name}' must be a string of valid text.`);
    }

    return parsed;
  };

  const integer = (name: string, minimum: number, maximum: number, message: string): number | undefined => {
    const value = raw.get(name);
    if (value === undefined) {
      return undefined;
    }

    const parsed = /^-?(0|[1-9][0-9]*)$/.test(value) ? Number(value) : Number.NaN;
    return Number.isSafeInteger(parsed) && parsed >= minimum && parsed <= maximum ? parsed : fail(message);
  };

  setIf(fields, "type", text("type"));
  setIf(fields, "id", integer("id", 1, Number.MAX_SAFE_INTEGER, "'id' must be a safe integer from 1."));
  setIf(fields, "protocol", integer("protocol", -2147483648, 2147483647, "'protocol' must be an integer."));
  setIf(fields, "types", readTypes(raw.get("types"), fail));
  setIf(fields, "contracts", text("contracts"));
  setIf(fields, "doc", text("doc"));
  setIf(fields, "contract", text("contract"));
  setIf(fields, "command", text("command"));
  setIf(fields, "traceparent", readTrace(raw.get("traceparent"), maxTraceParentLength, isValidTraceParent));
  setIf(fields, "tracestate", readTrace(raw.get("tracestate"), maxTraceStateLength, () => true));
  setIf(fields, "credits", integer("credits", 1, 2147483647, "'credits' must be a positive integer."));
  setIf(fields, "ok", readBoolean(raw.get("ok"), fail));
  setIf(fields, "dropped", integer("dropped", 0, Number.MAX_SAFE_INTEGER, "'dropped' must be a count."));
  setIf(fields, "kind", text("kind"));
  setIf(fields, "code", text("code"));
  setIf(fields, "execution", text("execution"));
  setIf(fields, "itemsSent", integer("itemsSent", 0, Number.MAX_SAFE_INTEGER, "'itemsSent' must be a count."));
  setIf(fields, "name", text("name"));
  setIf(fields, "seq", integer("seq", 1, Number.MAX_SAFE_INTEGER, "'seq' must be a safe integer from 1."));
  return fields;
}

function setIf<K extends keyof Fields>(fields: Fields, key: K, value: Fields[K] | undefined): void {
  if (value !== undefined) {
    fields[key] = value;
  }
}

function readTypes(value: string | undefined, fail: (problem: string) => undefined): string[] | undefined {
  if (value === undefined) {
    return undefined;
  }

  const parsed: unknown = JSON.parse(value);
  if (!Array.isArray(parsed) || !parsed.every(item => typeof item === "string")) {
    return fail("'types' must be an array of strings.");
  }

  return [...(parsed as string[])];
}

function readBoolean(value: string | undefined, fail: (problem: string) => undefined): boolean | undefined {
  if (value === undefined) {
    return undefined;
  }

  return value === "true" ? true : value === "false" ? false : fail("'ok' must be true or false.");
}

/** A trace value that is not a string, too long, not valid text, or not well formed is dropped, never refused. */
function readTrace(value: string | undefined, maxLength: number, isValid: (text: string) => boolean): string | undefined {
  if (value === undefined) {
    return undefined;
  }

  const parsed: unknown = JSON.parse(value);
  if (typeof parsed !== "string" || !parsed.isWellFormed() || parsed.length > maxLength || !isValid(parsed)) {
    return undefined;
  }

  return parsed;
}

/** Builds the message field by field, never by copying a parsed object. */
function toMessage(fields: Fields, payload: string): BridgeMessage {
  const type = fields.type!;
  const known = knownTypes.has(type);
  const message: { -readonly [K in keyof BridgeMessage]: BridgeMessage[K] } = {
    type: known ? (type as MessageType) : "unknown",
    payload,
  };
  if (!known) {
    message.typeName = type;
  }

  copy(message, "id", fields.id);
  copy(message, "protocol", fields.protocol);
  copy(message, "types", fields.types);
  copy(message, "contracts", fields.contracts);
  copy(message, "doc", fields.doc);
  copy(message, "contract", fields.contract);
  copy(message, "command", fields.command);
  copy(message, "traceparent", fields.traceparent);
  copy(message, "tracestate", fields.tracestate);
  copy(message, "credits", fields.credits);
  copy(message, "ok", fields.ok);
  copy(message, "dropped", fields.dropped);
  copy(message, "kind", fields.kind);
  copy(message, "code", fields.code);
  copy(message, "execution", fields.execution);
  copy(message, "itemsSent", fields.itemsSent);
  copy(message, "name", fields.name);
  copy(message, "seq", fields.seq);
  return message;
}

function copy<K extends keyof BridgeMessage>(
  target: { -readonly [P in keyof BridgeMessage]: BridgeMessage[P] },
  key: K,
  value: BridgeMessage[K] | undefined,
): void {
  if (value !== undefined) {
    target[key] = value;
  }
}

function missingField(message: BridgeMessage): string | undefined {
  const needsId = ["call", "stream", "credits", "cancel", "result", "items", "end", "failure"].includes(message.type);
  if (needsId && message.id === undefined) {
    return "id";
  }

  switch (message.type) {
    case "hello":
      return message.protocol === undefined ? "protocol" : message.contracts === undefined ? "contracts" : undefined;
    case "open":
      return message.protocol === undefined ? "protocol" : message.doc === undefined ? "doc" : message.contracts === undefined ? "contracts" : undefined;
    case "call":
      return message.contract === undefined ? "contract" : message.command === undefined ? "command" : undefined;
    case "stream":
      return message.contract === undefined ? "contract" : message.command === undefined ? "command" : message.credits === undefined ? "credits" : undefined;
    case "credits":
      return message.credits === undefined ? "credits" : undefined;
    case "result":
    case "end":
      return message.ok === undefined ? "ok" : undefined;
    case "failure":
      return message.kind === undefined ? "kind" : message.code === undefined ? "code" : message.execution === undefined ? "execution" : undefined;
    case "event":
      return message.name === undefined ? "name" : undefined;
    case "ping":
    case "pong":
      return message.seq === undefined ? "seq" : undefined;
    default:
      return undefined;
  }
}
