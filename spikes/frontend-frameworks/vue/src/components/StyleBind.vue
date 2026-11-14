<script setup lang="ts">
const color = "rgb(255, 0, 0)";
const inline = "border-top-width: 3px; border-top-style: solid";
</script>

<template>
  <p class="bound" :style="inline">styled</p>
</template>

<style scoped>
.bound { color: v-bind(color); }
</style>
