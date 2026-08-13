// E2E dos filtros avançados por parte no Ninho (13/08/2026) — mesma solução independente
// por parte (cor + padrão de cauda/dorsal/peitoral) já usada na Mochila, replicada aqui.
// API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function part(color, pattern = "None") {
  return { color, pattern, patternColor: pattern === "None" ? null : "Black", patternSize: pattern === "None" ? null : 50, patternOpacity: pattern === "None" ? null : 50, mix: null };
}

function creature(id, seed, rarityScore, tailPart) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    traits: {
      shimmerTier: "None", shimmerColor: null, shimmerOpacity: 0,
      tail: tailPart, dorsal: part("Orange"), pectoral: part("Orange"),
      movement: { tailSpeed: 50, tailAmplitude: 0.4, finSpeed: 50, finAmplitude: 0.3 },
    },
    breedingSource: null,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0,
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
  cy.contains("button", "Ninho").click();
}

describe("Ninho — filtros avançados por parte", () => {
  it("filtra candidatos por cor da cauda, independente das outras partes", () => {
    const blue = creature(501, 111111, 5.0, part("Blue"));
    const orange = creature(502, 222222, 5.5, part("Orange"));
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [blue, orange] } }).as("backpack");
    login();
    cy.wait(["@status", "@backpack"]);

    cy.get(".card").should("have.length", 2);
    cy.contains("button.detail-section-head", "Filtros avançados").click();

    // Filtro de cor da CAUDA por Azul — só o peixe com cauda Blue passa.
    cy.get(".appearance-filter-part").first().within(() => {
      cy.get(".color-chip").each(($el) => {
        if ($el.attr("title") === "Azul") cy.wrap($el).click();
      });
    });
    cy.get(".card").should("have.length", 1);

    // Badge de contagem de filtros ativos aparece no cabeçalho da seção.
    cy.contains("button.detail-section-head", "Filtros avançados (1)");

    // Voltar pra "Toda cor" reexibe os dois.
    cy.get(".appearance-filter-part").first().within(() => {
      cy.contains(".filter-chip", "Toda cor").click();
    });
    cy.get(".card").should("have.length", 2);
  });

  it("filtro por padrão da cauda combina com o filtro de cor (AND)", () => {
    const bluePatterned = creature(503, 333333, 6.0, part("Blue", "Stripe"));
    const bluePlain = creature(504, 444444, 6.5, part("Blue"));
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [bluePatterned, bluePlain] } }).as("backpack");
    login();
    cy.wait(["@status", "@backpack"]);

    cy.get(".card").should("have.length", 2);
    cy.contains("button.detail-section-head", "Filtros avançados").click();

    cy.get(".appearance-filter-part").first().within(() => {
      cy.get(".color-chip").each(($el) => {
        if ($el.attr("title") === "Azul") cy.wrap($el).click();
      });
    });
    cy.get(".card").should("have.length", 2); // os dois têm cauda azul

    cy.get(".appearance-filter-part").first().within(() => {
      cy.contains(".filter-chip", "Estria").click();
    });
    cy.get(".card").should("have.length", 1); // só o com padrão Estria passa agora
  });
});
