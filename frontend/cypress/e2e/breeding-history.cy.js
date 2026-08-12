// E2E do Registro de cruzamentos (§8.19): abre pelo botão "📜" no Ninho (ativo ou vazio),
// lista gestações passadas com pais/filhote e quem morreu, e é responsivo em mobile.
// API mockada via cy.intercept.

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

const historyBody = [
  {
    id: 1, parentA: creature(801, 111, 6.2), parentB: creature(802, 222, 5.2), child: creature(899, 333, 3.0),
    startedAt: "2026-08-12T17:02:01Z", readyAt: "2026-08-12T18:02:01Z", costPaid: 150,
    parentADied: false, parentBDied: false, insuranceUsed: false,
  },
  {
    id: 2, parentA: creature(796, 444, 6.2046), parentB: creature(795, 555, 5.2165), child: creature(819, 666, 7.7827),
    startedAt: "2026-08-12T14:40:01Z", readyAt: "2026-08-12T15:40:01Z", costPaid: 200,
    parentADied: false, parentBDied: true, insuranceUsed: false,
  },
];

function login(tankOverrides = {}) {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");
  cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null, ...tankOverrides } }).as("status");
  cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpack");
  cy.intercept("GET", "/api/breeding/history", { body: historyBody }).as("history");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Ninho").click();
  cy.wait(["@status", "@backpack"]);
}

describe("Registro de cruzamentos", () => {
  it("abre pelo botão do Ninho e lista as gestações passadas", () => {
    login();
    cy.get('button[title="Registro de cruzamentos"]').click();
    cy.wait("@history");

    cy.contains("Registro de cruzamentos");
    cy.get(".hist-entry").should("have.length", 2);
    // Entrada com um pai morto mostra o selo de despedida.
    cy.get(".hist-entry").eq(1).find(".hist-fish-rip").should("exist");
    cy.get(".hist-entry").eq(0).find(".hist-fish-rip").should("not.exist");
  });

  it("fecha e reabre sem duplicar requisições/estado", () => {
    login();
    cy.get('button[title="Registro de cruzamentos"]').click();
    cy.wait("@history");
    cy.get(".modal-close").click();
    cy.get(".hist-entry").should("not.exist");

    cy.get('button[title="Registro de cruzamentos"]').click();
    cy.wait("@history");
    cy.get(".hist-entry").should("have.length", 2);
  });

  it("sem cruzamentos ainda: mostra aviso, não a lista", () => {
    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
    cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpack");
    cy.intercept("GET", "/api/breeding/history", { body: [] }).as("emptyHistory");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
    cy.contains("button", "Mergulhar").click();
    cy.wait(["@login", "@tank"]);
    cy.contains("button", "Ninho").click();
    cy.wait(["@status", "@backpack"]);

    cy.get('button[title="Registro de cruzamentos"]').click();
    cy.wait("@emptyHistory");
    cy.contains("Nenhum cruzamento coletado ainda.");
    cy.get(".hist-entry").should("not.exist");
  });

  it("mobile (375px): lista responsiva, sem overflow horizontal, × sempre clicável", () => {
    cy.viewport(375, 700);
    login();
    cy.get('button[title="Registro de cruzamentos"]').click();
    cy.wait("@history");

    cy.get(".hist-entry").should("have.length", 2);
    cy.document().then((doc) => {
      expect(doc.documentElement.scrollWidth).to.be.at.most(doc.documentElement.clientWidth + 1);
    });
    cy.get(".modal-close").should("be.visible").click();
    cy.get(".hist-entry").should("not.exist");
  });
});
