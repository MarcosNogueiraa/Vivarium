// E2E do "Editar perfil" (14/08/2026): trocar email e trocar senha a partir do menu de
// conta (ícone 👤), cada ação exigindo a senha atual.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

function login() {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.intercept("GET", "/api/game/tank", { fixture: "tank-empty.json" }).as("tank");
  cy.intercept("GET", "/api/auth/me", { body: { userId: 1, username: "jogador1", email: "jogador1@teste.com" } }).as("me");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
}

function openProfile() {
  cy.get(".account-btn").click();
  cy.wait("@me");
  cy.contains("button", "✏️ Editar perfil").click();
}

// Reativado 18/08/2026 — ver forgot-password.cy.js pra contexto completo (mesmo hotfix,
// mesmo motivo do reativamento).
describe("Editar perfil", () => {
  it("abre com o email atual pré-preenchido", () => {
    login();
    openProfile();

    cy.contains(".eyebrow", "Editar perfil").should("be.visible");
    cy.get('input[type="email"]').should("have.value", "jogador1@teste.com");
  });

  it("troca o email com sucesso", () => {
    cy.intercept("PUT", "/api/account/email", { statusCode: 200, body: { userId: 1, username: "jogador1", email: "novo@teste.com" } }).as("updateEmail");
    login();
    openProfile();

    cy.get('input[type="email"]').clear().type("novo@teste.com");
    cy.get('input[placeholder="Sua senha atual"]').type("senha-forte-123");
    cy.contains("button", "Salvar email").click();

    cy.wait("@updateEmail").its("request.body").should("deep.equal", { newEmail: "novo@teste.com", currentPassword: "senha-forte-123" });
    cy.contains("Email atualizado.");
  });

  it("erro do servidor (senha atual errada) aparece inline no formulário de email", () => {
    cy.intercept("PUT", "/api/account/email", { statusCode: 400, body: { error: "Senha atual incorreta" } }).as("updateEmailFail");
    login();
    openProfile();

    cy.get('input[type="email"]').clear().type("novo@teste.com");
    cy.get('input[placeholder="Sua senha atual"]').type("senha-errada");
    cy.contains("button", "Salvar email").click();

    cy.wait("@updateEmailFail");
    cy.contains("Senha atual incorreta");
  });

  it("troca a senha com sucesso", () => {
    cy.intercept("PUT", "/api/account/password", { statusCode: 200, body: {} }).as("updatePassword");
    login();
    openProfile();

    cy.get(".detail-section").within(() => {
      cy.get('input[autocomplete="current-password"]').type("senha-forte-123");
      cy.get('input[autocomplete="new-password"]').eq(0).type("senha-nova-456");
      cy.get('input[autocomplete="new-password"]').eq(1).type("senha-nova-456");
    });
    cy.contains("button", "Salvar senha").click();

    cy.wait("@updatePassword").its("request.body").should("deep.equal", { currentPassword: "senha-forte-123", newPassword: "senha-nova-456" });
    cy.contains("Senha atualizada.");
  });

  it("nova senha e confirmação diferentes mostram erro sem chamar a API", () => {
    let called = false;
    cy.intercept("PUT", "/api/account/password", () => { called = true; });
    login();
    openProfile();

    cy.get(".detail-section").within(() => {
      cy.get('input[autocomplete="current-password"]').type("senha-forte-123");
      cy.get('input[autocomplete="new-password"]').eq(0).type("senha-nova-456");
      cy.get('input[autocomplete="new-password"]').eq(1).type("senha-diferente-999");
    });
    cy.contains("button", "Salvar senha").click();

    cy.contains("As duas senhas novas não coincidem.");
    cy.then(() => expect(called).to.be.false);
  });
});
