// E2E do modal em mobile (11/08/2026) — screenshot real de celular mostrava o modal de
// detalhe do peixe "gigante e incompleto": o conteúdo passava do viewport, o jogador
// precisava rolar pra ver "Atributos"/"Por que é raro", e nesse ponto o botão de fechar (×)
// já tinha rolado pra fora da tela junto — virava uma "página" sem fim perceptível, não um
// modal contido. Fix: `.modal-close` agora fica fora da área que rola (`.modal-body`),
// então continua alcançável em qualquer ponto do scroll. `cy.viewport` com altura BAIXA
// força o overflow de propósito, mesmo num modal com pouco conteúdo.

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
    cy.contains("button.detail-section-head", "Peixes no tanque").click();
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
    cy.contains("button.detail-section-head", "Peixes no tanque").click();
    cy.get(".fish-row").first().click();
    cy.get(".modal-close").should("be.visible").click();
    cy.get(".modal").should("not.exist");
  });
});

// Bug real relatado pelo dono do site (12/08/2026, celular de verdade): o modal de
// detalhe do peixe não ficava centralizado. Duas causas raiz distintas, achadas via
// instrumentação em Cypress (única forma confiável de medir layout mobile neste
// projeto — ver CLAUDE.md §13):
//
// 1) `.tank-layout::before` (vinheta do modo cinema) tinha `inset:-20px` nos 4 lados —
//    em qualquer viewport onde `.content{padding}` (16px no mobile) é menor que os
//    20px de bleed, isso criava overflow horizontal REAL da página inteira (4px em
//    390px de largura, confirmado desligando o pseudo-elemento e comparando
//    `document.documentElement.scrollWidth` antes/depois). Os testes de overflow já
//    existentes (`mobile-header.cy.js`) não pegavam isso porque comparavam contra
//    `innerWidth` (que já inclui a folga da barra de rolagem vertical) em vez de
//    `clientWidth` — 4px reais ficavam mascarados. Fix: bleed só vertical
//    (`inset:-20px 0`), mais `overflow-x:hidden` em html/body como rede de segurança
//    geral (não a correção em si, só evita que um futuro elemento decorativo cause a
//    mesma classe de bug de novo).
// 2) Bug SEPARADO, mais grave: `.modal-backdrop{display:grid; place-items:center}`
//    centraliza o ITEM dentro da sua área de grade, mas não centraliza a TRILHA em
//    si — como não há `grid-template-columns` explícito, a trilha implícita é
//    dimensionada pelo conteúdo do `.modal` (`width:min(560px,96vw)`), não pelo
//    espaço disponível. Em QUALQUER viewport abaixo de ~1000px, `96vw` já excede
//    `100vw − 40px` (padding do backdrop), a trilha estoura o container, e sem
//    `place-content` o excesso vaza só pra um lado (default `justify-content:normal`
//    ≈ `start`) — o modal renderiza deslocado, não centralizado. Esse era o bug
//    principal por trás do relato: reproduzido de forma determinística em Cypress
//    puro (headless Chrome, sem device toolbar/emulação), com desvio constante de
//    ~12-14px em qualquer largura testada — nada a ver com quirks de Safari mobile.
//    Fix: `place-content:center` no backdrop centraliza a trilha (mesmo maior que o
//    container), distribuindo o excesso igualmente pros dois lados.
describe("Modal de detalhe do peixe fica centralizado (12/08/2026)", () => {
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

  [[320, 568], [375, 667], [390, 844]].forEach(([w, h]) => {
    it(`modal fica centralizado horizontalmente em ${w}x${h}`, () => {
      cy.viewport(w, h);
      login();
      cy.contains("button.detail-section-head", "Peixes no tanque").click();
    cy.get(".fish-row").first().click();
      cy.get(".modal").should("be.visible").should(($modal) => {
        const rect = $modal[0].getBoundingClientRect();
        const expectedLeft = (w - rect.width) / 2;
        // Tolerância de 2px por sub-pixel/scrollbar — antes do fix o desvio
        // observado era de ~12-14px, bem acima dessa margem.
        expect(Math.abs(rect.left - expectedLeft)).to.be.lessThan(2);
      });
    });

    it(`sem overflow horizontal de página no Tanque com peixe presente em ${w}x${h}`, () => {
      cy.viewport(w, h);
      login();
      cy.document().should((doc) => {
        // +1px de folga por arredondamento sub-pixel.
        expect(doc.documentElement.scrollWidth).to.be.lte(doc.documentElement.clientWidth + 1);
      });
    });
  });
});
