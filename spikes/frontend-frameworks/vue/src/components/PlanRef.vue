<script setup lang="ts">
import { onMounted, ref, shallowRef } from "vue";
import type { Result } from "@spar/app/sdk";
import { copyJobs, type CopyPlan, type PlanError } from "@spar/app/copyJobs";
import { recipe } from "../../../shared/harness";

const props = defineProps<{ mode: "ref" | "shallowRef" }>();
// The common first attempt (ref) and the fix (shallowRef).
const result = props.mode === "ref" ? ref<Result<CopyPlan, PlanError>>() : shallowRef<Result<CopyPlan, PlanError>>();
onMounted(async () => { result.value = await copyJobs.plan(recipe); });
</script>

<template>
  <p>{{ result?.match({ success: p => `planned ${p.steps.length}`, errors: { connectionNotFound: e => `missing ${e.connectionId}`, tableMissing: e => `no table ${e.table}` } }) ?? "loading" }}</p>
</template>
