import * as Menu from "@radix-ui/react-dropdown-menu";
import { AlertTriangle, Check, CircleDot, Columns2, Eye, LoaderCircle, MoreHorizontal, PencilLine, Trash2 } from "lucide-react";
import { lazy, Suspense, useState } from "react";
import type { SaveState } from "../notebook.js";
import { ago, when } from "../time.js";
import type { Page } from "@spar/app/pages";
import { DeletePageDialog } from "./Dialogs.js";


// The Markdown renderer is most of the bundle; it loads with the first preview, in a chunk of its own.
const Preview = lazy(() => import("./Preview.js").then(module => ({ default: module.Preview })));

type Mode = "write" | "split" | "read";

interface PageViewProps {
  readonly page: Page;
  readonly draft: string;
  readonly save: SaveState;
  readonly onEdit: (markdown: string) => void;
  readonly onReload: () => void;
  readonly onKeepMine: () => void;
  readonly onDelete: () => void;
}

export function PageView({ page, draft, save, onEdit, onReload, onKeepMine, onDelete }: PageViewProps) {
  const [mode, setMode] = useState<Mode>("split");
  const [deleting, setDeleting] = useState(false);

  return (
    <section aria-label={page.title} className="flex min-w-0 flex-1 flex-col">
      <header className="flex items-center gap-3 border-b border-line px-6 py-3 dark:border-line-dark">
        <div className="min-w-0">
          <h2 className="truncate text-lg font-semibold tracking-tight">{page.title}</h2>
          <p className="text-xs text-muted dark:text-muted-dark" title={when(page.updatedAt)}>
            Edited {ago(page.updatedAt)}
          </p>
        </div>
        <SaveIndicator save={save} />
        <ModeSwitch mode={mode} onMode={setMode} />
        <Menu.Root modal={false}>
          <Menu.Trigger aria-label="Page actions" className="rounded-md p-1.5 hover:bg-line/60 dark:hover:bg-line-dark/60">
            <MoreHorizontal aria-hidden className="size-4" />
          </Menu.Trigger>
          <Menu.Portal>
            <Menu.Content
              align="end"
              sideOffset={6}
              className="min-w-44 rounded-lg border border-line bg-sheet p-1 shadow-lg dark:border-line-dark dark:bg-sheet-dark"
            >
              <Menu.Item
                onSelect={() => setDeleting(true)}
                className="flex cursor-default items-center gap-2 rounded-md px-2.5 py-1.5 text-sm text-red-600 outline-none data-highlighted:bg-red-50 dark:text-red-400 dark:data-highlighted:bg-red-950/40"
              >
                <Trash2 aria-hidden className="size-4" />
                Delete page…
              </Menu.Item>
            </Menu.Content>
          </Menu.Portal>
        </Menu.Root>
      </header>

      {save.kind === "conflict" && <ConflictBanner onReload={onReload} onKeepMine={onKeepMine} />}

      <div className={`grid min-h-0 flex-1 ${mode === "split" ? "grid-cols-2" : "grid-cols-1"}`}>
        {mode !== "read" && (
          <label className="flex min-h-0 flex-col border-line dark:border-line-dark [&:not(:last-child)]:border-r">
            <span className="sr-only">Markdown of {page.title}</span>
            <textarea
              value={draft}
              onChange={event => onEdit(event.target.value)}
              spellCheck
              className="min-h-0 flex-1 resize-none bg-transparent px-6 py-5 font-mono text-[13.5px] leading-relaxed [font-variant-ligatures:none] outline-none"
            />
          </label>
        )}
        {mode !== "write" && (
          <div className="min-h-0 overflow-y-auto px-8 py-6">
            <Suspense fallback={<p className="text-sm text-muted dark:text-muted-dark">Rendering…</p>}>
              <Preview markdown={draft} />
            </Suspense>
          </div>
        )}
      </div>

      <DeletePageDialog
        title={page.title}
        open={deleting}
        onOpenChange={setDeleting}
        onDelete={() => {
          setDeleting(false);
          onDelete();
        }}
      />
    </section>
  );
}

function SaveIndicator({ save }: { readonly save: SaveState }) {
  const look = {
    saved: { icon: <Check aria-hidden className="size-3.5" />, text: "Saved" },
    unsaved: { icon: <CircleDot aria-hidden className="size-3.5" />, text: "Unsaved changes" },
    saving: { icon: <LoaderCircle aria-hidden className="size-3.5 animate-spin" />, text: "Saving…" },
    conflict: { icon: <AlertTriangle aria-hidden className="size-3.5" />, text: "Not saved: changed elsewhere" },
    failed: { icon: <AlertTriangle aria-hidden className="size-3.5" />, text: save.kind === "failed" ? save.message : "" },
  }[save.kind];
  const warning = save.kind === "conflict" || save.kind === "failed";

  return (
    <p
      role="status"
      className={`ml-auto inline-flex items-center gap-1.5 text-xs ${warning ? "text-amber-700 dark:text-amber-400" : "text-muted dark:text-muted-dark"}`}
    >
      {look.icon}
      {look.text}
    </p>
  );
}

function ModeSwitch({ mode, onMode }: { readonly mode: Mode; readonly onMode: (mode: Mode) => void }) {
  const options: ReadonlyArray<readonly [Mode, string, typeof PencilLine]> = [
    ["write", "Write", PencilLine],
    ["split", "Write and preview", Columns2],
    ["read", "Preview", Eye],
  ];

  return (
    <div role="group" aria-label="View" className="inline-flex rounded-md border border-line p-0.5 dark:border-line-dark">
      {options.map(([value, label, Icon]) => (
        <button
          key={value}
          type="button"
          aria-pressed={mode === value}
          title={label}
          onClick={() => onMode(value)}
          className="rounded px-2 py-1 text-muted hover:text-ink aria-pressed:bg-line aria-pressed:text-ink dark:text-muted-dark dark:hover:text-ink-dark dark:aria-pressed:bg-line-dark dark:aria-pressed:text-ink-dark"
        >
          <Icon aria-hidden className="size-4" />
          <span className="sr-only">{label}</span>
        </button>
      ))}
    </div>
  );
}

function ConflictBanner({ onReload, onKeepMine }: { readonly onReload: () => void; readonly onKeepMine: () => void }) {
  return (
    <div role="alert" className="flex items-center gap-3 border-b border-amber-200 bg-amber-50 px-6 py-2.5 text-sm dark:border-amber-900/60 dark:bg-amber-950/30">
      <AlertTriangle aria-hidden className="size-4 shrink-0 text-amber-600 dark:text-amber-400" />
      <p className="min-w-0 flex-1">This page changed in another window since you opened it. Saving now would overwrite that change.</p>
      <button type="button" onClick={onReload} className="rounded-md px-2.5 py-1 font-medium hover:bg-amber-100 dark:hover:bg-amber-900/40">
        Load their version
      </button>
      <button type="button" onClick={onKeepMine} className="rounded-md bg-amber-600 px-2.5 py-1 font-medium text-white hover:bg-amber-600/90">
        Keep mine
      </button>
    </div>
  );
}
