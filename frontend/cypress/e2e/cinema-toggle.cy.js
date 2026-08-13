// E2E do toggle do efeito "cinema" (12/08/2026, pedido do usuário — no mobile o :hover que
// clareia de volta no desktop não existe, então a tela ficava sempre escura). API mockada.

import { generateTraits } from "../../src/lib/generator.js";

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function creature(id, seed, rarityScore) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    traits: generateTraits(BigInt(seed)), breedingSource: null,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0,
  };
}

const tankWithFish = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5, queue: [], creatures: [creature(201, 4001, 6.06)],
  wallet: { SOFT: 100, PREMIUM: 0 }, coinsPerHour: 4.7, generationProgressMinutes: 0, generationIntervalMinutes: 60,
};

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { body: tankWithFish }).as("tank");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
}

describe("Toggle do efeito cinema", () => {
  it("vem ligado por padrão (app-shell com cinema-mode)", () => {
    login();
    cy.get(".app-shell").should("have.class", "cinema-mode");
    cy.get(".tank-layout").should("not.have.class", "cinema-off");
  });

  it("desligar tira o cinema-mode e a vinheta, e persiste depois de recarregar", () => {
    login();
    cy.get('.tool-btn[title*="Desligar o efeito cinema"]').click();

    cy.get(".app-shell").should("not.have.class", "cinema-mode");
    cy.get(".tank-layout").should("have.class", "cinema-off");

    cy.reload();
    cy.wait("@tank");
    cy.get(".app-shell").should("not.have.class", "cinema-mode");
    cy.get('.tool-btn[title*="Ligar o efeito cinema"]').should("be.visible");
  });
});
