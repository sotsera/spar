// The notebook's state: the list of pages, the open page with its draft, and saving. Every outcome of every call is
// handled here, declared errors by name, so the components only render what this hook decides.
import { pages, type Page, type PageSummary } from "@spar/app/pages";
import { useCallback, useEffect, useRef, useState } from "react";

/** How long the editor waits after the last keystroke before it saves. */
const autosaveDelayMs = 800;

export type SaveState =
  | { readonly kind: "saved" }
  | { readonly kind: "unsaved" }
  | { readonly kind: "saving" }
  | { readonly kind: "conflict"; readonly currentRevision: bigint }
  | { readonly kind: "failed"; readonly message: string };

export type OpenPage =
  | { readonly kind: "none" }
  | { readonly kind: "loading"; readonly id: string }
  | { readonly kind: "missing"; readonly id: string }
  | { readonly kind: "open"; readonly page: Page; readonly draft: string; readonly save: SaveState };

export interface Notebook {
  readonly summaries: readonly PageSummary[] | undefined;
  readonly current: OpenPage;
  readonly notice: string | undefined;
  open(id: string): void;
  close(): void;
  edit(markdown: string): void;
  save(): void;
  reload(): void;
  keepMine(): void;
  /** Creates a page and opens it; resolves to an error message for the form, or undefined when it worked. */
  create(title: string): Promise<string | undefined>;
  remove(): Promise<void>;
  dismissNotice(): void;
  showNotice(message: string): void;
}

export function useNotebook(): Notebook {
  const [summaries, setSummaries] = useState<readonly PageSummary[]>();
  const [current, setCurrent] = useState<OpenPage>({ kind: "none" });
  const [notice, setNotice] = useState<string>();
  const currentRef = useRef(current);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  currentRef.current = current;

  const refresh = useCallback(async () => {
    await pages.list().match({
      success: list => setSummaries([...list].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))),
    });
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const open = useCallback((id: string) => {
    clearTimeout(timer.current);
    setCurrent({ kind: "loading", id });
    void pages.get({ id }).match({
      success: page => setCurrent({ kind: "open", page, draft: page.markdown, save: { kind: "saved" } }),
      errors: {
        notFound: () => setCurrent({ kind: "missing", id }),
        titleTaken: () => setCurrent({ kind: "missing", id }),
        invalidTitle: () => setCurrent({ kind: "missing", id }),
        changedElsewhere: () => setCurrent({ kind: "missing", id }),
      },
    });
  }, []);

  /** Saves the draft over `baseRevision`; a draft typed while the save was on its way stays unsaved. */
  const saveOver = useCallback(async (baseRevision: bigint) => {
    const state = currentRef.current;
    if (state.kind !== "open") {
      return;
    }

    const sent = state.draft;
    setCurrent({ ...state, save: { kind: "saving" } });
    const outcome = await pages.save({ id: state.page.id, markdown: sent, baseRevision }).match({
      success: (page): SaveState | Page => page,
      errors: {
        changedElsewhere: (error): SaveState => ({ kind: "conflict", currentRevision: error.currentRevision }),
        notFound: (): SaveState => ({ kind: "failed", message: "This page was deleted elsewhere." }),
        titleTaken: (): SaveState => ({ kind: "failed", message: "The page could not be saved." }),
        invalidTitle: (): SaveState => ({ kind: "failed", message: "The page could not be saved." }),
      },
      failure: (): SaveState => ({ kind: "failed", message: "Not saved: the notebook could not be reached." }),
    });

    setCurrent(latest => {
      if (latest.kind !== "open" || latest.page.id !== state.page.id) {
        return latest;
      }

      if ("revision" in outcome) {
        const unchanged = latest.draft === sent;
        return { kind: "open", page: outcome, draft: latest.draft, save: { kind: unchanged ? "saved" : "unsaved" } };
      }

      return { ...latest, save: outcome };
    });

    if ("revision" in outcome) {
      void refresh();
    }
  }, [refresh]);

  const save = useCallback(() => {
    clearTimeout(timer.current);
    const state = currentRef.current;
    if (state.kind === "open" && state.save.kind !== "saving" && state.save.kind !== "conflict") {
      void saveOver(state.page.revision);
    }
  }, [saveOver]);

  // Autosave: after a pause in typing, and again once a save that raced new typing has come back.
  useEffect(() => {
    if (current.kind !== "open" || current.save.kind !== "unsaved") {
      return;
    }

    clearTimeout(timer.current);
    timer.current = setTimeout(save, autosaveDelayMs);
    return () => clearTimeout(timer.current);
  }, [current, save]);

  const edit = useCallback((markdown: string) => {
    setCurrent(state => {
      if (state.kind !== "open") {
        return state;
      }

      const save: SaveState = state.save.kind === "saving" || state.save.kind === "conflict" ? state.save : { kind: "unsaved" };
      return { ...state, draft: markdown, save };
    });
  }, []);

  const keepMine = useCallback(() => {
    const state = currentRef.current;
    if (state.kind === "open" && state.save.kind === "conflict") {
      void saveOver(state.save.currentRevision);
    }
  }, [saveOver]);

  const reload = useCallback(() => {
    const state = currentRef.current;
    if (state.kind === "open") {
      open(state.page.id);
    }
  }, [open]);

  const create = useCallback(async (title: string): Promise<string | undefined> => {
    const message = await pages.create({ title, markdown: null }).match({
      success: page => {
        setCurrent({ kind: "open", page, draft: page.markdown, save: { kind: "saved" } });
        return undefined;
      },
      errors: {
        titleTaken: error => `There is already a page called “${error.title}”.`,
        invalidTitle: error => error.reason,
        notFound: () => "The page could not be created.",
        changedElsewhere: () => "The page could not be created.",
      },
      failure: () => "The notebook could not be reached. Try again in a moment.",
    });
    await refresh();
    return message;
  }, [refresh]);

  const remove = useCallback(async () => {
    const state = currentRef.current;
    if (state.kind !== "open") {
      return;
    }

    clearTimeout(timer.current);
    await pages.delete({ id: state.page.id }).match({
      success: () => undefined,
      errors: { notFound: () => undefined, titleTaken: () => undefined, invalidTitle: () => undefined, changedElsewhere: () => undefined },
    });
    setCurrent({ kind: "none" });
    await refresh();
  }, [refresh]);

  return {
    summaries,
    current,
    notice,
    open,
    close: () => setCurrent({ kind: "none" }),
    edit,
    save,
    reload,
    keepMine,
    create,
    remove,
    dismissNotice: () => setNotice(undefined),
    showNotice: setNotice,
  };
}
