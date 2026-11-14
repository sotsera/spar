// Minimal declarations: the spike type-checks without @types/node.
declare const process: {
  argv: string[];
  version: string;
  versions: Record<string, string>;
  arch: string;
  execArgv: string[];
  memoryUsage(): { heapUsed: number };
};
declare module "node:fs" {
  export function writeFileSync(path: string, data: string): void;
}
