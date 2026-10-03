import { defineConfig } from "vitest/config";

// No React plugin: esbuild compiles TSX with the automatic runtime. That keeps Babel and its
// browser-data packages (CC-BY-4.0 / ISC) out of the dependency tree (CLAUDE.md rule 6).
export default defineConfig({
  esbuild: { jsx: "automatic" },
  server: {
    port: Number(process.env.ERP_WEB_PORT ?? 5173),
    proxy: { "/api": process.env.ERP_API_URL ?? "http://localhost:5080" },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    target: "es2022",
    chunkSizeWarningLimit: 400,
  },
  test: {
    environment: "happy-dom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    setupFiles: ["src/test/setup.ts"],
  },
});
