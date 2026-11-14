import { useCallback, useEffect, useRef, useState } from "react";
import { notes, type Note } from "@spar/notes";
import { NotExecutedError } from "@spar/sdk";

export interface SearchState {
  readonly results: readonly Note[];
  readonly loading: boolean;
  readonly error: string | null;
}

/**
 * Backend search where the latest query always wins: each new query aborts the previous call's wait,
 * and a late answer for an older query is ignored even if it arrives after a newer one.
 */
export function useNoteSearch(text: string, delayMs: number | null) {
  const [state, setState] = useState<SearchState>({ results: [], loading: true, error: null });
  const sequence = useRef(0);
  const [refreshes, setRefreshes] = useState(0);

  useEffect(() => {
    const current = ++sequence.current;
    const controller = new AbortController();
    setState(previous => ({ ...previous, loading: true }));

    notes.search({ text: text || null, delayMs }, { signal: controller.signal })
      .then(results => {
        if (current === sequence.current) {
          setState({ results, loading: false, error: null });
        }
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || current !== sequence.current) {
          return;
        }

        const message = error instanceof NotExecutedError ? `Not executed: ${error.message}` : String(error);
        setState(previous => ({ ...previous, loading: false, error: message }));
      });

    return () => controller.abort();
  }, [text, delayMs, refreshes]);

  const refresh = useCallback(() => setRefreshes(n => n + 1), []);
  return { ...state, refresh };
}
