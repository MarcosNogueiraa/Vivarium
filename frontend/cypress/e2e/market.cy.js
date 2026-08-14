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

    // Nasce minimizada (13/08/2026) — o card só existe no DOM depois de expandir.
    cy.contains("button.detail-section-head", "Meus anúncios ativos");
    cy.contains("1/50");
    cy.get(".card").should("not.exist");
    cy.contains("button.detail-section-head", "Meus anúncios ativos").click();
    cy.get(".card").within(() => {
      cy.contains("button", "Cancelar").should("be.visible");
      cy.contains("button", "Comprar").should("not.exist");
    });
    cy.contains("Nenhum peixe corresponde a esse filtro"); // grade geral vazia (a própria listagem não duplica lá)

    cy.intercept("POST", "/api/market/listings/502/cancel", { statusCode: 200, body: {} }).as("cancel");
    cy.intercept("GET", "/api/market/listings*", { body: envelope() }).as("listingsAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.get(".card").contains("button", "Cancelar").click();
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

  it("marcar duas cores na mesma parte manda os dois valores (OU) pra API", () => {
    const l = listing(702, 2, "vendedor", 1111, 5.0, 10);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains("button.detail-section-head", "Filtros avançados").click();
    cy.get(".appearance-filter-part").first().within(() => {
      cy.get('.color-chip[title="Azul"]').click();
    });
    cy.wait("@listings");
    cy.get(".appearance-filter-part").first().within(() => {
      cy.get('.color-chip[title="Vermelho"]').click();
    });
    cy.wait("@listings").its("request.url").should("match", /tailColor=(Blue%2CRed|Red%2CBlue)/);
    cy.contains("button.detail-section-head", "Filtros avançados (2)");
  });

  it("botão de redefinir filtros limpa banda e filtros de parte", () => {
    const l = listing(703, 2, "vendedor", 1111, 5.0, 10);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains(".filter-chips", "Todos").contains("button", "Raro").click();
    cy.wait("@listings");
    cy.contains("button", "↺ Redefinir filtros").should("be.visible");

    cy.contains("button", "↺ Redefinir filtros").click();
    cy.wait("@listings").its("request.url").should("not.include", "band=");
    cy.contains("button", "↺ Redefinir filtros").should("not.exist");
    cy.contains(".filter-chips", "Todos").contains("button.active", "Todos");
  });

  it("mobile: cabeçalho de 'Meus anúncios ativos' tem alvo de toque grande e visual de cartão", () => {
    const l = listing(704, 2, "vendedor", 1111, 5.0, 10);
    cy.viewport(375, 800);
    cy.intercept("GET", "/api/market/listings*", { body: envelope({ listings: [l] }) }).as("listings");
    login();
    cy.wait("@listings");

    cy.contains("button.detail-section-head--prominent", "Meus anúncios ativos").then(($btn) => {
      expect($btn[0].getBoundingClientRect().height).to.be.at.least(48);
    });
    cy.contains("button.detail-section-head--prominent", "Meus anúncios ativos")
      .closest(".detail-section--prominent")
      .should("have.css", "background-color")
      .and("not.equal", "rgba(0, 0, 0, 0)");
  });
});
