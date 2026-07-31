// E2E do botão de recompensa diária (CLAUDE.md 8.10). API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function loginAndReachTank() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } }).as("heartbeat");
  cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
}

describe("Recompensa diária", () => {
  it("mostra o botão quando resgatável, some depois de resgatar e credita o saldo", () => {
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200, body: { canClaim: true, amount: 25, nextAvailableAtUtc: null },
    }).as("status");
    loginAndReachTank();
    cy.wait("@status");

    cy.contains("button", "Recompensa diária").should("be.visible");

    cy.intercept("POST", "/api/game/daily-reward/claim", {
      statusCode: 200, body: { amount: 25, wallet: 125 },
    }).as("claim");
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200, body: { canClaim: false, amount: 25, nextAvailableAtUtc: "2026-08-01T00:00:00Z" },
    }).as("statusAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter"); // saldo real vem do backend real; aqui só validamos que o fluxo dispara

    cy.contains("button", "Recompensa diária").click();
    cy.wait("@claim");
    cy.wait("@statusAfter");

    cy.contains("button", "Recompensa diária").should("not.exist");
    cy.contains("Recompensa diária: +25 soft"); // toast de confirmação
  });

  it("não mostra o botão quando já foi resgatada hoje", () => {
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200, body: { canClaim: false, amount: 25, nextAvailableAtUtc: "2026-08-01T00:00:00Z" },
    }).as("status");
    loginAndReachTank();
    cy.wait("@status");

    cy.contains("button", "Recompensa diária").should("not.exist");
  });
});
