// E2E do Mercado: comprar, cancelar, estado vazio, painel "meus anúncios", paginação,
// ordenação por preço e filtros por parte (13/08/2026 — resposta virou um envelope
// {listings, totalCount, myListings, myActiveListingsCount, maxActiveListings}).
// API mockada via cy.intercept.

function fakeTraits(seed, tailColor) {
  const colors = ["Orange", "Blue", "Red", "Yellow", "Green", "Purple", "Black", "PureWhite"];
  const color = tailColor ?? colors[Number(BigInt(seed) % 8n)];
  const tail = { color, pattern: "None", patternColor: null, patternSize: null, patternOpacity: null, mix: null };
  const rest = { color: "Orange", pattern: "None", patternColor: null, patternSize: null, patternOpacity: null, mix: null };
  return {
    shimmerTier: "None", shimmerColor: null, shimmerOpacity: 0,
    tail, dorsal: rest, pectoral: rest,
    movement: { tailSpeed: 50, tailAmplitude: 0.4, finSpeed: 50, finAmplitude: 0.3 },
  };
}

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function listing(id, sellerId, sellerName, seed, rarityScore, priceSoft, tailColor) {
  return {
    id, creatureInstanceId: id * 10, sellerId, sellerName, priceSoft,
    seed: String(seed), rarityScore, isBred: false, parentASeed: null, parentBSeed: null,
    traits: fakeTraits(seed, tailColor), breedingSource: null,
  };
}

function envelope({ listings = [], totalCount, myListings = [], myActiveListingsCount = 0, maxActiveListings = 50 } = {}) {
  return {
    listings, totalCount: totalCount ?? listings.length,
    myListings, myActiveListingsCount, maxActiveListings,
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
    cy.intercept("GET", "/api/market/listings*", { body: envelope() }).as("listings");
    login();
    cy.wait("@listings");
    cy.contains("O mercado está vazio");
    cy.get(".card").should("not.exist");
  });

  it("comprar uma listagem de outro jogador", () => {
    const l = listing(501, 2, "outrojogador", 7777, 6.4, 42);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains(".card", "de outrojogador").within(() => {
      cy.contains("button", "Comprar").should("be.visible");
      cy.contains("button", "Cancelar").should("not.exist");
    });

    cy.intercept("POST", "/api/market/listings/501/buy", { statusCode: 200, body: { price: 42 } }).as("buy");
    cy.intercept("GET", "/api/market/listings*", { body: envelope() }).as("listingsAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.contains(".card", "de outrojogador").contains("button", "Comprar").click();
    cy.wait("@buy");
    cy.wait("@listingsAfter");

    cy.contains("Comprado por 42 soft!");
    cy.contains("O mercado está vazio");
  });

  it("listagem própria aparece no painel 'Meus anúncios ativos', cancelar devolve ao tanque", () => {
    const own = listing(502, 1, "jogador1", 8888, 5.1, 30);
    cy.intercept("GET", "/api/market/listings*", {
      body: envelope({ myListings: [own], myActiveListingsCount: 1 }),
    }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains("Meus anúncios ativos");
    cy.contains("1/50");
    cy.get(".market-mine .card").within(() => {
      cy.contains("button", "Cancelar").should("be.visible");
      cy.contains("button", "Comprar").should("not.exist");
    });
    cy.contains("Nenhum peixe corresponde a esse filtro"); // grade geral vazia (a própria listagem não duplica lá)

    cy.intercept("POST", "/api/market/listings/502/cancel", { statusCode: 200, body: {} }).as("cancel");
    cy.intercept("GET", "/api/market/listings*", { body: envelope() }).as("listingsAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.get(".market-mine").contains("button", "Cancelar").click();
    cy.wait("@cancel");
    cy.wait("@listingsAfter");
    cy.contains("Você não tem anúncios ativos");
  });

  it("erro do servidor ao comprar aparece via toast, listagem continua visível", () => {
    const l = listing(503, 2, "outrojogador", 9999, 4.0, 15);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
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

  it("paginação: navega entre páginas via Anterior/Próxima", () => {
    const l = listing(600, 2, "vendedor", 1111, 5.0, 10);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l], totalCount: 50 }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains("Página 1 de 3");
    cy.contains("‹ Anterior").should("be.disabled");
    cy.contains("Próxima ›").click();
    cy.contains("Página 2 de 3");
    cy.contains("‹ Anterior").should("not.be.disabled");
  });

  it("ordenar por preço manda o parâmetro certo pra API", () => {
    const l = listing(700, 2, "vendedor", 1111, 5.0, 10);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.get(".custom-select-btn").first().click();
    cy.contains(".custom-select-option", "Preço (menor primeiro)").click();
    cy.wait("@listings").its("request.url").should("include", "sort=price-asc");
  });

  it("filtro por cor da cauda manda o parâmetro certo pra API", () => {
    const l = listing(701, 2, "vendedor", 1111, 5.0, 10);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains("button.detail-section-head", "Filtros avançados").click();
    cy.get(".appearance-filter-part").first().within(() => {
      cy.get(".color-chip").each(($el) => {
        if ($el.attr("title") === "Azul") cy.wrap($el).click();
      });
    });
    cy.wait("@listings").its("request.url").should("include", "tailColor=Blue");
    cy.contains("button.detail-section-head", "Filtros avançados (1)");
  });
});
