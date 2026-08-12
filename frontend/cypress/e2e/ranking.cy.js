// E2E do Ranking (CLAUDE.md 8.16): troca de métrica (raridade/renda), o ícone certo
// por métrica (🏆 pra raridade, moeda pra renda — bug corrigido nesta mesma leva de
// testes, RankingView.jsx mostrava moeda pras duas métricas) e visitar outro jogador.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const rarityBoard = {
  entries: [
    { rank: 1, username: "top1", value: 120.5, isSelf: false },
    { rank: 2, username: "jogador1", value: 88.2, isSelf: true },
  ],
  selfOutsideTop: null,
};

function spectatorCreature(id, seed, rarityScore) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0,
  };
}

const incomeBoard = {
  entries: [
    { rank: 1, username: "top1", value: 340.1, isSelf: false },
    { rank: 2, username: "jogador1", value: 210.4, isSelf: true },
  ],
  selfOutsideTop: null,
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
  cy.contains("button", "🏆 Ranking").click();
}

describe("Ranking", () => {
  it("métrica de raridade mostra o troféu, não o ícone de moeda", () => {
    cy.intercept("GET", "/api/leaderboard/rarity", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.contains(".leaderboard-row", "top1").within(() => {
      cy.contains("🏆");
      cy.get(".coin").should("not.exist");
      cy.contains("120.5");
    });
    cy.contains(".leaderboard-row.is-self", "jogador1").should("exist");
  });

  it("métrica de renda mostra o ícone de moeda e o sufixo /h", () => {
    cy.intercept("GET", "/api/leaderboard/rarity", { body: rarityBoard }).as("rarity");
    cy.intercept("GET", "/api/leaderboard/income", { body: incomeBoard }).as("income");
    login();
    cy.wait("@rarity");

    cy.contains("button", "Renda por hora").click();
    cy.wait("@income");

    cy.contains(".leaderboard-row", "top1").within(() => {
      cy.get(".coin").should("exist");
      cy.contains("340.1");
      cy.contains("/h");
    });
  });

  it("visitar outro jogador mostra o aquário dele em modo espectador e volta pro ranking", () => {
    cy.intercept("GET", "/api/leaderboard/rarity", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1, creatures: [],
      },
    }).as("visit");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visit");

    cy.contains("Aquário de top1");
    cy.contains("só visualização");
    cy.contains("120.5");
    cy.contains("button", "← Voltar ao ranking").click();
    cy.contains(".leaderboard-row", "top1"); // de volta na lista
  });

  it("clicar de novo na aba Ranking enquanto visita volta pra lista sem precisar de Voltar (12/08/2026)", () => {
    cy.intercept("GET", "/api/leaderboard/rarity", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1, creatures: [],
      },
    }).as("visit");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visit");
    cy.contains("Aquário de top1");

    cy.contains("button", "🏆 Ranking").click();
    cy.contains("Aquário de top1").should("not.exist");
    cy.contains(".leaderboard-row", "top1");
  });

  it("lista 'Peixes no tanque' do espectador nasce minimizada (12/08/2026)", () => {
    cy.intercept("GET", "/api/leaderboard/rarity", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1,
        creatures: [spectatorCreature(501, 4001, 6.06), spectatorCreature(502, 4002, 9.2)],
      },
    }).as("visit");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visit");

    cy.contains(".eyebrow", "Peixes no tanque").should("be.visible");
    cy.get(".fish-row").should("not.exist"); // minimizada por padrão

    cy.get(".collapse-btn").click();
    cy.get(".fish-row").should("have.length", 2);
  });
});
