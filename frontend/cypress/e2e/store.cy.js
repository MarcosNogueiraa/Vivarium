// E2E da Loja: compra normal, aviso de "água já limpa" antes de comprar filtro
// manual com água alta, e item bloqueado sem botão de compra. API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const items = [
  { key: "filter_basic", name: "Filtro", price: 20, owned: false, locked: false },
  { key: "auto_filter", name: "Filtro Automático", price: 500, owned: false, locked: false },
  { key: "tank_upgrade", name: "Expansão do Tanque", price: 75, owned: false, locked: false },
  {
    key: "aquario_grande", name: "Aquário Grande", price: 4000, owned: false,
    locked: true, lockedReason: "Disponível ao chegar em 5 de capacidade.",
  },
];

function login(tankOverrides = {}) {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.fixture("tank-empty.json").then((base) => {
    cy.intercept("GET", "/api/game/tank", { body: { ...base, ...tankOverrides } }).as("tank");
  });

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Loja").click();
}

describe("Loja", () => {
  it("compra um item comum sem aviso quando a água não está no teto", () => {
    cy.intercept("GET", "/api/items/", { body: items }).as("items");
    login({ maintenanceLevel: 40 });
    cy.wait("@items");

    cy.intercept("POST", "/api/items/tank_upgrade/buy", { statusCode: 200, body: {} }).as("buy");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");
    cy.intercept("GET", "/api/items/", { body: items.map((i) => i.key === "tank_upgrade" ? { ...i, owned: true } : i) }).as("itemsAfter");

    cy.contains(".card", "Expansão do Tanque").contains("button", "Comprar").click();
    cy.wait("@buy");
    cy.contains("Expansão do Tanque comprado!");
  });

  it("comprar filtro manual com água alta (>=95) pede confirmação antes de gastar", () => {
    cy.intercept("GET", "/api/items/", { body: items }).as("items");
    login({ maintenanceLevel: 98 });
    cy.wait("@items");

    cy.contains("strong", /^Filtro$/).closest(".card").contains("button", "Comprar").click();
    cy.contains("Água já está limpa").should("be.visible");
    cy.contains("98%");

    cy.intercept("POST", "/api/items/filter_basic/buy", { statusCode: 200, body: {} }).as("buy");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.contains("button", "Comprar mesmo assim").click();
    cy.wait("@buy");
    cy.contains("Filtro comprado!");
  });

  it("item bloqueado mostra o motivo e não tem botão de compra", () => {
    cy.intercept("GET", "/api/items/", { body: items }).as("items");
    login({ maintenanceLevel: 40 });
    cy.wait("@items");

    cy.contains(".card", "Aquário Grande").within(() => {
      cy.contains("Bloqueado");
      cy.contains("Disponível ao chegar em 5 de capacidade.");
      cy.get("button.btn-primary").should("not.exist");
    });
  });

  it("saldo insuficiente: servidor recusa e o erro aparece via toast", () => {
    cy.intercept("GET", "/api/items/", { body: items }).as("items");
    login({ maintenanceLevel: 40 });
    cy.wait("@items");

    cy.intercept("POST", "/api/items/auto_filter/buy", {
      statusCode: 400, body: { error: "Saldo de moeda soft insuficiente" },
    }).as("buyFail");

    cy.contains("strong", "Filtro Automático").closest(".card").contains("button", "Comprar").click();
    cy.wait("@buyFail");
    cy.contains("Saldo de moeda soft insuficiente");
  });

  const eggItems = [
    ...items,
    { key: "egg_common", name: "Ovo Comum", price: 8, owned: false, locked: false, currency: "PREMIUM" },
  ];

  it("ovo: mostra preço em diamante e desabilita o botão sem saldo premium", () => {
    cy.intercept("GET", "/api/items/", { body: eggItems }).as("items");
    login({ maintenanceLevel: 40 }); // wallet.PREMIUM = 0 (tank-empty.json)
    cy.wait("@items");

    cy.contains(".card", "Ovo Comum").within(() => {
      cy.contains("💎8");
      cy.get("button.btn-primary").should("be.disabled");
    });
  });

  it("ovo: compra gera peixe e mostra a raridade sem abrir celebração pra Comum/Incomum", () => {
    cy.intercept("GET", "/api/items/", { body: eggItems }).as("items");
    login({ maintenanceLevel: 40, wallet: { SOFT: 100, PREMIUM: 50 } });
    cy.wait("@items");

    cy.intercept("POST", "/api/items/egg_common/buy", {
      statusCode: 200,
      body: { paid: 8, creature: { id: 999, rarityScore: 3, traits: {} } },
    }).as("buyEgg");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.contains(".card", "Ovo Comum").contains("button", "Comprar").click();
    cy.wait("@buyEgg");
    cy.contains("Ovo Comum: Comum!");
    cy.get(".celebrate").should("not.exist");
  });
});
