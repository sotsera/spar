// Reads one base64 UTF-8 JSON document per line, parses it with JSON.parse as the TypeScript client does, and
// writes it back with every number and string tagged so .NET can compare exact values (including -0).
import { createInterface } from 'node:readline';

const tag = (_key, value) => {
  if (typeof value === 'number') {
    return 'N:' + (Object.is(value, -0) ? '-0' : String(value));
  }
  return typeof value === 'string' ? 'S:' + value : value;
};

const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });
const out = [];
for await (const line of lines) {
  if (line.length === 0) {
    continue;
  }
  let result;
  try {
    const text = new TextDecoder('utf-8').decode(Buffer.from(line, 'base64'));
    result = JSON.stringify(JSON.parse(text), tag);
  } catch (error) {
    result = JSON.stringify('E:' + error.message);
  }
  out.push(result);
}
process.stdout.write(out.join('\n') + '\n');
