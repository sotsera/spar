import { useEffect, useState, type FormEvent } from "react";
import { links } from "@spar/links";
import { notes } from "@spar/notes";
import { ContractMismatchError, ready } from "@spar/sdk";
import { useNoteSearch } from "./useNoteSearch";
import { Header } from "./Header";

export function App() {
  const [text, setText] = useState("");
  const [tags, setTags] = useState("");
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("Ready");
  const [count, setCount] = useState<number | null>(null);
  const [link, setLink] = useState("https://example.com/");
  const [preview, setPreview] = useState("");
  // ?slow: shorter (older) queries answer later than longer (newer) ones, so stale answers really arrive last.
  const slow = new URLSearchParams(location.search).has("slow");
  const search = useNoteSearch(query, slow ? Math.max(0, 900 - query.length * 300) : null);

  useEffect(() => {
    void ready();
    const onMismatch = () => setStatus("The app was rebuilt with different contracts. Reload the page.");
    addEventListener("spar:contract-mismatch", onMismatch);
    return () => removeEventListener("spar:contract-mismatch", onMismatch);
  }, []);

  useEffect(() => {
    void notes.count().then(setCount);
  }, [search.results]);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!text.trim()) {
      setStatus("Enter a note.");
      return;
    }

    try {
      const saved = await notes.save({ text, tags: tags.split(",").map(t => t.trim()).filter(Boolean) });
      setStatus(`Saved version ${saved.version}`);
      setText("");
      setTags("");
      search.refresh();
    } catch (error) {
      setStatus(error instanceof ContractMismatchError ? error.message : `Error: ${String(error)}`);
    }
  }

  async function showPreview() {
    setPreview("Loading…");
    try {
      const result = await links.preview({ url: link });
      setPreview(`${result.status} ${result.title ?? "(no title)"} (${result.bytes} bytes)`);
    } catch (error) {
      setPreview(`Error: ${String(error)}`);
    }
  }

  async function remove(id: string) {
    await notes.remove({ id });
    setStatus("Removed");
    search.refresh();
  }

  return (
    <main>
      <Header count={count} />
      <form onSubmit={save} aria-label="New note">
        <label>Note <input value={text} onChange={e => setText(e.target.value)} /></label>
        <label>Tags <input value={tags} onChange={e => setTags(e.target.value)} placeholder="comma separated" /></label>
        <button type="submit">Save</button>
      </form>
      <p role="status">{status}</p>
      <section aria-label="Link preview">
        <label>Link <input value={link} onChange={e => setLink(e.target.value)} /></label>
        <button type="button" onClick={() => void showPreview()}>Preview</button>
        <output aria-label="Preview result">{preview}</output>
      </section>
      <label>Search <input type="search" value={query} onChange={e => setQuery(e.target.value)} /></label>
      {search.error && <p role="alert">{search.error}</p>}
      <ul aria-label="Notes" aria-busy={search.loading}>
        {search.results.map(note => (
          <li key={note.id}>
            <span>{note.text}</span>
            {note.tags.map(tag => <span key={tag} className="tag"> #{tag}</span>)}
            <time dateTime={note.createdAt}> {note.createdAt.slice(0, 10)}</time>
            <button type="button" onClick={() => void remove(note.id)} aria-label={`Remove ${note.text}`}>Remove</button>
          </li>
        ))}
      </ul>
    </main>
  );
}
