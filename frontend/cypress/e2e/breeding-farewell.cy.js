// E2E da "despedida" quando um pai não sobrevive à gestação (CLAUDE.md 8.8).
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
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 2,
  };
}

const parentA = creature(101, 111, 6.2);
const parentB = creature(102, 222, 7.1);
const child = creature(103, 333, 13.0); // Raro (corte atual: score >= 12.04)

const activeSlot = {
  active: true,
  slot: {
    id: 1, parentA, parentB,
    startedAt: "2026-01-01T00:00:00Z", readyAt: "2020-01-01T00:00:00Z", // no passado = pronto
    isReady: true, costPaid: 200, rushCostPremium: 0,
    parentADeathChance: 0.23, parentBDeathChance: 0.23, insuranceUsed: false,
  },
};

describe("Despedida do pai que não sobrevive à gestação", () => {
  it("mostra o retrato do pai perdido, não só uma contagem", () => {
    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
    cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
    cy.intercept("GET", "/api/game/tank", {
      body: {
        online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5, queue: [], creatures: [],
        wallet: { SOFT: 100, PREMIUM: 0 }, coinsPerHour: 0, generationProgressMinutes: 0, generationIntervalMinutes: 60,
      },
    }).as("tank");
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
    cy.contains("button", "Coletar filhote").should("not.be.disabled");

    cy.intercept("POST", "/api/breeding/collect", {
      statusCode: 200, body: { child, parentADied: true, parentBDied: false },
    }).as("collect");
    cy.intercept("GET", "/api/breeding", { body: { active: false, slot: null } }); // pós-coleta

    cy.contains("button", "Coletar filhote").click();
    cy.wait("@collect");

    cy.contains("Uma despedida").should("be.visible");
    cy.contains("Um dos pais não resistiu à gestação").should("be.visible");
    // O retrato do pai A (score 6.2, banda Incomum) aparece — não só a contagem genérica.
    // Visível mesmo com o filhote (score 13.0, Raro) ainda em revelação suspense —
    // pais/despedida não fazem parte do "mistério" (CLAUDE.md 8.8).
    cy.get(".farewell-fish").should("have.length", 1);
    cy.get(".farewell-fish").contains("6.2");

    // Filhote é Raro (13.0 ≥ 12.04) — revelação clique-a-clique (CollectCelebration.jsx)
    // esconde "Maravilha!" até tocar o peixe as 4 vezes (corpo/cauda/dorsal/peitoral).
    cy.get(".celebrate-fish").click().click().click().click();

    cy.contains("button", "Maravilha!").click();
    cy.contains("Uma despedida").should("not.exist");
  });
});
