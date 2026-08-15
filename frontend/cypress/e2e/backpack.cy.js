// E2E da Mochila: mochila vazia, mover pro tanque (deploy), listar no mercado (sell),
// transferir, e os filtros de raridade/cor. API mockada via cy.intercept.

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

// 13/08/2026 — traits congelados no nascimento: a API sempre devolve `traits` pronto
// (`creature.traits`), nunca mais um seed pra o cliente derivar — os mocks espelham isso.
function creature(id, seed, rarityScore, isBred = false, isNew = false) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    traits: fakeTraits(seed), breedingSource: null,
    createdAt: "2026-01-01T00:00:00Z", isBred, parentASeed: null, parentBSeed: null, breedCount: 0, isNew,
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
  cy.contains("button", "Mochila").click();
}

describe("Mochila", () => {
  it("mochila vazia mostra a dica, sem cards", () => {
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpack");
    login();
    cy.wait("@backpack");
    cy.contains("Mochila vazia");
    cy.get(".card").should("not.exist");
  });

  it("mover peixe da mochila pro tanque (deploy)", () => {
    const fish = creature(301, 1111, 5.5);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [fish] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.intercept("POST", "/api/game/creatures/301/deploy", { statusCode: 200, body: {} }).as("deploy");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpackAfter");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tankAfter");

    cy.contains("button", "Pro tanque").click();
    cy.wait("@deploy");
    cy.wait("@backpackAfter");
    cy.contains("Peixe de volta ao tanque!");
    cy.contains("Mochila vazia");
  });

  it("listar no mercado via prompt de preço", () => {
    const fish = creature(302, 2222, 6.2);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [fish] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.contains("button", "Vender").click();
    cy.contains("Vender no mercado").should("be.visible");
    cy.get('input[type="number"]').clear().type("75");

    cy.intercept("POST", "/api/market/listings", { statusCode: 200, body: { id: 900 } }).as("createListing");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpackAfter");

    cy.contains("button", "Listar peixe").click();
    cy.wait("@createListing").its("request.body").should("deep.equal", { creatureInstanceId: 302, priceSoft: 75 });
    cy.contains("Listado no mercado.");
  });

  it("transferir pra outro jogador via prompt de username", () => {
    const fish = creature(303, 3333, 4.1);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [fish] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.contains("button", "Transferir").click();
    cy.contains("Transferir peixe").should("be.visible");
    cy.get('input[placeholder="username"]').type("amigo123");

    cy.intercept("POST", "/api/game/creatures/303/transfer", { statusCode: 200, body: {} }).as("transfer");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpackAfter");

    cy.get(".modal").contains("button", "Transferir").click();
    cy.wait("@transfer").its("request.body").should("deep.equal", { toUsername: "amigo123" });
    cy.contains("Transferido para amigo123.");
  });

  it("transferência recusada pelo servidor mostra o erro sem fechar o modal", () => {
    const fish = creature(304, 4444, 4.1);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [fish] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.contains("button", "Transferir").click();
    cy.get('input[placeholder="username"]').type("naoexiste");
    cy.intercept("POST", "/api/game/creatures/304/transfer", {
      statusCode: 404, body: { error: "Usuário não encontrado" },
    }).as("transferFail");
    cy.get(".modal").contains("button", "Transferir").click();
    cy.wait("@transferFail");

    cy.contains("Usuário não encontrado");
    cy.contains("Transferir peixe").should("be.visible"); // modal continua aberto
  });

  it("filtro por faixa de raridade esconde peixes de outras faixas", () => {
    const common = creature(305, 5555, 3.0);   // Comum
    const legendary = creature(306, 6666, 20.0); // Lendário (corte atual: score >= 16.60)
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [common, legendary] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.get(".card").should("have.length", 2);
    cy.contains(".filter-chip", "Lendário").click();
    cy.get(".card").should("have.length", 1);
    cy.contains(".card", "Lendário");
  });

  it("filtros avançados por parte (cor/padrão independentes) combinam corretamente", () => {
    // Dois peixes: um com cauda Azul + padrão Estria, outro com cauda Laranja sem padrão —
    // filtrar por "cauda Azul" deve esconder o segundo; "cauda Azul + Estria" mantém só o primeiro.
    const bluePart = { color: "Blue", pattern: "Stripe", patternColor: "Black", patternSize: 50, patternOpacity: 50, mix: null };
    const orangePart = { color: "Orange", pattern: "None", patternColor: null, patternSize: null, patternOpacity: null, mix: null };
    const withPattern = {
      id: 320, speciesId: 1, seed: "1234", traitConfigVersion: 1, rarityScore: 5.0,
      traits: { shimmerTier: "None", shimmerColor: null, shimmerOpacity: 0, tail: bluePart, dorsal: orangePart, pectoral: orangePart, movement: { tailSpeed: 50, tailAmplitude: 0.4, finSpeed: 50, finAmplitude: 0.3 } },
      breedingSource: null, createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0, isNew: false,
    };
    const plain = creature(321, 5678, 5.0);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [withPattern, plain] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.get(".card").should("have.length", 2);
    cy.contains("button.detail-section-head", "Filtros avançados").click();

    // Filtro de cor da CAUDA por Azul — só o peixe com cauda Blue passa.
    cy.get(".appearance-filter-part").first().within(() => {
      cy.get(".color-chip").each(($el) => {
        if ($el.attr("title") === "Azul") cy.wrap($el).click();
      });
    });
    cy.get(".card").should("have.length", 1);

    // Adiciona filtro de padrão da CAUDA (Estria) — continua 1, já bate os dois critérios.
    cy.get(".appearance-filter-part").first().within(() => {
      cy.contains(".filter-chip", "Estria").click();
    });
    cy.get(".card").should("have.length", 1);

    // Badge de contagem de filtros ativos aparece no cabeçalho da seção.
    cy.contains("button.detail-section-head", "Filtros avançados (2)");
  });

  it("filtro 'só filhotes' esconde não-filhotes", () => {
    const normal = creature(307, 7777, 5.0, false);
    const bred = creature(308, 8888, 5.0, true);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [normal, bred] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.get(".card").should("have.length", 2);
    cy.get('input[type="checkbox"]').check();
    cy.get(".card").should("have.length", 1);
    cy.contains(".card", "Filhote");
  });

  it("venda em massa ao NPC mostra progresso ao vivo no modal", () => {
    const a = creature(330, 1111, 3.0);
    const b = creature(331, 2222, 3.5);
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [a, b] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.contains("button", "Selecionar peixes").click();
    cy.contains("button", "Selecionar todos").click();
    cy.contains("button", "Vender ao NPC").click();
    cy.contains("Essa ação não pode ser desfeita.");

    // Ordenação padrão é raridade desc: 331 (3.5) processa antes de 330 (3.0) — atraso na
    // primeira venda processada dá tempo de ver "Vendendo 0 de 2...".
    cy.intercept("POST", "/api/game/creatures/331/sell-vendor", (req) => {
      req.reply({ delay: 300, statusCode: 200, body: { price: 10 } });
    }).as("sell2");
    cy.intercept("POST", "/api/game/creatures/330/sell-vendor", { statusCode: 200, body: { price: 9 } }).as("sell1");
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } }).as("backpackAfter");

    cy.get(".modal").contains("button", "Vender agora").click();
    cy.contains("Vendendo 0 de 2...");
    cy.wait(["@sell1", "@sell2"]);
    cy.contains("19 moedas");
  });

  it("peixe novo comum (< 12.04) esconde score/produção, abre revelação instantânea e marca visto", () => {
    const fresh = creature(309, 9999, 6.0, false, true); // < 12.04: sem suspense
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [fresh] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.contains(".card", "??? — clique pra revelar");
    cy.get(".fish-silhouette").should("exist");
    cy.get(".card .badge").should("not.exist"); // RarityBadge escondido
    cy.get(".card .produces").should("not.exist"); // produção escondida
    cy.contains(".card", "Pro tanque").should("not.exist"); // ações escondidas até revelar

    cy.intercept("POST", "/api/game/creatures/309/mark-seen", { statusCode: 200, body: {} }).as("markSeen");
    cy.get(".fish-stage.as-button").click();
    cy.get(".modal.celebrate").should("be.visible");
    cy.wait("@markSeen"); // < 12.04: revela e marca visto assim que o modal abre, sem clique extra

    cy.get(".modal-close").click();
    cy.contains(".card", "??? — clique pra revelar").should("not.exist");
    cy.get(".fish-silhouette").should("not.exist");
    cy.contains(".card", "Pro tanque"); // ações voltam a aparecer
  });

  it("peixe novo Raro+ (>= 12.04) revela clique-a-clique antes de marcar visto", () => {
    const rare = creature(310, 8888, 13.0, false, true); // >= 12.04: suspense
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [rare] } }).as("backpack");
    login();
    cy.wait("@backpack");

    cy.intercept("POST", "/api/game/creatures/310/mark-seen", { statusCode: 200, body: {} }).as("markSeen");
    cy.get(".fish-stage.as-button").click();
    cy.get(".modal.celebrate").should("be.visible");
    cy.contains("Abrindo peixe raro"); // ainda não revelado — sem mark-seen

    // 4 cliques no peixe (corpo/brilho, cauda, dorsal, peitoral) — mesma revelação da coleta.
    for (let i = 0; i < 4; i++) cy.get(".celebrate-fish").click();
    cy.wait("@markSeen"); // só marca visto depois da revelação completa

    cy.get(".modal-close").click();
    cy.contains(".card", "??? — clique pra revelar").should("not.exist");
  });

  it("peixe novo (não revelado) sempre aparece primeiro na lista, não importa a ordenação", () => {
    // Score baixo mas isNew=true — se a ordenação por raridade fosse aplicada sem prioridade
    // pro "novo", esse peixe ficaria por último, escondido atrás de "???".
    const veteran = creature(401, 1111, 18.9, false, false); // Lendário, já visto
    const fresh = creature(402, 2222, 3.0, false, true); // Comum, ainda não revelado
    cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [veteran, fresh] } }).as("backpack");
    login();
    cy.wait("@backpack");

    // Ordenação padrão é "Raridade (maior primeiro)" — sem a prioridade, o Lendário viria antes.
    cy.get(".card").first().should("contain.text", "??? — clique pra revelar");
  });
});
