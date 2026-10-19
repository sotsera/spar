import { spawn } from 'node:child_process';
import { appendFileSync } from 'node:fs';

const grandchild = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'ignore' });
appendFileSync(process.env.SPAR_TEST_TREE, JSON.stringify({ role: 'grandchild', pid: grandchild.pid, parent: process.pid }) + '\n');
setInterval(() => {}, 1000);
