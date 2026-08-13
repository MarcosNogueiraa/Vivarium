// E2E do toast (12/08/2026, pedido do usuário): clicar na notificação deve fechá-la na
// hora, em vez de esperar os 4s — às vezes ela fica em cima de um botão que o jogador
// quer clicar. API mockada via cy.intercept.

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

const fish = creature(201, 4001, 6.06);
const tankWithFish = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5, queue: [], creatures: [fish],
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

describe("Toast", () => {
  it("clicar na notificação fecha na hora, sem esperar os 4s", () => {
    cy.clock();
    login();

    cy.get(".fish-row").first().click();
    cy.contains("button", "Vender ao NPC · 7").scrollIntoView().should("be.visible").click();
    cy.intercept("POST", "/api/game/creatures/201/sell-vendor", { statusCode: 200, body: { price: 7 } });
    cy.intercept("GET", "/api/game/tank", { body: { ...tankWithFish, creatures: [], wallet: { SOFT: 107, PREMIUM: 0 } } });
    cy.contains("button", "Vender agora").click();

    cy.get(".toast").should("be.visible").contains("Vendido ao NPC por 7 moedas.");
    cy.get(".toast").click();
    cy.get(".toast").should("not.exist");
  });

  it("some sozinho depois de 4s se não for clicado (sem regressão)", () => {
    cy.clock();
    login();

    cy.get(".fish-row").first().click();
    cy.contains("button", "Vender ao NPC · 7").scrollIntoView().should("be.visible").click();
    cy.intercept("POST", "/api/game/creatures/201/sell-vendor", { statusCode: 200, body: { price: 7 } });
    cy.intercept("GET", "/api/game/tank", { body: { ...tankWithFish, creatures: [], wallet: { SOFT: 107, PREMIUM: 0 } } });
    cy.contains("button", "Vender agora").click();

    cy.get(".toast").should("be.visible");
    cy.tick(4001);
    cy.get(".toast").should("not.exist");
  });
});
