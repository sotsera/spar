// The consumer's page: counts the words of the text through the app's notes contract, over the channel of the host it
// runs in (the loopback one in browser tests).
import { connectSpar, loopbackChannel } from "@spar/app/sdk";
import { notes } from "@spar/app/notes";
import { fingerprint } from "@spar/app/spar";

const connection = connectSpar(loopbackChannel(), fingerprint);

const form = required(document.querySelector("form"));
const text = required(document.querySelector("textarea"));
const result = required(document.querySelector("#result"));

form.addEventListener("submit", event => {
  event.preventDefault();
  void count(text.value);
});

async function count(value: string): Promise<void> {
  result.textContent = await notes.count(value).match({
    success: note => `${note.words} words`,
    failure: error => `Could not count the words (${error.code}).`,
  });
}

function required<T>(element: T | null): T {
  if (element === null) {
    throw new Error("The page is missing part of its form.");
  }

  return element;
}

connection.ready();
