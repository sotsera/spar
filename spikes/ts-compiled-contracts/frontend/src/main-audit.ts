// Control for the tree-shaking check: imports the auditLog contract, so its marker must be in the bundle.
import { auditLog } from "@spar/app/auditLog";

void auditLog.list(10).match({ success: entries => console.log(entries.length) });
