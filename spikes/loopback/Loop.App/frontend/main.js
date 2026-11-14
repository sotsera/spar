import { notes } from "./notes.js";
import { ready, ContractMismatchError } from "./spar-sdk.js";

const text = document.getElementById("text");
const save = document.getElementById("save");
const status = document.getElementById("status");
const list = document.getElementById("list");

function render(items) {
  list.replaceChildren(...items.map(note => {
    const item = document.createElement("li");
    item.textContent = `${note.text} (v${note.version})`;
    return item;
  }));
}

save.addEventListener("click", async () => {
  try {
    const saved = await notes.save({ text: text.value, tags: [], version: null });
    status.textContent = `Saved version ${saved.version}`;
    render(await notes.list());
  } catch (error) {
    status.textContent = error instanceof ContractMismatchError ? error.message : `Error: ${error.message}`;
  }
});

render(await notes.list());
save.disabled = false;
status.textContent = "Ready";
await ready();
