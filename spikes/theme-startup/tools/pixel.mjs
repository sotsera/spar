// Usage: node pixel.mjs <png> [x y]  Prints the RGB of a pixel (default: the centre) of a PNG written by the hosts.
import fs from "node:fs";
import zlib from "node:zlib";
const buf = fs.readFileSync(process.argv[2]);
let pos = 8, width = 0, height = 0, type = 2; const idat = [];
while (pos < buf.length) {
  const len = buf.readUInt32BE(pos); const kind = buf.toString("ascii", pos + 4, pos + 8); const data = buf.subarray(pos + 8, pos + 8 + len);
  if (kind === "IHDR") { width = data.readUInt32BE(0); height = data.readUInt32BE(4); type = data[9]; }
  if (kind === "IDAT") idat.push(data);
  pos += 12 + len;
}
const raw = zlib.inflateSync(Buffer.concat(idat)); const bpp = type === 6 ? 4 : 3; const stride = 1 + width * bpp;
const x = Number(process.argv[3] ?? width >> 1), y = Number(process.argv[4] ?? height >> 1);
const o = y * stride + 1 + x * bpp;
if (raw[y * stride] !== 0) console.log("(row filter " + raw[y * stride] + ": value not exact)");
console.log(`${width}x${height} (${x},${y}) = #${[raw[o], raw[o + 1], raw[o + 2]].map((v) => v.toString(16).padStart(2, "0")).join("")}`);
