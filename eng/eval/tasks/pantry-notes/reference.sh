#!/usr/bin/env bash
# A reference solution: the contract, and a form and list on the page.
source "$EVAL_LIB"
cp -r "$EVAL_TASK/reference/." .
replace Pantry/frontend/src/App.tsx 'import { greet } from "./greet.js";' 'import { greet } from "./greet.js";
import { notes } from "@spar/app/notes";'
replace Pantry/frontend/src/App.tsx '  const [message, setMessage] = useState("");' '  const [message, setMessage] = useState("");
  const [note, setNote] = useState("");
  const [added, setAdded] = useState<readonly string[]>([]);
  const [problem, setProblem] = useState("");'
replace Pantry/frontend/src/App.tsx '      <p role="status">{message}</p>' '      <p role="status">{message}</p>
      <form
        onSubmit={event => {
          event.preventDefault();
          void notes.add({ text: note }).match({
            success: saved => {
              setAdded(list => [...list, saved.text]);
              setProblem("");
            },
            errors: { empty: () => setProblem("A note needs text.") },
            failure: error => setProblem(`The app did not answer (${error.code}).`),
          });
        }}
      >
        <label htmlFor="note">Note</label>
        <input id="note" value={note} onChange={event => setNote(event.target.value)} />
        <button type="submit">Add note</button>
      </form>
      <p>{problem}</p>
      <ul>
        {added.map((text, index) => (
          <li key={index}>{text}</li>
        ))}
      </ul>'
