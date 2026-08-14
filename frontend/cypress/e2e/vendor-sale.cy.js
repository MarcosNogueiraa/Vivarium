// E2E da venda instantânea ao NPC (CLAUDE.md 8.12) — sink de duplicatas/comuns.
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

function creature(id, seed, rarityScore) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    traits: fakeTraits(seed), breedingSource: null,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0,
  };
}

const fish = creature(201, 4001, 6.06); // vendorPriceOf(6.06) = 7 (round(coinsPerHourOf(6.06) * 2), growth 0.42)

const tankWithFish = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5, queue: [], creatures: [fish],
  wallet: { SOFT: 100, PREMIUM: 0 }, coinsPerHour: 4.7, generationProgressMinutes: 0, generationIntervalMinutes: 60,
};

describe("Venda ao NPC", () => {
  it("mostra o preço, vende e o peixe some do tanque", () => {
    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
    cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
    cy.intercept("GET", "/api/game/tank", { body: tankWithFish }).as("tank");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
    cy.contains("button", "Mergulhar").click();
    cy.wait("@login");
    cy.wait("@tank");

    cy.contains("button.detail-section-head", "Peixes no tanque").click();
    cy.get(".fish-row").first().click();
    cy.contains("button", "Vender ao NPC · 7").scrollIntoView().should("be.visible").click();
    cy.contains("Venda instantânea por 7 moedas soft").should("be.visible");

    cy.intercept("POST", "/api/game/creatures/201/sell-vendor", { statusCode: 200, body: { price: 7, wallet: 107 } }).as("sell");
    cy.intercept("GET", "/api/game/tank", {
      body: { ...tankWithFish, creatures: [], coinsPerHour: 0, wallet: { SOFT: 107, PREMIUM: 0 } },
    }).as("tankAfter");

    cy.contains("button", "Vender agora").click();
    cy.wait("@sell");
    cy.wait("@tankAfter");

    cy.contains("Vendido ao NPC por 7 moedas.");
    cy.get(".fish-row").should("not.exist");
  });
});
