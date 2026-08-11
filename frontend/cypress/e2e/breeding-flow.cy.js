// E2E do fluxo principal do Ninho (CLAUDE.md 8.8): escolher 2 peixes, ver a prévia,
// confirmar o cruzamento, e a gestação ativa (aguardando/pronta + acelerar com premium).
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

const parentA = creature(401, 111111, 5.2);
const parentB = creature(402, 222222, 6.8);

const quoteBody = {
  costSoft: 150, stabilizerCostSoft: 150, insuranceCostPremium: 60,
  gestationHours: 12.5,
  childTierProbabilities: { None: 0.78, Subtle: 0.15, Vibrant: 0.055, Rare: 0.013, Legendary: 0.002 },
  parentADeathChance: 0.05, parentBDeathChance: 0.05,
  parentABreedCount: 0, parentBBreedCount: 0,
  stabilizerReductionFactor: 0.5,
};

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

describe("Ninho — escolher e cruzar", () => {
  it("menos de 2 candidatos: mostra o aviso, sem grade de seleção", () => {
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpack");
    login();
    cy.wait(["@status", "@backpack"]);
    cy.contains("Você precisa de pelo menos 2 peixes");
    cy.get(".card").should("not.exist");
  });

  it("escolhe 2 peixes, vê a prévia e confirma o cruzamento", () => {
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [parentA, parentB] } }).as("backpack");
    login();
    cy.wait(["@status", "@backpack"]);

    cy.get(".card").should("have.length", 2);
    cy.get(".card").eq(0).find(".fish-stage").click();
    cy.get(".card").eq(1).find(".fish-stage").click();
    cy.contains("2 peixes selecionados");

    cy.intercept("GET", "/api/breeding/quote*", { body: quoteBody }).as("quote");
    cy.contains("button", "Ver chances e cruzar").click();
    cy.wait("@quote");

    cy.contains("Prévia do cruzamento");
    cy.contains("12.5h"); // hoursLabel
    cy.contains("Sem brilho"); // childTierProbabilities.None -> PT.tier.None
    cy.contains("Brilho lendário"); // childTierProbabilities.Legendary -> PT.tier.Legendary

    cy.intercept("POST", "/api/breeding/start", { statusCode: 200, body: {} }).as("start");
    cy.intercept("GET", "/api/breeding", {
      body: { active: true, slot: { parentA, parentB, readyAt: "2099-01-01T00:00:00Z", isReady: false, costPaid: 150, insuranceUsed: false, parentADeathChance: 0.05, parentBDeathChance: 0.05, rushCostPremium: 25 } },
    }).as("statusAfter");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpackAfter");

    cy.contains("button", "Confirmar cruzamento").click();
    // Ordenação padrão da grade é "rarity-desc" — parentB (6.8) some antes de parentA (5.2),
    // então é ele quem vira o "peixe A" (primeiro clicado).
    cy.wait("@start").its("request.body").should("deep.equal", { parentAId: 402, parentBId: 401, useStabilizer: false, useInsurance: false });
    cy.contains("Casal levado pro ninho!");
  });
});

describe("Ninho — gestação ativa", () => {
  it("gestação não pronta: coleta desabilitada, botão de acelerar pede confirmação", () => {
    cy.intercept("GET", "/api/breeding", {
      body: { active: true, slot: { parentA, parentB, readyAt: "2099-01-01T00:00:00Z", isReady: false, costPaid: 150, insuranceUsed: false, parentADeathChance: 0.12, parentBDeathChance: 0.08, rushCostPremium: 30 } },
    }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpack");
    login();
    cy.wait(["@status", "@backpack"]);

    cy.contains("button", "Aguardando…").should("be.disabled");
    cy.contains("button", "⚡ Acelerar com").click();
    cy.contains("Acelerar cruzamento").should("be.visible");
    cy.contains("💎 30");

    cy.intercept("POST", "/api/breeding/rush", { statusCode: 200, body: {} }).as("rush");
    cy.intercept("GET", "/api/breeding", {
      body: { active: true, slot: { parentA, parentB, readyAt: "2020-01-01T00:00:00Z", isReady: true, costPaid: 150, insuranceUsed: false, parentADeathChance: 0.12, parentBDeathChance: 0.08, rushCostPremium: 0 } },
    }).as("statusAfter");

    cy.contains("button", "Gastar premium e acelerar").click();
    cy.wait("@rush");
    cy.contains("Gestação acelerada!");
  });

  it("gestação pronta: coleta o filhote e mostra a celebração", () => {
    cy.intercept("GET", "/api/breeding", {
      body: { active: true, slot: { parentA, parentB, readyAt: "2020-01-01T00:00:00Z", isReady: true, costPaid: 150, insuranceUsed: true, parentADeathChance: 0, parentBDeathChance: 0, rushCostPremium: 0 } },
    }).as("status");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpack");
    login();
    cy.wait(["@status", "@backpack"]);

    cy.contains("🛡️ Seguro ativo");
    cy.contains("button", "Coletar filhote").should("not.be.disabled");

    const child = creature(403, 333333, 8.1);
    cy.intercept("POST", "/api/breeding/collect", { statusCode: 200, body: { child, parentADied: false, parentBDied: false } }).as("collect");
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }).as("statusAfter");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [child] } }).as("backpackAfter");

    cy.contains("button", "Coletar filhote").click();
    cy.wait("@collect");
    cy.get(".modal.celebrate").should("be.visible");
  });
});
