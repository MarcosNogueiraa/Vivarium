// E2E dos toggles de coleta automática / Limpeza Automática de VIP (opt-out, §8.3/§8.18).
// API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function login(tankOverrides = {}) {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.fixture("tank-empty.json").then((base) => {
    cy.intercept("GET", "/api/game/tank", { body: { ...base, isVip: true, vipEndAt: "2027-01-01T00:00:00Z", ...tankOverrides } }).as("tank");
  });
  cy.intercept("GET", "/api/items/", { body: [] }).as("items");
  cy.intercept("GET", "/api/vip", { body: { packages: { 7: 7, 15: 10, 30: 15 } } }).as("vip");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Loja").click();
  cy.wait("@items");
}

describe("Toggles de VIP (coleta/limpeza automática)", () => {
  it("nasce com os dois toggles ligados (default true)", () => {
    login();
    cy.contains("label", "Coleta automática").find("input[type=checkbox]").should("be.checked");
    cy.contains("label", "Limpeza automática").find("input[type=checkbox]").should("be.checked");
  });

  it("desligar a coleta automática chama a API e persiste no reload", () => {
    login();
    cy.intercept("POST", "/api/game/toggles", (req) => {
      expect(req.body).to.deep.equal({ autoCollectEnabled: false, autoCleanEnabled: true });
      req.reply({ statusCode: 200, body: { autoCollectEnabled: false, autoCleanEnabled: true } });
    }).as("setToggles");
    cy.fixture("tank-empty.json").then((base) => {
      cy.intercept("GET", "/api/game/tank", { body: { ...base, isVip: true, autoCollectEnabled: false, autoCleanEnabled: true } }).as("tankAfter");
    });

    cy.contains("label", "Coleta automática").find("input[type=checkbox]").uncheck({ force: true });
    cy.wait("@setToggles");
    cy.wait("@tankAfter");
    cy.contains("label", "Coleta automática").find("input[type=checkbox]").should("not.be.checked");
    cy.contains("label", "Limpeza automática").find("input[type=checkbox]").should("be.checked");
  });

  it("sem VIP ativo, mostra aviso de que só tem efeito com VIP", () => {
    login({ isVip: false, vipEndAt: null });
    cy.contains("Só tem efeito com VIP ativo");
  });
});
