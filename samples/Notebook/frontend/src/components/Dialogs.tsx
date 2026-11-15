// The notebook's dialogs, on the browser's own modal <dialog>: focus stays inside, Escape closes it, and the page
// behind is inert. Modal dialogs that lock scrolling by injecting a <style> element are blocked by the default
// Content Security Policy (style-src 'self'), so the sample does not use them.
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";

const panel =
  "m-auto w-[min(28rem,calc(100vw-2rem))] rounded-xl border border-line bg-sheet p-5 text-ink shadow-xl backdrop:bg-ink/30 backdrop:backdrop-blur-[2px] dark:border-line-dark dark:bg-sheet-dark dark:text-ink-dark dark:backdrop:bg-black/50";
const primary =
  "rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-accent/90 disabled:opacity-60 dark:bg-accent-dark dark:text-paper-dark";
const secondary = "rounded-md px-3 py-1.5 text-sm font-medium hover:bg-line/60 dark:hover:bg-line-dark/60";

interface ModalProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly role?: "dialog" | "alertdialog";
  readonly title: string;
  readonly description?: string;
  readonly children: ReactNode;
}

/** A modal dialog, opened with showModal() so the browser handles focus, Escape and the inert page. */
function Modal({ open, onOpenChange, role = "dialog", title, description, children }: ModalProps) {
  const dialog = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const element = dialog.current;
    if (element === null) {
      return;
    }

    if (open && !element.open) {
      element.showModal();
    } else if (!open && element.open) {
      element.close();
    }
  }, [open]);

  return (
    <dialog
      ref={dialog}
      role={role === "alertdialog" ? "alertdialog" : undefined}
      aria-labelledby={titleId}
      aria-describedby={description === undefined ? undefined : descriptionId}
      onClose={() => onOpenChange(false)}
      onClick={event => {
        // A click on the backdrop lands on the dialog element itself.
        if (event.target === dialog.current) {
          onOpenChange(false);
        }
      }}
      className={panel}
    >
      {open && (
        <>
          <h2 id={titleId} className="text-base font-semibold">
            {title}
          </h2>
          {description !== undefined && (
            <p id={descriptionId} className="mt-2 text-sm text-muted dark:text-muted-dark">
              {description}
            </p>
          )}
          {children}
        </>
      )}
    </dialog>
  );
}

interface NewPageProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly onCreate: (title: string) => Promise<string | undefined>;
}

/** Asks for a title; the backend's answer (taken, invalid) is shown under the field. */
export function NewPageDialog({ open, onOpenChange, onCreate }: NewPageProps) {
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const errorId = useId();

  const change = (next: boolean) => {
    if (!next) {
      setTitle("");
      setError(undefined);
    }

    onOpenChange(next);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    const message = await onCreate(title);
    setBusy(false);
    if (message === undefined) {
      change(false);
    } else {
      setError(message);
    }
  };

  return (
    <Modal open={open} onOpenChange={change} title="New page">
      <form onSubmit={submit} className="mt-4 space-y-4">
        <label className="block">
          <span className="mb-1.5 block text-sm font-medium">Title</span>
          <input
            autoFocus
            value={title}
            onChange={event => setTitle(event.target.value)}
            aria-invalid={error !== undefined}
            aria-describedby={error === undefined ? undefined : errorId}
            className="w-full rounded-md border border-line bg-paper px-3 py-2 text-sm aria-invalid:border-red-500 dark:border-line-dark dark:bg-paper-dark"
          />
        </label>
        {error !== undefined && (
          <p id={errorId} role="alert" className="text-sm text-red-600 dark:text-red-400">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={() => change(false)} className={secondary}>
            Cancel
          </button>
          <button type="submit" disabled={busy} className={primary}>
            Create page
          </button>
        </div>
      </form>
    </Modal>
  );
}

interface DeleteProps {
  readonly title: string;
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly onDelete: () => void;
}

/** Confirms deleting a page, which removes its Markdown file. The safe choice has the focus. */
export function DeletePageDialog({ title, open, onOpenChange, onDelete }: DeleteProps) {
  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      role="alertdialog"
      title={`Delete “${title}”?`}
      description="The page and its Markdown file are removed from the notebook. This cannot be undone."
    >
      <div className="mt-5 flex justify-end gap-2">
        <button type="button" autoFocus onClick={() => onOpenChange(false)} className={secondary}>
          Keep it
        </button>
        <button type="button" onClick={onDelete} className="rounded-md bg-red-600 px-3 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-red-600/90">
          Delete page
        </button>
      </div>
    </Modal>
  );
}
