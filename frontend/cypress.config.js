import { defineConfig } from "cypress";

export default defineConfig({
  e2e: {
    baseUrl: "http://localhost:4173", // `npm run preview` (build de produção servido localmente)
    specPattern: "cypress/e2e/**/*.cy.js",
    supportFile: "cypress/support/e2e.js",
    video: false, // CI só precisa do resultado pass/fail, sem gastar tempo gravando
  },
});
