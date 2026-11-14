// What a Vue-specific SDK declaration (or the app's env.d.ts) could add: Vue's types then keep Result and Call as they are.
import type { Call, Result } from "@spar/app/sdk";

declare module "@vue/reactivity" {
  interface RefUnwrapBailTypes {
    spar: Result<any, any> | Call<any, any>; // eslint-disable-line @typescript-eslint/no-explicit-any
  }
}
