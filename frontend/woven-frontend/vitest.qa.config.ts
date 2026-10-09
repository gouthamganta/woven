import { defineConfig } from 'vitest/config';

// Keep the local QA suite within the shared laptop's memory budget.
export default defineConfig({
  test: {
    maxWorkers: 1,
    fileParallelism: false,
    coverage: { reportOnFailure: true },
  },
});
