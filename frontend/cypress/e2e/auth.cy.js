// E2E de fumaça do fluxo de autenticação. Todo o backend é mockado via cy.intercept —
// não precisa da API/Postgres rodando, então roda em qualquer máquina/CI só com o front
// buildado (`npm run build && npm run preview`).

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

describe("Autenticação", () => {
  it("login com sucesso leva ao tanque vazio", () => {
    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
    cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } }).as("heartbeat");
    cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
    cy.contains("button", "Mergulhar").click();

    cy.wait("@login");
    cy.wait("@tank");

    cy.contains("Tanque"); // aba de navegação do jogo
    cy.contains("100 "); // saldo soft da carteira (chip do topbar)
    cy.contains("Seu aquário está esperando"); // estado vazio do tanque
  });

  it("credenciais inválidas mostram a mensagem de erro do servidor, sem entrar no jogo", () => {
    cy.intercept("POST", "/api/auth/login", {
      statusCode: 401, body: { error: "Usuário ou senha inválidos" },
    }).as("loginFail");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-errada-999");
    cy.contains("button", "Mergulhar").click();

    cy.wait("@loginFail");
    cy.contains("Usuário ou senha inválidos");
    cy.contains("Tanque").should("not.exist"); // não navegou pro jogo
  });

  it("alterna pro modo de criar conta e mostra o campo de email", () => {
    cy.visit("/");
    cy.get('input[type="email"]').should("not.exist");
    cy.contains("button", "Criar conta").click();
    cy.get('input[type="email"]').should("be.visible");
    cy.contains("button", "Criar meu aquário");
  });
});
