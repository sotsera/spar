// The notebook's state: the list of pages, the open page with its draft, and saving. Every outcome of every call is
// handled here, declared errors by name, so the components only render what this hook decides. Closing the window
// saves a draft that is not saved yet first, and asks before losing one that cannot be saved (docs/app-model.md 6.11).
import { pages, type Page, type PageChange, type PageSummary } from "@spar/app/pages";
import { onBackendUpdate } from "@spar/app/sdk";
import { windows, type CloseAnswer, type CloseRequest } from "@spar/app/windows";
import { useCallback, useEffect, useRef, useState } from "react";
import { addImage as addPageImage, useRemembered, type ViewMode } from "./remembered.js";

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
  /** How the open page is shown, remembered between runs. */
  readonly viewMode: ViewMode;
  setViewMode(mode: ViewMode): void;
  /** Adds an image to the open page and resolves with the Markdown that shows it; a refusal becomes the notice. */
  addImage(file: File): Promise<string | undefined>;
}

export function useNotebook(): Notebook {
  const [summaries, setSummaries] = useState<readonly PageSummary[]>();
  const [current, setCurrent] = useState<OpenPage>({ kind: "none" });
  const [notice, setNotice] = useState<string>();
  const currentRef = useRef(current);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const saving = useRef<Promise<SaveState> | undefined>(undefined);
  // The newest draft, typed or saved, read when a save comes back: React may apply a state update after it returns.
  const newestDraft = useRef<string>("");
  currentRef.current = current;

  const refresh = useCallback(async () => {
    await pages.list().match({
      success: list => setSummaries([...list].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))),
    });
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // In a Debug build, after Hot Reload changed the backend's code, the list is fetched again with the new code (for
  // example a changed sort or title rule). A production build has no such updates; the call does nothing there.
  useEffect(() => onBackendUpdate(() => void refresh()), [refresh]);

  // A page saved in another window: an open page with nothing unsaved shows the new text; one with unsaved typing is
  // never overwritten, it shows the conflict at once. Changes this window made come back too, and are already here.
  const follow = useCallback(async (change: PageChange) => {
    const state = currentRef.current;
    if (state.kind !== "open" || state.page.id !== change.id || state.save.kind === "saving") {
      return;
    }

    if (change.kind === "Deleted") {
      setCurrent(latest => (latest.kind === "open" && latest.page.id === change.id
        ? { ...latest, save: { kind: "failed", message: "This page was deleted elsewhere." } }
        : latest));
      return;
    }

    if (change.revision <= state.page.revision) {
      return;
    }

    if (state.save.kind !== "saved") {
      setCurrent(latest => (latest.kind === "open" && latest.page.id === change.id && latest.save.kind !== "saving"
        ? { ...latest, save: { kind: "conflict", currentRevision: change.revision } }
        : latest));
      return;
    }

    await pages.get({ id: change.id }).match({
      success: page => setCurrent(latest => {
        // Typing that began while the new text was on its way keeps the page as it is: the next save finds the conflict.
        if (latest.kind !== "open" || latest.page.id !== page.id || latest.save.kind !== "saved") {
          return latest;
        }

        newestDraft.current = page.markdown;
        return { kind: "open", page, draft: page.markdown, save: { kind: "saved" } };
      }),
      errors: { notFound: () => undefined, titleTaken: () => undefined, invalidTitle: () => undefined, changedElsewhere: () => undefined },
      failure: () => undefined,
    });
  }, []);

  // Live changes, as a subscription for as long as the notebook is shown: the list follows every one. After a lost
  // connection, Spar subscribes again by itself and resets: the list is read again, since changes may have been missed.
  useEffect(() => {
    const live = new AbortController();
    void (async () => {
      for await (const item of pages.changes({ signal: live.signal })) {
        item.match({
          success: change => {
            void refresh();
            void follow(change);
          },
          events: { reset: () => void refresh() },
          // Our own abort when the notebook goes away; any other end leaves the list as it is until the next action.
          failure: () => undefined,
        });
      }
    })();
    return () => live.abort();
  }, [refresh, follow]);

  /** Opens a page; one that is gone shows as missing, or, for the page remembered from the last run, as no page. */
  const load = useCallback((id: string, gone: OpenPage) => {
    clearTimeout(timer.current);
    setCurrent({ kind: "loading", id });
    void pages.get({ id }).match({
      success: page => {
        newestDraft.current = page.markdown;
        setCurrent({ kind: "open", page, draft: page.markdown, save: { kind: "saved" } });
      },
      errors: {
        notFound: () => setCurrent(gone),
        titleTaken: () => setCurrent(gone),
        invalidTitle: () => setCurrent(gone),
        changedElsewhere: () => setCurrent(gone),
      },
    });
  }, []);

  const open = useCallback((id: string) => load(id, { kind: "missing", id }), [load]);
  const openLast = useCallback((id: string) => load(id, { kind: "none" }), [load]);
  const shown = current.kind === "open" ? current.page.id : current.kind === "none" ? undefined : current.id;
  const remembered = useRemembered(shown, openLast);

  const addImage = useCallback(async (file: File): Promise<string | undefined> => {
    const state = currentRef.current;
    if (state.kind !== "open") {
      return undefined;
    }

    const added = await addPageImage(state.page.id, file);
    if ("notice" in added) {
      setNotice(added.notice);
      return undefined;
    }

    return added.markdown;
  }, []);

  /**
   * Saves the draft over `baseRevision`; a draft typed while the save was on its way stays unsaved. Resolves with how
   * the save ended, which a close of the window waits for.
   */
  const saveOver = useCallback((baseRevision: bigint): Promise<SaveState> => {
    const run = saveDraft(baseRevision);
    saving.current = run;
    void run.finally(() => {
      if (saving.current === run) {
        saving.current = undefined;
      }
    });
    return run;

    async function saveDraft(base: bigint): Promise<SaveState> {
      const state = currentRef.current;
      if (state.kind !== "open") {
        return { kind: "saved" };
      }

      // The draft of the last render, which may be a keystroke behind: an edit React has not applied yet stays in the
      // state, which is only updated from what it holds now, and the draft stays unsaved until a later save sends it.
      const sent = state.draft;
      setCurrent(latest => (latest.kind === "open" && latest.page.id === state.page.id ? { ...latest, save: { kind: "saving" } } : latest));
      const outcome = await pages.save({ id: state.page.id, markdown: sent, baseRevision: base }).match({
        success: (page): SaveState | Page => page,
        errors: {
          changedElsewhere: (error): SaveState => ({ kind: "conflict", currentRevision: error.currentRevision }),
          notFound: (): SaveState => ({ kind: "failed", message: "This page was deleted elsewhere." }),
          titleTaken: (): SaveState => ({ kind: "failed", message: "The page could not be saved." }),
          invalidTitle: (): SaveState => ({ kind: "failed", message: "The page could not be saved." }),
        },
        failure: (): SaveState => ({ kind: "failed", message: "Not saved: the notebook could not be reached." }),
      });

      const ended: SaveState = "revision" in outcome ? { kind: newestDraft.current === sent ? "saved" : "unsaved" } : outcome;
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

      return ended;
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

  /** Saves what is not saved yet (a failed save is tried once more) and resolves with how the last save ended. */
  const flushDraft = useCallback(async (): Promise<SaveState> => {
    clearTimeout(timer.current);
    for (let round = 0; round < 3; round++) {
      const before = saving.current;
      if (before !== undefined) {
        const ended = await before;
        if (ended.kind !== "unsaved") {
          return ended;
        }
      }

      const state = currentRef.current;
      if (state.kind !== "open" || state.save.kind === "saved") {
        return { kind: "saved" };
      }

      if (state.save.kind === "conflict") {
        // Saving again would overwrite the other change: the person decides.
        return state.save;
      }

      const ended = await saveOver(state.page.revision);
      if (ended.kind !== "unsaved") {
        return ended;
      }
    }

    return { kind: "unsaved" };
  }, [saveOver]);

  // Closing the window: a draft not saved yet is saved first, and one on its way is waited for. When it cannot be saved,
  // the person decides; at session end, or when tests or an agent close the app, nobody can, so the window closes.
  useEffect(() => windows.onCloseRequested(async request => answerClose(request, await flushDraft())), [flushDraft]);

  const edit = useCallback((markdown: string) => {
    newestDraft.current = markdown;
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
        newestDraft.current = page.markdown;
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
    viewMode: remembered.viewMode,
    setViewMode: remembered.setViewMode,
    addImage,
  };
}

/** The close guard's answer once the draft was saved, or could not be. */
export function answerClose(request: CloseRequest, saved: SaveState): CloseAnswer {
  if (saved.kind === "saved" || request.automated || request.reason === "sessionEnd") {
    return "close";
  }

  const why = saved.kind === "conflict"
    ? "This page was changed elsewhere."
    : saved.kind === "failed"
      ? saved.message
      : "Your last changes are still being saved.";
  return { ask: `${why} Close without your last changes?` };
}
