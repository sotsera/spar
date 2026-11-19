// Usage: node spikes/windows-single-instance/scripts/summarize.mjs <folder with case folders>
// For each case folder: the arguments the primaries received with their handoff times, the secondaries' exit codes
// and wall times, and the lines that show a failure path.
import { readdirSync, readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

const root = process.argv[2];
const interesting = /refused|did not answer|giving up|attempt [2-9]|attempt \d\d|without a pipe|CreateNamedPipe failed|client is gone|sent nothing|pipe broke|foreground=(True|False)|SetForegroundWindow=|probe:|Unhandled/;

function stats(values) {
  if (values.length === 0) return "none";
  const sorted = [...values].sort((a, b) => a - b);
  const median = sorted[Math.floor(sorted.length / 2)];
  return `n=${sorted.length} min=${sorted[0].toFixed(1)} median=${median.toFixed(1)} max=${sorted.at(-1).toFixed(1)}`;
}

for (const name of readdirSync(root).sort()) {
  const caseDir = join(root, name);
  const logDirs = readdirSync(caseDir).filter((d) => d.startsWith("logs")).map((d) => join(caseDir, d));
  for (const logDir of logDirs) {
    const lines = readdirSync(logDir)
      .flatMap((file) => readFileSync(join(logDir, file), "utf8").split("\n"))
      .filter(Boolean)
      .sort((a, b) => parseFloat(a) - parseFloat(b));
    const t0 = parseFloat(lines[0]);
    const launch = [];
    const main = [];
    const exits = {};
    const walls = {};
    for (const line of lines) {
      const received = line.match(/from_launch_ms=([\d.]+) from_main_ms=([\d.]+)/);
      if (received) {
        launch.push(parseFloat(received[1]));
        main.push(parseFloat(received[2]));
      }

      const spawned = line.match(/spawned #\d+ pid \d+: exit (\d+) after ([\d.]+) ms/);
      if (spawned) {
        exits[spawned[1]] = (exits[spawned[1]] ?? 0) + 1;
        (walls[spawned[1]] ??= []).push(parseFloat(spawned[2]));
      }
    }

    console.log(`== ${name}/${logDir.slice(caseDir.length + 1)}`);
    const paths = join(caseDir, "paths.txt");
    if (existsSync(paths)) console.log(`   ${readFileSync(paths, "utf8").trim()}`);
    console.log(`   received: ${main.length}; from launch (ms) ${stats(launch)}; from secondary main (ms) ${stats(main)}`);
    for (const [code, count] of Object.entries(exits)) {
      console.log(`   secondaries exit ${code}: ${count}, wall (ms) ${stats(walls[code])}`);
    }

    for (const line of lines.filter((l) => interesting.test(l))) {
      const time = (parseFloat(line) - t0).toFixed(1).padStart(9);
      console.log(`   +${time} ${line.slice(line.indexOf(" ") + 1, 230)}`);
    }
  }
}
