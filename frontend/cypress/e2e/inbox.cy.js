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
    // O modal ganhou textos explicativos (15/08/2026) — "Dar moedas" agora fica fora da área
    // visível sem rolar; scrollIntoView antes de checar (a interação em si já rola sozinha).
    cy.contains("h4", "🎁 Dar moedas").scrollIntoView().should("be.visible");

    // select[0]="Moeda" e [1]="Modo" são da seção de ajustar carteira (acima); [2]="Público".
    cy.get('select').eq(2).select("Selected");
    cy.get('input[placeholder="ex: fulano, beltrano"]').type("fulano, beltrano");
    cy.contains("label", "Título").next("input").type("Manutenção");
    cy.contains("label", "Mensagem").next("textarea").type("Vai ficar fora do ar às 22h.");
    cy.contains("button", "Enviar").click();

    cy.wait("@sendMessage").its("request.body").should("deep.equal", {
      title: "Manutenção", body: "Vai ficar fora do ar às 22h.", audience: "Selected",
      usernames: ["fulano", "beltrano"], rewardCurrencyCode: null, rewardCurrencyAmount: null,
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
    // O modal ganhou textos explicativos (15/08/2026) — "Dar moedas" agora fica fora da área
    // visível sem rolar; scrollIntoView antes de checar (a interação em si já rola sozinha).
    cy.contains("h4", "🎁 Dar moedas").scrollIntoView().should("be.visible");

    // Público já nasce em "Todos os jogadores" — só preenche moeda/quantia, sem mexer em
    // título/mensagem (ambos ficam em branco).
    cy.get('select').eq(2).should("have.value", "All");
    cy.get('select').eq(3).select("SOFT");
    cy.get('input[placeholder="quantia"]').type("500");
    cy.contains("button", "Enviar").click();

    cy.wait("@sendMessage").its("request.body").should("deep.equal", {
      title: "🎁 Presente do admin", body: "Você recebeu uma recompensa da equipe do jogo!",
      audience: "All", usernames: null, rewardCurrencyCode: "SOFT", rewardCurrencyAmount: 500,
    });
  });
});
