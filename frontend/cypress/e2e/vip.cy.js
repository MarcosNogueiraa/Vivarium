// E2E do VIP (11/08/2026): pacotes de dias pagos em premium, sem renovação automática.
// API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const vipStatusInactive = { active: false, endAt: null, packages: { 7: 7, 15: 10, 30: 15 } };

function login(tankOverrides = {}, vipStatus = vipStatusInactive) {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.fixture("tank-empty.json").then((base) => {
    cy.intercept("GET", "/api/game/tank", { body: { ...base, ...tankOverrides } }).as("tank");
  });
  cy.intercept("GET", "/api/vip", { body: vipStatus }).as("vip");
  cy.intercept("GET", "/api/items/", { body: [] });

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Loja").click();
  cy.wait("@vip");
}

describe("VIP", () => {
  it("mostra os 3 pacotes com preço e nenhum VIP ativo", () => {
    login({ wallet: { SOFT: 100, PREMIUM: 20 } });

    cy.contains(".vip-card", "Sem VIP ativo agora.");
    cy.contains(".vip-card button", "7d · 💎7");
    cy.contains(".vip-card button", "15d · 💎10");
    cy.contains(".vip-card button", "30d · 💎15");
  });

  it("botão de um pacote fica desabilitado sem saldo suficiente", () => {
    login({ wallet: { SOFT: 100, PREMIUM: 5 } });

    cy.contains(".vip-card button", "7d · 💎7").should("be.disabled");
    cy.contains(".vip-card button", "15d · 💎10").should("be.disabled");
    cy.contains(".vip-card button", "30d · 💎15").should("be.disabled");
  });

  it("comprar um pacote ativa o VIP e mostra o selo no topo", () => {
    login({ wallet: { SOFT: 100, PREMIUM: 20 } });

    const endAt = new Date(Date.now() + 7 * 86_400_000).toISOString();
    cy.intercept("POST", "/api/vip/subscribe", { statusCode: 200, body: { paid: 7, endAt } }).as("subscribe");
    cy.intercept("GET", "/api/vip", { body: { active: true, endAt, packages: vipStatusInactive.packages } }).as("vipAfter");
    cy.fixture("tank-empty.json").then((base) => {
      cy.intercept("GET", "/api/game/tank", {
        body: { ...base, wallet: { SOFT: 100, PREMIUM: 13 }, isVip: true, vipEndAt: endAt },
      }).as("tankAfter");
    });

    cy.contains(".vip-card button", "7d · 💎7").click();
    cy.wait("@subscribe").its("request.body").should("deep.equal", { days: 7 });
    cy.contains("VIP +7 dia(s)!");
    cy.contains(".vip-card", "Ativo até");
    cy.contains(".vip-chip", "VIP");
  });

  it("erro do servidor aparece via toast", () => {
    login({ wallet: { SOFT: 100, PREMIUM: 20 } });

    cy.intercept("POST", "/api/vip/subscribe", {
      statusCode: 400, body: { error: "Saldo de moeda premium insuficiente" },
    }).as("subscribeFail");

    cy.contains(".vip-card button", "7d · 💎7").click();
    cy.wait("@subscribeFail");
    cy.contains("Saldo de moeda premium insuficiente");
  });
});
