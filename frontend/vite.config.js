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
});
