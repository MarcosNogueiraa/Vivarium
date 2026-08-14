// E2E do polish mobile do Tanque (13/08/2026, print real de celular do usuário):
// long-press revela o nome de um botão-ícone sem disparar a ação, e a lista "Peixes no
// tanque" nasce minimizada. API mockada via cy.intercept.

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

const tankWithFish = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5, queue: [], creatures: [creature(201, 4001, 6.06)],
  wallet: { SOFT: 100, PREMIUM: 0 }, coinsPerHour: 4.7, generationProgressMinutes: 0, generationIntervalMinutes: 60,
};

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { body: tankWithFish }).as("tank");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
}

describe("Tanque — polish mobile (long-press e lista minimizada)", () => {
  it("segurar um botão-ícone mostra o nome sem disparar a ação", () => {
    cy.clock();
    login();

    // "Modo aquário" (🐠) ainda não foi ativado.
    cy.get(".tank-stage").should("not.have.class", "aquarium-mode");

    cy.contains(".tool-btn", "🐠").trigger("touchstart");
    cy.tick(600);
    cy.get(".icon-btn-tip").should("be.visible").and("contain.text", "Modo aquário");

    cy.contains(".tool-btn", "🐠").trigger("touchend");
    cy.get(".icon-btn-tip").should("not.exist");
    // Segurar não deveria ter ativado o modo aquário.
    cy.get(".tank-stage").should("not.have.class", "aquarium-mode");
  });

  it("toque rápido continua ativando o botão normalmente", () => {
    login();
    cy.get(".tank-stage").should("not.have.class", "aquarium-mode");
    cy.contains(".tool-btn", "🐠").click();
    cy.get(".tank-stage").should("have.class", "aquarium-mode");
  });

  it("'Peixes no tanque' nasce minimizada", () => {
    login();
    cy.contains("button.detail-section-head", "Peixes no tanque").should("be.visible");
    cy.get(".fish-list").should("not.exist");

    cy.contains("button.detail-section-head", "Peixes no tanque").click();
    cy.get(".fish-list").should("be.visible");
    cy.get(".fish-row").should("have.length", 1);
  });

  it("o HUD de status fica acima do aquário, sem sobrepor o canvas", () => {
    login();
    cy.get(".tank-hud-bar").should("be.visible");
    cy.get(".tank-hud-bar").then(($hud) => {
      const hudRect = $hud[0].getBoundingClientRect();
      cy.get(".aquarium").then(($canvas) => {
        const canvasRect = $canvas[0].getBoundingClientRect();
        // O HUD termina antes do canvas começar (sem overlap vertical).
        expect(hudRect.bottom).to.be.lte(canvasRect.top + 1);
      });
    });
  });
});
