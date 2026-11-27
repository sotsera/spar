import * as Menu from "@radix-ui/react-dropdown-menu";
import { AlertTriangle, Check, CircleDot, Columns2, Eye, LoaderCircle, MoreHorizontal, PencilLine, Trash2 } from "lucide-react";
import { lazy, Suspense, useRef, useState, type ClipboardEvent, type DragEvent } from "react";
import type { SaveState } from "../notebook.js";
import type { ViewMode } from "../remembered.js";
import { ago, when } from "../time.js";
import type { Page } from "@spar/app/pages";
import { DeletePageDialog } from "./Dialogs.js";


// The Markdown renderer is most of the bundle; it loads with the first preview, in a chunk of its own.
const Preview = lazy(() => import("./Preview.js").then(module => ({ default: module.Preview })));

interface PageViewProps {
  readonly page: Page;
  readonly draft: string;
  readonly save: SaveState;
  readonly onEdit: (markdown: string) => void;
  readonly onReload: () => void;
  readonly onKeepMine: () => void;
  readonly onDelete: () => void;
  /** The view mode, remembered between runs. */
  readonly mode: ViewMode;
  readonly onMode: (mode: ViewMode) => void;
  /** Adds a pasted or dropped image to the page and resolves with the Markdown that shows it, or undefined. */
  readonly onAddImage: (file: File) => Promise<string | undefined>;
}

export function PageView({ page, draft, save, onEdit, onReload, onKeepMine, onDelete, mode, onMode, onAddImage }: PageViewProps) {
  const editor = useRef<HTMLTextAreaElement>(null);
  const insertImages = useImageInsert(editor, draft, onEdit, onAddImage);
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
        <ModeSwitch mode={mode} onMode={onMode} />
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

      <div className={`grid min-h-0 flex-1 ${mode === "Split" ? "grid-cols-2" : "grid-cols-1"}`}>
        {mode !== "Read" && (
          <label className="flex min-h-0 flex-col border-line dark:border-line-dark [&:not(:last-child)]:border-r">
            <span className="sr-only">Markdown of {page.title}</span>
            <textarea
              ref={editor}
              value={draft}
              onChange={event => onEdit(event.target.value)}
              onPaste={insertImages.paste}
              onDrop={insertImages.drop}
              spellCheck
              className="min-h-0 flex-1 resize-none bg-transparent px-6 py-5 font-mono text-[13.5px] leading-relaxed [font-variant-ligatures:none] outline-none"
            />
          </label>
        )}
        {mode !== "Write" && (
          <div className="min-h-0 overflow-y-auto px-8 py-6">
            <Suspense fallback={<p className="text-sm text-muted dark:text-muted-dark">Rendering…</p>}>
              <Preview pageId={page.id} markdown={draft} />
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

function ModeSwitch({ mode, onMode }: { readonly mode: ViewMode; readonly onMode: (mode: ViewMode) => void }) {
  const options: ReadonlyArray<readonly [ViewMode, string, typeof PencilLine]> = [
    ["Write", "Write", PencilLine],
    ["Split", "Write and preview", Columns2],
    ["Read", "Preview", Eye],
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

/**
 * Image files pasted or dropped into the editor: each is added to the page, and its Markdown goes where the caret was,
 * in the order the files came. Text pastes and drops keep the browser's own behaviour.
 */
function useImageInsert(
  editor: React.RefObject<HTMLTextAreaElement | null>,
  draft: string,
  onEdit: (markdown: string) => void,
  onAddImage: (file: File) => Promise<string | undefined>,
) {
  const insert = async (files: readonly File[]) => {
    const area = editor.current;
    const start = area?.selectionStart ?? draft.length;
    const end = area?.selectionEnd ?? draft.length;
    const added: string[] = [];
    for (const file of files) {
      const markdown = await onAddImage(file);
      if (markdown !== undefined) {
        added.push(markdown);
      }
    }

    if (added.length > 0) {
      const text = editor.current?.value ?? draft;
      onEdit(text.slice(0, start) + added.join("\n") + text.slice(end));
    }
  };

  const imagesOf = (list: FileList | null): File[] => Array.from(list ?? []).filter(file => file.type.startsWith("image/"));

  return {
    paste: (event: ClipboardEvent<HTMLTextAreaElement>) => {
      const files = imagesOf(event.clipboardData.files);
      if (files.length > 0) {
        event.preventDefault();
        void insert(files);
      }
    },
    drop: (event: DragEvent<HTMLTextAreaElement>) => {
      const files = imagesOf(event.dataTransfer.files);
      if (files.length > 0) {
        event.preventDefault();
        void insert(files);
      }
    },
  };
}
