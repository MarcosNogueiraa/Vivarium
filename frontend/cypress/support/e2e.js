// Stub padrão pra chamadas que toda tela logada dispara em background (polling de
// useGame/useDailyReward), assim specs que não testam essas telas não precisam
// mockar tudo — só sobrescrever com um cy.intercept mais específico quando o teste
// se importa com o resultado.
beforeEach(() => {
  cy.intercept("GET", "/api/game/daily-reward", {
    statusCode: 200, body: { canClaim: false, amount: 25, nextAvailableAtUtc: null },
  });
});
