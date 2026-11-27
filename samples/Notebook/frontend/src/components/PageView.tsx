import * as Menu from "@radix-ui/react-dropdown-menu";
import { files, type PickedFile } from "@spar/app/sdk";
import { AlertTriangle, Check, CircleDot, ClipboardCopy, Columns2, Eye, FileUp, LoaderCircle, MoreHorizontal, PencilLine, Trash2 } from "lucide-react";
import { lazy, Suspense, useEffect, useRef, useState, type ClipboardEvent } from "react";
import type { SaveState } from "../notebook.js";
import type { ViewMode } from "../remembered.js";
import { ago, when } from "../time.js";
import type { Page } from "@spar/app/pages";
import { imageExtensions } from "../transfer.js";
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
  /** Adds an image file dropped on the editor, as a grant, and resolves with the Markdown that shows it, or undefined. */
  readonly onAddImageFile: (file: PickedFile) => Promise<string | undefined>;
  readonly onExport: () => void;
  readonly onCopy: () => void;
  readonly onNotice: (text: string) => void;
}

export function PageView(props: PageViewProps) {
  const { page, draft, save, onEdit, onReload, onKeepMine, onDelete, mode, onMode, onAddImage, onAddImageFile, onExport, onCopy, onNotice } = props;
  const editor = useRef<HTMLTextAreaElement>(null);
  const insertImages = useImageInsert(editor, draft, onEdit, onAddImage, onAddImageFile, onNotice, mode !== "Read");
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
                onSelect={onExport}
                className="flex cursor-default items-center gap-2 rounded-md px-2.5 py-1.5 text-sm outline-none data-highlighted:bg-line/60 dark:data-highlighted:bg-line-dark/60"
              >
                <FileUp aria-hidden className="size-4" />
                Export…
              </Menu.Item>
              <Menu.Item
                onSelect={onCopy}
                className="flex cursor-default items-center gap-2 rounded-md px-2.5 py-1.5 text-sm outline-none data-highlighted:bg-line/60 dark:data-highlighted:bg-line-dark/60"
              >
                <ClipboardCopy aria-hidden className="size-4" />
                Copy as Markdown
              </Menu.Item>
              <Menu.Separator className="my-1 h-px bg-line dark:bg-line-dark" />
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
 * Images pasted or dropped into the editor: each is added to the page, and its Markdown goes where the caret was, in
 * the order the images came. A paste carries the image's bytes; a drop from the OS arrives as grants, since WebKitGTK
 * gives the page no files for a drop (the editor is a drop area that takes images only). Text pastes and drops, and
 * drags of the page's own text, keep the browser's own behaviour.
 */
function useImageInsert(
  editor: React.RefObject<HTMLTextAreaElement | null>,
  draft: string,
  onEdit: (markdown: string) => void,
  onAddImage: (file: File) => Promise<string | undefined>,
  onAddImageFile: (file: PickedFile) => Promise<string | undefined>,
  onNotice: (text: string) => void,
  editing: boolean,
) {
  const insert = async (added: Promise<string | undefined>[]) => {
    const area = editor.current;
    const start = area?.selectionStart ?? draft.length;
    const end = area?.selectionEnd ?? draft.length;
    const markdown: string[] = [];
    for (const image of added) {
      const text = await image;
      if (text !== undefined) {
        markdown.push(text);
      }
    }

    if (markdown.length > 0) {
      const text = editor.current?.value ?? draft;
      onEdit(text.slice(0, start) + markdown.join("\n") + text.slice(end));
    }
  };

  // The newest callbacks, read when a drop comes: the drop area itself is made once, for the editor's element.
  const latest = useRef({ insert, onAddImageFile, onNotice });
  latest.current = { insert, onAddImageFile, onNotice };
  useEffect(() => {
    if (editor.current === null) {
      return undefined;
    }

    const accept = imageExtensions.map(extension => `.${extension}`);
    return files.dropTarget(editor.current, { accept, limit: 10 }, result => result.match({
      success: dropped => void latest.current.insert(dropped.files.map(file => latest.current.onAddImageFile(file))),
      errors: {
        notAccepted: e => latest.current.onNotice(`${e.name} is not an image a page takes.`),
        tooMany: e => latest.current.onNotice(`At most ${e.limit} images at a time.`),
      },
    }));
  }, [editor, editing]);

  const imagesOf = (list: FileList | null): File[] => Array.from(list ?? []).filter(file => file.type.startsWith("image/"));

  return {
    paste: (event: ClipboardEvent<HTMLTextAreaElement>) => {
      const pasted = imagesOf(event.clipboardData.files);
      if (pasted.length > 0) {
        event.preventDefault();
        void insert(pasted.map(file => onAddImage(file)));
      }
    },
  };
}
