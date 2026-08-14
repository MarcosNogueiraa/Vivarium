// E2E do fluxo "esqueci minha senha" (14/08/2026): pedir o link na tela de login e
// redefinir via `?resetToken=...` (sem router — App.jsx checa a URL direto).
//
// .skip (14/08/2026): UI temporariamente desativada em AuthView.jsx/App.jsx — a feature
// ainda não foi implantada no backend (Oracle VM), subir o frontend com esses pontos de
// entrada ativos quebrava "Esqueceu sua senha?" pros jogadores de verdade. Reativar (e tirar
// o .skip) junto com o deploy do backend (AccountEndpoints.cs/PasswordResetService.cs +
// migration AddPasswordResetToken).
describe.skip("Esqueci minha senha", () => {
  it("pedir o link mostra a confirmação genérica, exista ou não a conta", () => {
    cy.intercept("POST", "/api/auth/forgot-password", {
      statusCode: 200, body: { message: "Se esse email tiver uma conta, um link de redefinição foi enviado." },
    }).as("forgot");

    cy.visit("/");
    cy.contains("button", "Esqueceu sua senha?").click();
    cy.get('input[type="email"]').type("alguem@teste.com");
    cy.contains("button", "Mandar link de redefinição").click();

    cy.wait("@forgot").its("request.body").should("deep.equal", { email: "alguem@teste.com" });
    cy.contains("Se esse email tiver uma conta, você vai receber um link");
  });

  it("volta pro login sem perder o resto do formulário quebrado", () => {
    cy.visit("/");
    cy.contains("button", "Esqueceu sua senha?").click();
    cy.contains("Escolha uma senha nova").should("not.exist");
    cy.get('input[type="email"]').should("be.visible");

    cy.contains("button", "← Voltar pro login").click();
    cy.contains("button", "Mergulhar").should("be.visible");
    cy.contains("button", "Esqueceu sua senha?").should("be.visible");
  });

  it("abrir o link do email (?resetToken=) mostra a tela de nova senha, sem precisar estar logado", () => {
    cy.visit("/?resetToken=abc123token");
    cy.contains("Escolha uma senha nova");
    cy.get('input[placeholder^="Nova senha"]').should("be.visible");
  });

  it("senhas diferentes mostram erro sem chamar a API", () => {
    let called = false;
    cy.intercept("POST", "/api/auth/reset-password", () => { called = true; });

    cy.visit("/?resetToken=abc123token");
    cy.get('input[placeholder^="Nova senha"]').type("senha-nova-12345");
    cy.get('input[placeholder="Confirmar nova senha"]').type("outra-senha-999");
    cy.contains("button", "Redefinir senha").click();

    cy.contains("As duas senhas não coincidem.");
    cy.then(() => expect(called).to.be.false);
  });

  it("redefine com sucesso e volta pro login", () => {
    cy.intercept("POST", "/api/auth/reset-password", { statusCode: 200, body: {} }).as("reset");

    cy.visit("/?resetToken=abc123token");
    cy.get('input[placeholder^="Nova senha"]').type("senha-nova-12345");
    cy.get('input[placeholder="Confirmar nova senha"]').type("senha-nova-12345");
    cy.contains("button", "Redefinir senha").click();

    cy.wait("@reset").its("request.body").should("deep.equal", { token: "abc123token", newPassword: "senha-nova-12345" });
    cy.contains("Senha redefinida!");

    cy.contains("button", "Ir pro login").click();
    cy.location("search").should("eq", "");
    cy.contains("button", "Mergulhar").should("be.visible");
  });

  it("token inválido/expirado mostra o erro do servidor", () => {
    cy.intercept("POST", "/api/auth/reset-password", {
      statusCode: 400, body: { error: "Link inválido ou expirado — peça uma nova redefinição de senha." },
    }).as("resetFail");

    cy.visit("/?resetToken=token-vencido");
    cy.get('input[placeholder^="Nova senha"]').type("senha-nova-12345");
    cy.get('input[placeholder="Confirmar nova senha"]').type("senha-nova-12345");
    cy.contains("button", "Redefinir senha").click();

    cy.wait("@resetFail");
    cy.contains("Link inválido ou expirado");
  });
});
