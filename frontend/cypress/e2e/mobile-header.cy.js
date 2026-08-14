// E2E do cabeçalho mobile (11/08/2026) — revisão de UX mobile pedida pelo dono do site
// depois de screenshots reais de celular mostrando o topbar tomando quase a tela toda
// antes de qualquer conteúdo do aquário aparecer (marca / carteira+premium / admin+conta
// / nav em DUAS linhas). `cy.viewport` é o único jeito confiável de emular uma tela
// estreita neste projeto (device toolbar do Chrome DevTools não funciona no ambiente de
// automação — ver CLAUDE.md §13, "Responsividade mobile").

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const PHONE = [390, 844]; // iPhone 12-ish

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");
  cy.intercept("GET", "/api/game/daily-reward", { statusCode: 200, body: { canClaim: false, amount: 25, nextAvailableAtUtc: null } });

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
}

function assertNoHorizontalOverflow() {
  cy.window().then((win) => {
    // 1px de folga por causa de arredondamento sub-pixel entre scrollWidth/innerWidth.
    expect(win.document.documentElement.scrollWidth).to.be.lte(win.innerWidth + 1);
  });
}

describe("Cabeçalho mobile", () => {
  beforeEach(() => cy.viewport(...PHONE));

  it("marca e conta ficam na mesma linha, acima das abas", () => {
    login();
    cy.get(".brand").then(($brand) => {
      cy.get(".account-menu").then(($account) => {
        // Tolerância generosa: os dois elementos têm alturas/baselines diferentes
        // (texto vs. botão redondo de 38px) — o que importa é que estão na MESMA
        // linha visual, não pixel-perfeitamente alinhados no topo.
        const brandTop = $brand[0].getBoundingClientRect().top;
        const accountTop = $account[0].getBoundingClientRect().top;
        expect(Math.abs(brandTop - accountTop)).to.be.lessThan(20);
      });
    });
  });

  it("as 6 abas ficam numa única linha rolável, não quebradas em duas", () => {
    login();
    cy.get(".nav-pills button").then(($buttons) => {
      // Tanque/Mochila/Mercado/Loja/Ninho/Ranking — Caixa de Entrada virou ícone solto
      // (14/08/2026), não é mais uma aba do nav.
      expect($buttons.length).to.equal(6);
      const tops = [...$buttons].map((b) => Math.round(b.getBoundingClientRect().top));
      // todas as abas no mesmo "y" — se tivesse quebrado em 2 linhas, haveria 2 valores de topo.
      const distinctTops = new Set(tops);
      expect(distinctTops.size).to.equal(1);
    });
  });

  it("trocar pra uma aba fora da área visível funciona e ela rola até a vista", () => {
    login();
    cy.contains(".nav-pills button", "🏆 Ranking").as("rankingTab");
    cy.intercept("GET", "/api/leaderboard/rarity", { body: { entries: [], selfOutsideTop: null } });
    cy.get("@rankingTab").click({ force: true });
    cy.get("@rankingTab").should("have.class", "active");
    // `.should(callback)` re-tenta até passar (ou expirar) — necessário porque o
    // scrollIntoView roda com `behavior:"smooth"` (animado, assíncrono); um
    // `.then()` simples capturaria a posição em pleno meio da animação.
    cy.get("@rankingTab").should(($btn) => {
      const rect = $btn[0].getBoundingClientRect();
      // depois do scrollIntoView, o botão ativo precisa estar dentro da faixa visível do nav.
      expect(rect.left).to.be.gte(0);
      expect(rect.right).to.be.lte(PHONE[0] + 1);
    });
  });

  it("o cabeçalho fica compacto (3 linhas), não engole a tela toda", () => {
    login();
    // Mede o cabeçalho isolado (sem o banner de "bem-vindo" que só aparece pra
    // conta nova, ruído que não tem nada a ver com o cabeçalho em si). Antes da
    // revisão, o topbar sozinho passava de ~350-400px em 390px de largura
    // (marca / carteira+premium / admin+conta / nav em 2 linhas); com as 3
    // linhas compactas (marca+conta / abas roláveis / carteira+premium) fica
    // bem abaixo disso.
    cy.get(".topbar").then(($topbar) => {
      expect($topbar[0].getBoundingClientRect().height).to.be.lessThan(200);
    });
  });

  ["Tanque", "Mochila", "Mercado", "Loja", "Ninho", "🏆 Ranking"].forEach((label) => {
    it(`aba "${label}": sem overflow horizontal da página`, () => {
      cy.intercept("GET", "/api/game/backpack", { body: { capacity: 50, creatures: [] } });
      cy.intercept("GET", "/api/market/listings", { body: [] });
      cy.intercept("GET", "/api/items/", { body: [] });
      cy.intercept("GET", "/api/breeding", { body: { active: false } });
      cy.intercept("GET", "/api/leaderboard/rarity", { body: { entries: [], selfOutsideTop: null } });
      login();
      cy.contains(".nav-pills button", label).click({ force: true });
      assertNoHorizontalOverflow();
    });
  });
});
