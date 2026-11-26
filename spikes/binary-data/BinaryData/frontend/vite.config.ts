import spar from "@spar/app/vite";
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import { defineConfig } from "vite";

// The bytes the page fetches from its own origin, the same xorshift sequence as Payload.Of in C#; generated here so the
// repository holds no large files (public/bin is ignored; .webp, one of the types the hosts serve, already compressed, so the packer stores it).
const sizes = [16 * 1024, 256 * 1024, 1 << 20, 4 << 20, 12 << 20, 32 << 20];
mkdirSync("public/bin", { recursive: true });
for (const size of sizes) {
  const file = `public/bin/blob-${size}.webp`;
  if (existsSync(file)) {
    continue;
  }

  const bytes = new Uint8Array(size);
  let state = 2463534242;
  for (let i = 0; i < size; i++) {
    state = (state ^ (state << 13)) >>> 0;
    state = (state ^ (state >>> 17)) >>> 0;
    state = (state ^ (state << 5)) >>> 0;
    bytes[i] = state & 0xff;
  }

  writeFileSync(file, bytes);
}

export default defineConfig({
  plugins: [spar()],
});
