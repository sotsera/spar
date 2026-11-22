import { useState } from "react";
import { greet } from "./greet.js";

export function App() {
  const [name, setName] = useState("");
  const [message, setMessage] = useState("");

  return (
    <main>
      <h1>MyApp</h1>
      <form
        onSubmit={event => {
          event.preventDefault();
          void greet(name).then(setMessage);
        }}
      >
        <label htmlFor="name">Name</label>
        <input id="name" value={name} onChange={event => setName(event.target.value)} />
        <button type="submit">Greet</button>
      </form>
      <p role="status">{message}</p>
    </main>
  );
}
