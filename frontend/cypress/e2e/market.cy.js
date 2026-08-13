// E2E do Mercado: comprar de outro jogador, cancelar a própria listagem, estado vazio.
// API mockada via cy.intercept.

function fakeTraits(seed) {
  const colors = ["Orange", "Blue", "Red", "Yellow", "Green", "Purple", "Black", "PureWhite"];
  const color = colors[Number(BigInt(seed) % 8n)];
  const part = { color, pattern: "None", patternColor: null, patternSize: null, patternOpacity: null, mix: null };
  return {
    shimmerTier: "None", shimmerColor: null, shimmerOpacity: 0,
    tail: part, dorsal: part, pectoral: part,
    movement: { tailSpeed: 50, tailAmplitude: 0.4, finSpeed: 50, finAmplitude: 0.3 },
  };
}

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function listing(id, sellerId, sellerName, seed, rarityScore, priceSoft) {
  return {
    id, creatureInstanceId: id * 10, sellerId, sellerName, priceSoft,
    seed: String(seed), rarityScore, isBred: false, parentASeed: null, parentBSeed: null,
    traits: fakeTraits(seed), breedingSource: null,
  };
}

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Mercado").click();
}

describe("Mercado", () => {
  it("mercado vazio mostra o estado vazio, sem cards", () => {
    cy.intercept("GET", "/api/market/listings", { body: [] }).as("listings");
    login();
    cy.wait("@listings");
    cy.contains("O mercado está vazio");
    cy.get(".card").should("not.exist");
  });

  it("comprar uma listagem de outro jogador", () => {
    const l = listing(501, 2, "outrojogador", 7777, 6.4, 42);
    cy.intercept("GET", "/api/market/listings", { body: [l] }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains(".card", "de outrojogador").within(() => {
      cy.contains("button", "Comprar").should("be.visible");
      cy.contains("button", "Cancelar").should("not.exist");
    });

    cy.intercept("POST", "/api/market/listings/501/buy", { statusCode: 200, body: { price: 42 } }).as("buy");
    cy.intercept("GET", "/api/market/listings", { body: [] }).as("listingsAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.contains(".card", "de outrojogador").contains("button", "Comprar").click();
    cy.wait("@buy");
    cy.wait("@listingsAfter");

    cy.contains("Comprado por 42 soft!");
    cy.contains("O mercado está vazio");
  });

  it("listagem própria mostra Cancelar em vez de Comprar, e cancelar devolve ao tanque", () => {
    const own = listing(502, 1, "jogador1", 8888, 5.1, 30);
    cy.intercept("GET", "/api/market/listings", { body: [own] }).as("listings");
    login();
    cy.wait("@listings");

    cy.get(".card").within(() => {
      cy.contains("button", "Cancelar").should("be.visible");
      cy.contains("button", "Comprar").should("not.exist");
    });

    cy.intercept("POST", "/api/market/listings/502/cancel", { statusCode: 200, body: {} }).as("cancel");
    cy.intercept("GET", "/api/market/listings", { body: [] }).as("listingsAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.contains("button", "Cancelar").click();
    cy.wait("@cancel");
    cy.wait("@listingsAfter");
    cy.contains("O mercado está vazio");
  });

  it("erro do servidor ao comprar aparece via toast, listagem continua visível", () => {
    const l = listing(503, 2, "outrojogador", 9999, 4.0, 15);
    cy.intercept("GET", "/api/market/listings", { body: [l] }).as("listings");
    login();
    cy.wait("@listings");

    cy.intercept("POST", "/api/market/listings/503/buy", {
      statusCode: 409, body: { error: "Essa listagem já não está mais disponível" },
    }).as("buyFail");

    cy.contains("button", "Comprar").click();
    cy.wait("@buyFail");
    cy.contains("Essa listagem já não está mais disponível");
    cy.get(".card").should("exist"); // não sumiu do mercado
  });
});
