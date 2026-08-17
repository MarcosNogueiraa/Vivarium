// E2E da recompensa diária (CLAUDE.md §8.10, redesenho 17/08/2026 — roleta + streak + bônus).
// API mockada via cy.intercept.

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
  it("mostra o botão quando resgatável; abrir a roleta mostra sequência/bônus/faixa e resgatar credita o saldo", () => {
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200,
      body: { canClaim: true, minAmount: 15, maxAmount: 35, currentStreak: 3, streakBonusPercent: 10, eggChancePercent: 3, nextAvailableAtUtc: null },
    }).as("status");
    loginAndReachTank();
    cy.wait("@status");

    cy.contains("button", "Recompensa diária").should("be.visible").click();

    cy.contains("3 dias seguidos");
    cy.contains("+10% de bônus");
    cy.contains("15–35 soft");

    cy.intercept("POST", "/api/game/daily-reward/claim", {
      statusCode: 200, body: { amount: 27, wallet: 127, streak: 3, gotEgg: false },
    }).as("claim");
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200,
      body: { canClaim: false, minAmount: 15, maxAmount: 35, currentStreak: 3, streakBonusPercent: 10, eggChancePercent: 3, nextAvailableAtUtc: "2026-08-18T00:00:00Z" },
    }).as("statusAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");
    cy.intercept("GET", "/api/inbox/", { statusCode: 200, body: { entries: [] } }).as("inboxAfter");

    cy.contains("button", "Resgatar").click();
    cy.wait("@claim");
    cy.wait("@statusAfter");

    cy.contains(".daily-reward-roulette-value", "27", { timeout: 4000 });
    cy.contains("Creditado na carteira");

    cy.contains("button", "Fechar").click();
    cy.contains("button", "Recompensa diária").should("not.exist");
  });

  it("mostra o brinde de ovo quando a roleta concede um", () => {
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200,
      body: { canClaim: true, minAmount: 25, maxAmount: 25, currentStreak: 1, streakBonusPercent: 0, eggChancePercent: 3, nextAvailableAtUtc: null },
    }).as("status");
    loginAndReachTank();
    cy.wait("@status");

    cy.contains("button", "Recompensa diária").click();

    cy.intercept("POST", "/api/game/daily-reward/claim", {
      statusCode: 200, body: { amount: 25, wallet: 125, streak: 1, gotEgg: true, eggItemKey: "egg_rare" },
    }).as("claim");
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200,
      body: { canClaim: false, minAmount: 25, maxAmount: 25, currentStreak: 1, streakBonusPercent: 0, eggChancePercent: 3, nextAvailableAtUtc: "2026-08-18T00:00:00Z" },
    }).as("statusAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");
    cy.intercept("GET", "/api/inbox/", { statusCode: 200, body: { entries: [] } }).as("inboxAfter");

    cy.contains("button", "Resgatar").click();
    cy.wait("@claim");

    cy.contains("Sorte grande", { timeout: 4000 });
    cy.contains("Ovo Raro caiu na sua Caixa de Entrada");
  });

  it("não mostra o botão quando já foi resgatada hoje", () => {
    cy.intercept("GET", "/api/game/daily-reward", {
      statusCode: 200,
      body: { canClaim: false, minAmount: 25, maxAmount: 25, currentStreak: 1, streakBonusPercent: 0, eggChancePercent: 3, nextAvailableAtUtc: "2026-08-18T00:00:00Z" },
    }).as("status");
    loginAndReachTank();
    cy.wait("@status");

    cy.contains("button", "Recompensa diária").should("not.exist");
  });
});
