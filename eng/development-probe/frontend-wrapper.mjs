// Test-only process tree: coordinator job -> wrapper -> helper -> grandchild, alongside the actual Vite server.
import { spawn } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const helper = spawn(process.execPath, [fileURLToPath(new URL('./helper.mjs', import.meta.url))], { stdio: 'ignore' });
appendFileSync(process.env.SPAR_TEST_TREE, JSON.stringify({ role: 'helper', pid: helper.pid, parent: process.pid }) + '\n');
const vite = spawn(process.execPath, [process.env.SPAR_TEST_VITE], { stdio: 'inherit' });
vite.once('exit', code => { helper.kill(); process.exitCode = code ?? 1; });
