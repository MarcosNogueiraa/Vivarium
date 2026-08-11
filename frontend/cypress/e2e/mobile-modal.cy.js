// E2E do modal em mobile (11/08/2026) — screenshot real de celular mostrava o modal de
// detalhe do peixe "gigante e incompleto": o conteúdo passava do viewport, o jogador
// precisava rolar pra ver "Atributos"/"Por que é raro", e nesse ponto o botão de fechar (×)
// já tinha rolado pra fora da tela junto — virava uma "página" sem fim perceptível, não um
// modal contido. Fix: `.modal-close` agora fica fora da área que rola (`.modal-body`),
// então continua alcançável em qualquer ponto do scroll. `cy.viewport` com altura BAIXA
// força o overflow de propósito, mesmo num modal com pouco conteúdo.

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

// Score alto pra garantir várias linhas em "por que é raro" (mais fatores no breakdown).
const fish = creature(401, 9001, 12.4);
const tankWithFish = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5, queue: [], creatures: [fish],
  wallet: { SOFT: 100, PREMIUM: 0 }, coinsPerHour: 12.0, generationProgressMinutes: 0, generationIntervalMinutes: 60,
};

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { body: tankWithFish }).as("tank");
  cy.intercept("GET", "/api/game/daily-reward", { statusCode: 200, body: { canClaim: false, amount: 25, nextAvailableAtUtc: null } });

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
}

describe("Modal em mobile", () => {
  beforeEach(() => cy.viewport(390, 620)); // altura baixa de propósito: força o modal a rolar

  it("o × continua visível e clicável depois de rolar o conteúdo até o fim", () => {
    login();
    cy.get(".fish-row").first().click();
    cy.get(".modal").should("be.visible");
    cy.contains("Por que é raro").should("exist"); // existe no DOM, mesmo fora da vista ainda

    // As seções nascem fechadas (12/08/2026) — abrir as duas pra ter conteúdo alto o
    // suficiente e exercitar o overflow de verdade, mesmo objetivo original do teste.
    cy.contains("button.detail-section-head", "Atributos").click();
    cy.contains("button.detail-section-head", "Por que é raro").click();

    cy.get(".modal-close").should("be.visible");
    cy.get(".modal-body").scrollTo("bottom");
    cy.contains("Por que é raro").should("be.visible"); // agora visível, era o trecho que sumia no bug relatado

    // o × não pode ter rolado junto — continua no viewport do modal.
    cy.get(".modal-close").should("be.visible");
    cy.get(".modal-close").click();
    cy.get(".modal").should("not.exist");
  });

  it("o × fecha o modal mesmo sem rolar (caso feliz, sem regressão)", () => {
    login();
    cy.get(".fish-row").first().click();
    cy.get(".modal-close").should("be.visible").click();
    cy.get(".modal").should("not.exist");
  });
});
