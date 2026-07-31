import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Em dev, o front fala com a API local sem CORS
      "/api": "http://localhost:5258",
      "/health": "http://localhost:5258",
    },
  },
  test: {
    environment: "node", // testes cobrem lib/* puro (sem DOM); nada usa jsdom ainda
    include: ["src/**/*.test.js"],
    setupFiles: ["src/lib/vitest.setup.js"],
  },
});
