<script setup lang="ts">
import { onUnmounted } from "vue";
import { copyJobs } from "@spar/app/copyJobs";
import { recipe } from "../../../shared/harness";

const emit = defineEmits<{ unmounted: [] }>();
// Top-level await of a Call: Vue wraps it with withAsyncContext, which restores the instance after the await.
const result = await copyJobs.plan(recipe);
const text = result.match({
  success: p => `planned ${p.steps.length}`,
  errors: { connectionNotFound: e => `missing ${e.connectionId}`, tableMissing: e => `no table ${e.table}` },
  failure: e => `failure ${e.kind}`,
});
// Registered after the await: works only if the instance was restored.
onUnmounted(() => emit("unmounted"));
</script>

<template>
  <p>{{ text }}</p>
</template>
