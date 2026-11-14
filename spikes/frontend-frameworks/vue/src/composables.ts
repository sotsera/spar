import { onScopeDispose, ref, shallowRef } from "vue";
import type { Call, Result, SubscriptionEvent } from "@spar/app/sdk";

/** Starts a call for the lifetime of the current scope; the Result is kept in a shallowRef (never proxied). */
export function useCall<T, E extends object>(start: (signal: AbortSignal) => Call<T, E>) {
  const result = shallowRef<Result<T, E>>();
  const abort = new AbortController();
  void start(abort.signal).then(r => { result.value = r; });
  onScopeDispose(() => abort.abort());
  return result;
}

/** Follows a subscription for the lifetime of the current scope. */
export function useSubscription<T>(open: (signal: AbortSignal) => AsyncIterable<Result<SubscriptionEvent<T>>>, onEvent: (event: SubscriptionEvent<T>) => void) {
  const status = ref("connecting");
  const abort = new AbortController();
  void (async () => {
    for await (const item of open(abort.signal)) {
      item.match({
        success: e => { onEvent(e); if (e.type === "event" || e.type === "resumed") status.value = "live"; else if (e.type === "reconnecting") status.value = "reconnecting"; },
        failure: e => { if (!abort.signal.aborted) status.value = e.message; },
      });
    }
  })();
  onScopeDispose(() => abort.abort());
  return status;
}
