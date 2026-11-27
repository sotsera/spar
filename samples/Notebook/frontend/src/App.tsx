import { FilePlus2, FileQuestion, NotebookText, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { NewPageDialog } from "./components/Dialogs.js";
import { PageView } from "./components/PageView.js";
import { Sidebar } from "./components/Sidebar.js";
import type { Notebook } from "./notebook.js";

export function App({ notebook }: { readonly notebook: Notebook }) {
  const [query, setQuery] = useState("");
  const [creating, setCreating] = useState(false);
  const search = useRef<HTMLInputElement>(null);
  const { current } = notebook;

  useShortcuts({
    newPage: () => setCreating(true),
    save: notebook.save,
    search: () => search.current?.focus(),
  });

  const selected = current.kind === "none" ? undefined : current.kind === "open" ? current.page.id : current.id;

  return (
    <>
      <div className="flex h-full">
        <Sidebar
          ref={search}
          summaries={notebook.summaries}
          selected={selected}
          query={query}
          onQuery={setQuery}
          onSelect={notebook.open}
          onNew={() => setCreating(true)}
        />
        <main className="flex min-w-0 flex-1 bg-sheet dark:bg-sheet-dark">
          {current.kind === "open" ? (
            <PageView
              key={current.page.id}
              page={current.page}
              draft={current.draft}
              save={current.save}
              onEdit={notebook.edit}
              onReload={notebook.reload}
              onKeepMine={notebook.keepMine}
              onDelete={() => void notebook.remove()}
              mode={notebook.viewMode}
              onMode={notebook.setViewMode}
              onAddImage={notebook.addImage}
            />
          ) : current.kind === "missing" ? (
            <Empty
              icon={<FileQuestion aria-hidden className="size-8" />}
              title="This page is gone"
              text="It may have been deleted in another window."
              action={{ label: "Back to the notebook", run: notebook.close }}
            />
          ) : current.kind === "loading" ? (
            <p className="m-auto text-sm text-muted dark:text-muted-dark">Opening…</p>
          ) : (
            <Empty
              icon={<NotebookText aria-hidden className="size-8" />}
              title={notebook.summaries?.length === 0 ? "Start your notebook" : "Pick a page"}
              text={notebook.summaries?.length === 0 ? "Pages are Markdown files you can open anywhere." : "Choose a page on the left, or start a new one."}
              action={{ label: "New page", run: () => setCreating(true) }}
            />
          )}
        </main>
      </div>

      <NewPageDialog open={creating} onOpenChange={setCreating} onCreate={notebook.create} />
      <Notice text={notebook.notice} onDismiss={notebook.dismissNotice} />
    </>
  );
}

interface EmptyProps {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly text: string;
  readonly action: { readonly label: string; readonly run: () => void };
}

function Empty({ icon, title, text, action }: EmptyProps) {
  return (
    <div className="m-auto flex max-w-sm flex-col items-center text-center">
      <div className="mb-4 rounded-2xl bg-accent/10 p-4 text-accent dark:bg-accent-dark/15 dark:text-accent-dark">{icon}</div>
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      <p className="mt-1 text-sm text-muted dark:text-muted-dark">{text}</p>
      <button
        type="button"
        onClick={action.run}
        className="mt-5 inline-flex items-center gap-1.5 rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-accent/90 dark:bg-accent-dark dark:text-paper-dark"
      >
        <FilePlus2 aria-hidden className="size-4" />
        {action.label}
      </button>
    </div>
  );
}

/** Failures nothing else handled, announced and shown until dismissed. */
function Notice({ text, onDismiss }: { readonly text: string | undefined; readonly onDismiss: () => void }) {
  return (
    <div aria-live="assertive" className="pointer-events-none fixed right-4 bottom-4 left-4 flex justify-center">
      {text !== undefined && (
        <div className="pointer-events-auto flex max-w-lg items-start gap-3 rounded-lg border border-line bg-ink px-4 py-3 text-sm text-paper shadow-lg dark:border-line-dark dark:bg-ink-dark dark:text-paper-dark">
          <p className="flex-1">{text}</p>
          <button type="button" onClick={onDismiss} aria-label="Dismiss" className="rounded p-0.5 opacity-70 hover:opacity-100">
            <X aria-hidden className="size-4" />
          </button>
        </div>
      )}
    </div>
  );
}

function useShortcuts(actions: { readonly newPage: () => void; readonly save: () => void; readonly search: () => void }) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const command = event.ctrlKey || event.metaKey;
      const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
      if (command && event.key.toLowerCase() === "n") {
        event.preventDefault();
        actions.newPage();
      } else if (command && event.key.toLowerCase() === "s") {
        event.preventDefault();
        actions.save();
      } else if (event.key === "/" && !typing) {
        event.preventDefault();
        actions.search();
      }
    };

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [actions]);
}
