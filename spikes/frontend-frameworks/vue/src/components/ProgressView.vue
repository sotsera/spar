<script setup lang="ts">
import { ref } from "vue";
import { copyJobs } from "@spar/app/copyJobs";
import { useCall, useSubscription } from "../composables";
import { recipe } from "../../../shared/harness";

const props = defineProps<{ jobId: string }>();
const rows = ref(0n);
const status = useSubscription(signal => copyJobs.progress(props.jobId, { signal }), e => {
  if (e.type === "event") rows.value = e.value.rows;
  else if (e.type === "reset") rows.value = 0n;
});
const planned = useCall(signal => copyJobs.plan(recipe, { signal }));
</script>

<template>
  <p>{{ status }}: {{ rows.toString() }} rows; {{ planned?.match({ success: p => p.steps.length, errors: { connectionNotFound: () => -1, tableMissing: () => -2 }, failure: e => e.kind }) ?? "planning" }}</p>
</template>
