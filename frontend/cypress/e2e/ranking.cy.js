// E2E do Ranking (CLAUDE.md 8.16): troca de métrica (raridade/renda), o ícone certo
// por métrica (🏆 pra raridade, moeda pra renda — bug corrigido nesta mesma leva de
// testes, RankingView.jsx mostrava moeda pras duas métricas) e visitar outro jogador.

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

const rarityBoard = {
  metric: "rarity", page: 1, pageSize: 50, totalCount: 2,
  entries: [
    { rank: 1, username: "top1", value: 120.5, isSelf: false, level: 3, avatar: null },
    { rank: 2, username: "jogador1", value: 88.2, isSelf: true, level: 2, avatar: null },
  ],
  selfRank: 2, selfValue: 88.2,
};

function spectatorCreature(id, seed, rarityScore) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    traits: fakeTraits(seed), breedingSource: null,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0,
  };
}

const noBreeding = { active: false, parentA: null, parentB: null, readyAt: null, isReady: false };

const incomeBoard = {
  metric: "income", page: 1, pageSize: 50, totalCount: 2,
  entries: [
    { rank: 1, username: "top1", value: 340.1, isSelf: false, level: 3, avatar: null },
    { rank: 2, username: "jogador1", value: 210.4, isSelf: true, level: 2, avatar: null },
  ],
  selfRank: 2, selfValue: 210.4,
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
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
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
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    cy.intercept("GET", "/api/leaderboard/income*", { body: incomeBoard }).as("income");
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
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1, creatures: [], breeding: noBreeding,
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
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1, creatures: [], breeding: noBreeding,
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
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1,
        creatures: [spectatorCreature(501, 4001, 6.06), spectatorCreature(502, 4002, 9.2)],
        breeding: noBreeding,
      },
    }).as("visit");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visit");

    cy.contains(".eyebrow", "Peixes no tanque").should("be.visible");
    cy.get(".fish-row").should("not.exist"); // minimizada por padrão

    cy.get(".collapse-btn").click();
    cy.get(".fish-row").should("have.length", 2);
  });

  it("mostra a perda por água suja ao visitar um aquário com água abaixo do patamar (12/08/2026)", () => {
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 40,
        rarityTotal: 120.5, coinsPerHour: 8.2, // potencial (água cheia) do score 9.2 é ~13.3/h — 40% de água reduz bem abaixo disso
        creatures: [spectatorCreature(502, 4002, 9.2)],
        breeding: noBreeding,
      },
    }).as("visitSujo");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visitSujo");

    cy.get(".water-loss").should("be.visible").contains("/h");
  });

  it("não mostra perda por água suja quando a água está limpa", () => {
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 100,
        rarityTotal: 120.5, coinsPerHour: 13.33, // água cheia == potencial calculado client-side
        creatures: [spectatorCreature(502, 4002, 9.2)],
        breeding: noBreeding,
      },
    }).as("visitLimpo");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visitLimpo");

    cy.get(".water-loss").should("not.exist");
  });

  it("visitar mostra 'Ninho vazio' quando o jogador não tem gestação em andamento", () => {
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1, creatures: [], breeding: noBreeding,
      },
    }).as("visit");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visit");

    cy.contains(".eyebrow", "Ninho").should("be.visible");
    cy.contains("Ninho vazio no momento.");
  });

  it("visitar mostra os pais em gestação no Ninho, sem informação financeira (14/08/2026)", () => {
    cy.intercept("GET", "/api/leaderboard/rarity*", { body: rarityBoard }).as("rarity");
    login();
    cy.wait("@rarity");

    const readyAt = new Date(Date.now() + 3 * 60 * 60 * 1000).toISOString();
    cy.intercept("GET", "/api/leaderboard/visit/top1", {
      body: {
        username: "top1", capacityBandName: "Aquário Grande", maintenanceLevel: 80,
        rarityTotal: 120.5, coinsPerHour: 340.1, creatures: [],
        breeding: {
          active: true,
          parentA: spectatorCreature(601, 5001, 7.1),
          parentB: spectatorCreature(602, 5002, 8.4),
          readyAt, isReady: false,
        },
      },
    }).as("visitNinho");

    cy.contains(".leaderboard-row", "top1").contains("button", "Visitar").click();
    cy.wait("@visitNinho");

    cy.contains(".eyebrow", "Ninho").should("be.visible");
    cy.contains(".hint", "Ninho vazio no momento.").should("not.exist");
    cy.contains("7.1");
    cy.contains("8.4");
    // Sem custo/risco/seguro expostos ao espectador — a seção do Ninho não menciona
    // premium/soft nem "risco", diferente da tela do próprio dono (BreedingView).
    // A chip 💎 do topbar (saldo do PRÓPRIO visitante) continua existindo à parte —
    // por isso o escopo é só dentro da seção do Ninho, não a página inteira.
    cy.contains(".eyebrow", "Ninho").parents("section.cinema-dim").within(() => {
      cy.contains("💎").should("not.exist");
      cy.contains("risco", { matchCase: false }).should("not.exist");
    });
  });
});
