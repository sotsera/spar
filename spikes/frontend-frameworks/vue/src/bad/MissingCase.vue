<script setup lang="ts">
import { shallowRef } from "vue";
import type { Result } from "@spar/app/sdk";
import type { CopyPlan, PlanError } from "@spar/app/copyJobs";

const result = shallowRef<Result<CopyPlan, PlanError>>();
</script>

<template>
  <!-- bad 4: a declared case missing in a template match; bad 5: a misspelled case -->
  <p>{{ result?.match({ success: p => p.steps.length, errors: { connectionNotFound: () => 0 } }) }}</p>
  <p>{{ result?.match({ success: p => p.steps.length, errors: { connectionNotFound: () => 0, tableMising: () => 0 } }) }}</p>
</template>
