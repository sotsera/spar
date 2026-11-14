// Expected type errors (vue-tsc -p tsconfig.bad.json): what the types say about Results inside Vue state.
import { reactive, ref, shallowRef, markRaw } from "vue";
import type { Result } from "@spar/app/sdk";
import type { CopyPlan, PlanError } from "@spar/app/copyJobs";

declare const result: Result<CopyPlan, PlanError>;
declare function show(r: Result<CopyPlan, PlanError>): string;

const deep = ref(result);
show(deep.value); // bad 1: ref() unwraps the class type, losing #private
const inReactive = reactive({ result });
show(inReactive.result); // bad 2: the same through reactive()
show(shallowRef(result).value); // control: shallowRef keeps the type (no error)
show(ref(markRaw(result)).value); // control: markRaw keeps the type (no error)
const typed = ref<Result<CopyPlan, PlanError>>();
show(typed.value!); // control: ref<T>() with an explicit type and no initial value does not unwrap (no error)
deep.value.match({ success: p => p.steps.length, errors: { connectionNotFound: () => 0, tableMissing: () => 0 } }); // control: match itself still type-checks
