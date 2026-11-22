// The start-up probe's splash page for a later document: its address picks the start (start.js).
import "./probe.css";
import { runStart } from "./start.js";

await runStart(new URLSearchParams(location.search));
