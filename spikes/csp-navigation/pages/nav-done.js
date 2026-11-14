import { send } from "/report.js";

await send({ kind: "alive", name: "sameOrigin", href: location.href });
await send({ kind: "done" });
