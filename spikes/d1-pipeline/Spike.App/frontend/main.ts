import { notes } from "./notes.js";

const saved = await notes.save({ text: "hi", tags: [], parent: null, score: 0, pinned: false, version: null });
console.log(saved.version);
