// E2E do "acelerar com premium" (CLAUDE.md 8.11) — único jeito de pular a espera da fila.
// API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const tankWithPendingItem = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5,
  queue: [{ id: 42, readyAt: "2099-01-01T00:00:00Z", isReady: false, isSick: false, rushCostPremium: 9 }],
  creatures: [], wallet: { SOFT: 100, PREMIUM: 50 },
  coinsPerHour: 0, generationProgressMinutes: 0, generationIntervalMinutes: 60,
};

const tankWithReadyItem = {
  ...tankWithPendingItem,
  queue: [{ id: 42, readyAt: "2020-01-01T00:00:00Z", isReady: true, isSick: false, rushCostPremium: 0 }],
  wallet: { SOFT: 100, PREMIUM: 41 },
};

describe("Acelerar com moeda premium", () => {
  it("mostra o custo em premium, acelera e libera a coleta", () => {
    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
    cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
    cy.intercept("GET", "/api/game/tank", { body: tankWithPendingItem }).as("tank");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
    cy.contains("button", "Mergulhar").click();
    cy.wait("@login");
    cy.wait("@tank");

    cy.contains("💎50"); // saldo premium no topbar
    cy.contains("button", "⚡ 9").should("be.visible"); // custo de acelerar

    cy.intercept("POST", "/api/game/queue/42/rush", { statusCode: 200, body: { paid: 9, readyAt: "2020-01-01T00:00:00Z" } }).as("rush");
    cy.intercept("GET", "/api/game/tank", { body: tankWithReadyItem }).as("tankAfter");

    cy.contains("button", "⚡ 9").click();
    cy.wait("@rush");
    cy.wait("@tankAfter");

    cy.contains("button", "⚡ 9").should("not.exist");
    cy.contains("button", "Coletar").should("not.be.disabled");
    cy.contains("💎41"); // saldo premium debitado
  });

  it("saldo insuficiente: servidor recusa e a UI mostra o erro", () => {
    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
    cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
    cy.intercept("GET", "/api/game/tank", {
      body: { ...tankWithPendingItem, wallet: { SOFT: 100, PREMIUM: 0 } },
    }).as("tank");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
    cy.contains("button", "Mergulhar").click();
    cy.wait("@login");
    cy.wait("@tank");

    cy.intercept("POST", "/api/game/queue/42/rush", {
      statusCode: 400, body: { error: "Saldo de moeda premium insuficiente" },
    }).as("rushFail");

    cy.contains("button", "⚡ 9").click();
    cy.wait("@rushFail");
    cy.contains("Saldo de moeda premium insuficiente");
  });
});
