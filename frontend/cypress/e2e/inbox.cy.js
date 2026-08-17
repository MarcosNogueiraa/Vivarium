// E2E da Caixa de Entrada (CLAUDE.md §8.23/§8.24): badge de pendentes, listagem (mensagem
// admin + entrega de peixe), resgate individual/em massa, ler tudo, apagar resgatadas, e o
// formulário de "mandar mensagem" no painel de admin.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

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

function inboxCreature(id, seed, rarityScore) {
  return {
    id, speciesId: 1, seed: String(seed), traitConfigVersion: 1, rarityScore,
    traits: fakeTraits(seed), breedingSource: null,
    createdAt: "2026-01-01T00:00:00Z", isBred: false, parentASeed: null, parentBSeed: null, breedCount: 0, isNew: false,
  };
}

function login({ isAdmin = false, inboxEntries = [] } = {}) {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.fixture("tank-empty.json").then((base) => {
    cy.intercept("GET", "/api/game/tank", { body: { ...base, isAdmin } }).as("tank");
  });
  cy.intercept("GET", "/api/inbox/", { body: { entries: inboxEntries } }).as("inbox");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.wait("@inbox");
}

describe("Caixa de Entrada", () => {
  it("sem entradas pendentes, o ícone não mostra badge", () => {
    login({ inboxEntries: [] });
    cy.get(".inbox-btn").find(".inbox-badge").should("not.exist");
  });

  it("com entradas pendentes, o badge mostra a contagem", () => {
    login({
      inboxEntries: [
        { id: 1, kind: "AdminMessage", title: "Oi", body: "Mensagem", senderUsername: null, creature: null, rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
        { id: 2, kind: "MarketPurchase", title: null, body: null, senderUsername: "vendedor1", creature: inboxCreature(501, 4001, 6.06), rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
      ],
    });
    cy.get(".inbox-btn").find(".inbox-badge").should("have.text", "2");
  });

  // 14/08/2026, pedido do usuário: badge trava em "9+" a partir de 10 pendentes, pra nunca
  // estourar a bolinha.
  it("com 10+ entradas pendentes, o badge mostra 9+", () => {
    const entries = Array.from({ length: 12 }, (_, i) => ({
      id: i + 1, kind: "AdminMessage", title: `Msg ${i + 1}`, body: "x", senderUsername: null,
      creature: null, rewardCurrencyCode: null, rewardCurrencyAmount: null,
      readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z",
    }));
    login({ inboxEntries: entries });
    cy.get(".inbox-btn").find(".inbox-badge").should("have.text", "9+");
  });

  it("abrir o ícone lista as entradas — mensagem e entrega de peixe", () => {
    login({
      inboxEntries: [
        { id: 1, kind: "AdminMessage", title: "Aviso importante", body: "Leia isso", senderUsername: null, creature: null, rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
        { id: 2, kind: "DirectTransfer", title: null, body: null, senderUsername: "amigo1", creature: inboxCreature(502, 4002, 9.2), rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
      ],
    });
    cy.get(".inbox-btn").click();

    cy.contains("Aviso importante");
    cy.contains("Leia isso");
    cy.contains("de amigo1");
  });

  it("resgatar uma entrega de peixe chama o claim e atualiza o tanque", () => {
    login({
      inboxEntries: [
        { id: 2, kind: "MarketPurchase", title: null, body: null, senderUsername: "vendedor2", creature: inboxCreature(503, 4003, 5.5), rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
      ],
    });
    cy.intercept("POST", "/api/inbox/2/claim", { statusCode: 200, body: {} }).as("claim");
    cy.intercept("GET", "/api/inbox/", { body: { entries: [] } }).as("inboxAfter");
    cy.fixture("tank-empty.json").then((base) => {
      cy.intercept("GET", "/api/game/tank", { body: base }).as("tankAfter");
    });

    cy.get(".inbox-btn").click();
    cy.contains("button", "Resgatar pro tanque/mochila").click();

    cy.wait("@claim");
    cy.wait("@tankAfter");
  });

  it("mensagem com recompensa mostra o valor e resgatar credita a carteira", () => {
    login({
      inboxEntries: [
        { id: 3, kind: "AdminMessage", title: "Prêmio", body: "Toma!", senderUsername: null, creature: null, rewardCurrencyCode: "SOFT", rewardCurrencyAmount: 50, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
      ],
    });
    cy.intercept("POST", "/api/inbox/3/claim", { statusCode: 200, body: {} }).as("claim");

    cy.get(".inbox-btn").click();
    cy.contains("50 SOFT");
    // Escopado no card — "Resgatar tudo" (barra de ações) também contém a substring "Resgatar".
    cy.get(".card").contains("button", "Resgatar").click();

    cy.wait("@claim");
  });

  it("mensagem com ovo mostra a prévia do tier e chocar abre a celebração", () => {
    login({
      inboxEntries: [
        {
          id: 4, kind: "AdminMessage", title: "🥚 Presente do admin", body: "Toma um ovo!",
          senderUsername: null, creature: null, rewardCurrencyCode: null, rewardCurrencyAmount: null,
          rewardEggKey: "egg_legendary", readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z",
        },
      ],
    });
    cy.intercept("POST", "/api/inbox/4/claim", {
      statusCode: 200,
      body: { creature: inboxCreature(999, 424242, 3) },
    }).as("claim");

    cy.get(".inbox-btn").click();
    cy.contains(".card", "Toma um ovo!").within(() => {
      cy.contains("Ovo Lendário");
      cy.get(".mini-fish-egg--legendary").should("be.visible");
    });
    cy.contains("button", "Chocar ovo").click();
    cy.wait("@claim");

    cy.get(".celebrate-egg--legendary").should("be.visible").click();
    cy.contains("Seu peixe chocou!");
  });

  it("Resgatar tudo / Ler tudo / Apagar mensagens lidas chamam os endpoints certos", () => {
    login({
      inboxEntries: [
        { id: 1, kind: "AdminMessage", title: "Msg 1", body: "x", senderUsername: null, creature: null, rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: null, claimedAt: null, createdAt: "2026-01-01T00:00:00Z" },
        { id: 2, kind: "AdminMessage", title: "Msg 2 (já resgatada)", body: "y", senderUsername: null, creature: null, rewardCurrencyCode: null, rewardCurrencyAmount: null, readAt: "2026-01-01T00:00:00Z", claimedAt: "2026-01-01T00:00:00Z", createdAt: "2026-01-01T00:00:00Z" },
      ],
    });
    cy.intercept("POST", "/api/inbox/claim-all", { statusCode: 200, body: { claimedCount: 1, failedCount: 0 } }).as("claimAll");
    cy.intercept("POST", "/api/inbox/mark-all-read", { statusCode: 200, body: {} }).as("markAllRead");
    cy.intercept("POST", "/api/inbox/clear-claimed", { statusCode: 200, body: {} }).as("clearClaimed");

    cy.get(".inbox-btn").click();

    cy.contains("button", "Resgatar tudo").click();
    cy.wait("@claimAll");

    cy.contains("button", "Ler tudo").click();
    cy.wait("@markAllRead");

    cy.contains("button", "Apagar mensagens lidas").click();
    // A Caixa de Entrada agora já é um modal (14/08/2026) — o ConfirmModal de confirmação abre
    // ANINHADO nele (2 `.modal` no DOM); `.narrow` (só o ConfirmModal usa) desambigua.
    cy.get(".modal.narrow").contains("button", "Apagar").click();
    cy.wait("@clearClaimed");
  });

  it("admin manda mensagem — formulário envia o payload certo", () => {
    login({ isAdmin: true, inboxEntries: [] });
    cy.intercept("POST", "/api/admin/inbox/send", {
      statusCode: 200, body: { recipientCount: 2, notFoundUsernames: [] },
    }).as("sendMessage");

    cy.contains("button", "🛠️ Admin").click();
    // Seções ficam recolhidas por padrão (17/08/2026) — abre "Dar moedas / ovo" antes de mexer
    // nos campos de dentro dela.
    cy.contains("button", "🎁 Dar moedas / ovo").click();
    cy.contains("label", "Público").next("select").select("Selected");
    cy.get('input[placeholder="ex: fulano, beltrano"]').type("fulano, beltrano");
    cy.contains("label", "Título").next("input").type("Manutenção");
    cy.contains("label", "Mensagem").next("textarea").type("Vai ficar fora do ar às 22h.");
    cy.contains("button", "Enviar").click();

    cy.wait("@sendMessage").its("request.body").should("deep.equal", {
      title: "Manutenção", body: "Vai ficar fora do ar às 22h.", audience: "Selected",
      usernames: ["fulano", "beltrano"], rewardCurrencyCode: null, rewardCurrencyAmount: null,
      rewardEggKeys: null,
    });
  });

  // 15/08/2026, pedido do usuário: "Dar moedas" pra todos/lista sem escrever mensagem —
  // título/corpo têm que ir preenchidos com um texto padrão (backend exige os dois).
  it("dar moedas pra todos sem escrever mensagem usa o texto padrão", () => {
    login({ isAdmin: true, inboxEntries: [] });
    cy.intercept("POST", "/api/admin/inbox/send", {
      statusCode: 200, body: { recipientCount: 5, notFoundUsernames: [] },
    }).as("sendMessage");

    cy.contains("button", "🛠️ Admin").click();
    // Seções ficam recolhidas por padrão (17/08/2026) — abre "Dar moedas / ovo" antes de mexer
    // nos campos de dentro dela.
    cy.contains("button", "🎁 Dar moedas / ovo").click();

    // Público já nasce em "Todos os jogadores" — só preenche moeda/quantia, sem mexer em
    // título/mensagem (ambos ficam em branco).
    cy.contains("label", "Público").next("select").should("have.value", "All");
    cy.contains("label", "Moeda").next("select").select("SOFT");
    cy.get('input[placeholder="quantia"]').type("500");
    cy.contains("button", "Enviar").click();

    cy.wait("@sendMessage").its("request.body").should("deep.equal", {
      title: "🎁 Presente do admin", body: "Você recebeu uma recompensa da equipe do jogo!",
      audience: "All", usernames: null, rewardCurrencyCode: "SOFT", rewardCurrencyAmount: 500,
      rewardEggKeys: null,
    });
  });

  it("admin adiciona vários ovos de tiers diferentes ao carrinho antes de enviar", () => {
    login({ isAdmin: true, inboxEntries: [] });
    cy.intercept("POST", "/api/admin/inbox/send", {
      statusCode: 200, body: { recipientCount: 3, notFoundUsernames: [] },
    }).as("sendMessage");

    cy.contains("button", "🛠️ Admin").click();
    cy.contains("button", "🎁 Dar moedas / ovo").click();

    cy.contains("label", "Ovos").next(".card-row").within(() => {
      cy.get("select").select("egg_common");
      cy.contains("button", "+ Adicionar").click();
      cy.get("select").select("egg_common");
      cy.contains("button", "+ Adicionar").click();
      cy.get("select").select("egg_legendary");
      cy.contains("button", "+ Adicionar").click();
    });

    cy.contains("Ovo Comum ×2");
    cy.contains("Ovo Lendário ×1");

    cy.contains("label", "Título").next("input").type("Cesta de ovos");
    cy.contains("label", "Mensagem").next("textarea").type("Presentão!");
    cy.contains("button", "Enviar").click();

    cy.wait("@sendMessage").its("request.body.rewardEggKeys").should("deep.equal", [
      "egg_common", "egg_common", "egg_legendary",
    ]);
  });
});
