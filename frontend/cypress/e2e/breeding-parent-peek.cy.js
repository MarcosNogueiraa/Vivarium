// E2E: na celebração de coleta de um filhote (Ninho), os retratos dos pais
// mostram os atributos ao passar o mouse OU ao clicar/tocar (pedido do
// usuário — hover sozinho não funciona em telas sem mouse). API mockada.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function creature(id, seed, rarityScore) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0,
  };
}

const parentA = creature(201, 111, 5.0);
const parentB = creature(202, 222, 4.2);
const child = creature(203, 333, 3.1); // < 7.5: sem suspense, celebração já revelada

const activeSlot = {
  active: true,
  slot: {
    id: 1, parentA, parentB,
    startedAt: "2026-01-01T00:00:00Z", readyAt: "2020-01-01T00:00:00Z",
    isReady: true, costPaid: 150, rushCostPremium: 0,
    parentADeathChance: 0.05, parentBDeathChance: 0.05, insuranceUsed: false,
  },
};

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");
  cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } });
  cy.intercept("GET", "/api/breeding", { body: activeSlot }).as("breedingStatus");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Ninho").click();
  cy.wait("@breedingStatus");

  cy.intercept("POST", "/api/breeding/collect", {
    statusCode: 200, body: { child, parentADied: false, parentBDied: false },
  }).as("collect");
  cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } });

  cy.contains("button", "Coletar filhote").click();
  cy.wait("@collect");
  cy.get(".parents-chips .parent-chip").should("have.length", 2);
}

describe("Ninho — atributos dos pais na celebração", () => {
  it("passar o mouse mostra os atributos do pai", () => {
    login();
    // React sintetiza mouseenter/mouseleave a partir de mouseover/mouseout delegados na
    // raiz (mouseenter/mouseleave nativos não borbulham) — precisa disparar o evento que
    // React de fato escuta, não o sintético.
    cy.get(".parents-chips .parent-chip").eq(0).trigger("mouseover");
    cy.get(".peek-card").should("be.visible");
    cy.get(".peek-card").contains("5.0"); // rarityScore do parentA
    cy.get(".peek-card").contains("Cauda:");

    cy.get(".parents-chips .parent-chip").eq(0).trigger("mouseout");
    cy.get(".peek-card").should("not.exist");
  });

  it("clicar fixa os atributos abertos, clicar de novo fecha", () => {
    login();
    cy.get(".parents-chips .parent-chip").eq(1).click();
    cy.get(".peek-card").should("be.visible");
    cy.get(".peek-card").contains("4.2"); // rarityScore do parentB

    cy.get(".parents-chips .parent-chip").eq(1).click();
    cy.get(".peek-card").should("not.exist");
  });

  it("clicar num pai e depois no outro troca o preview fixado", () => {
    login();
    cy.get(".parents-chips .parent-chip").eq(0).click();
    cy.get(".peek-card").contains("5.0");

    cy.get(".parents-chips .parent-chip").eq(1).click();
    cy.get(".peek-card").contains("4.2");
    cy.get(".peek-card").should("have.length", 1);
  });

  it("preview fixado por clique não é sobrescrito ao passar o mouse no outro pai", () => {
    login();
    cy.get(".parents-chips .parent-chip").eq(0).click();
    cy.get(".peek-card").contains("5.0"); // fixado no pai A

    cy.get(".parents-chips .parent-chip").eq(1).trigger("mouseover");
    cy.get(".peek-card").contains("5.0"); // continua no pai A, hover não sobrescreve o fixado
    cy.get(".peek-card").should("have.length", 1);
  });
});
