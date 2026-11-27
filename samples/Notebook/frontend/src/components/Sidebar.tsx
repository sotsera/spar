import type { PageSummary } from "@spar/app/pages";
import { files, type PickedFile } from "@spar/app/sdk";
import { FileDown, FilePlus2, NotebookPen, Search } from "lucide-react";
import { forwardRef, useEffect, useMemo, useRef } from "react";
import { markdownExtensions } from "../transfer.js";
import { ago, when } from "../time.js";

interface SidebarProps {
  readonly summaries: readonly PageSummary[] | undefined;
  readonly selected: string | undefined;
  readonly query: string;
  readonly onQuery: (query: string) => void;
  readonly onSelect: (id: string) => void;
  readonly onNew: () => void;
  /** Imports Markdown files: the ones dropped on the list, or, called without any, the ones the person chooses. */
  readonly onImport: (dropped?: readonly PickedFile[]) => void;
  /** Shows why a drop was not taken. */
  readonly onNotice: (text: string) => void;
}

/** The notebook's pages, newest first, filtered by the search box; Markdown files dropped on the list become pages. */
export const Sidebar = forwardRef<HTMLInputElement, SidebarProps>(function Sidebar(props, searchRef) {
  const { summaries, selected, query, onQuery, onSelect, onNew, onImport, onNotice } = props;
  const list = useDropOfMarkdown(onImport, onNotice);
  const visible = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase();
    return (summaries ?? []).filter(s => needle === "" || s.title.toLocaleLowerCase().includes(needle));
  }, [summaries, query]);

  return (
    <aside className="flex w-72 shrink-0 flex-col border-r border-line bg-sheet/60 dark:border-line-dark dark:bg-sheet-dark/60">
      <header className="flex items-center gap-2 px-4 pt-4 pb-3">
        <NotebookPen aria-hidden className="size-5 text-accent dark:text-accent-dark" />
        <h1 className="text-[15px] font-semibold tracking-tight">Notebook</h1>
        <button
          type="button"
          onClick={() => onImport()}
          title="Import Markdown files"
          aria-label="Import Markdown files"
          className="ml-auto rounded-md p-1.5 text-muted hover:bg-line/60 hover:text-ink dark:text-muted-dark dark:hover:bg-line-dark/60 dark:hover:text-ink-dark"
        >
          <FileDown aria-hidden className="size-4" />
        </button>
        <button
          type="button"
          onClick={onNew}
          title="New page (Ctrl+N)"
          className="inline-flex items-center gap-1.5 rounded-md bg-accent px-2.5 py-1.5 text-xs font-medium text-white shadow-sm hover:bg-accent/90 dark:bg-accent-dark dark:text-paper-dark"
        >
          <FilePlus2 aria-hidden className="size-3.5" />
          New page
        </button>
      </header>

      <div className="px-3 pb-2">
        <label className="relative block">
          <span className="sr-only">Search pages</span>
          <Search aria-hidden className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted dark:text-muted-dark" />
          <input
            ref={searchRef}
            type="search"
            value={query}
            onChange={event => onQuery(event.target.value)}
            placeholder="Search pages"
            className="w-full rounded-md border border-line bg-sheet py-1.5 pr-2 pl-8 text-sm placeholder:text-muted dark:border-line-dark dark:bg-sheet-dark dark:placeholder:text-muted-dark"
          />
        </label>
      </div>

      <nav
        ref={list}
        aria-label="Pages"
        className="min-h-0 flex-1 overflow-y-auto px-2 pb-4 data-[spar-drop=over]:bg-accent/5 data-[spar-drop=over]:ring-2 data-[spar-drop=over]:ring-accent/40 data-[spar-drop=over]:ring-inset dark:data-[spar-drop=over]:ring-accent-dark/40"
      >
        {summaries === undefined ? (
          <p className="px-2 py-3 text-sm text-muted dark:text-muted-dark">Loading pages…</p>
        ) : visible.length === 0 ? (
          <p className="px-2 py-3 text-sm text-muted dark:text-muted-dark">
            {summaries.length === 0 ? "No pages yet." : `Nothing matches “${query.trim()}”.`}
          </p>
        ) : (
          <ul className="space-y-0.5">
            {visible.map(summary => (
              <li key={summary.id}>
                <button
                  type="button"
                  onClick={() => onSelect(summary.id)}
                  aria-current={summary.id === selected ? "page" : undefined}
                  className="group w-full rounded-md px-2.5 py-2 text-left hover:bg-line/60 aria-[current=page]:bg-accent/10 dark:hover:bg-line-dark/60 dark:aria-[current=page]:bg-accent-dark/15"
                >
                  <span className="block truncate text-sm font-medium group-aria-[current=page]:text-accent dark:group-aria-[current=page]:text-accent-dark">
                    {summary.title}
                  </span>
                  <span className="block text-xs text-muted dark:text-muted-dark" title={when(summary.updatedAt)}>
                    {ago(summary.updatedAt)}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </nav>
    </aside>
  );
});

/** The list of pages as a drop area for Markdown files from the OS: each one dropped becomes a page. */
function useDropOfMarkdown(onImport: (dropped: readonly PickedFile[]) => void, onNotice: (text: string) => void) {
  const list = useRef<HTMLElement>(null);
  useEffect(() => {
    if (list.current === null) {
      return undefined;
    }

    const accept = markdownExtensions.map(extension => `.${extension}`);
    return files.dropTarget(list.current, { accept, limit: 50 }, result => result.match({
      success: dropped => onImport(dropped.files),
      errors: {
        notAccepted: e => onNotice(`${e.name} is not a Markdown file.`),
        tooMany: e => onNotice(`At most ${e.limit} files at a time.`),
      },
    }));
  }, [onImport, onNotice]);

  return list;
}
