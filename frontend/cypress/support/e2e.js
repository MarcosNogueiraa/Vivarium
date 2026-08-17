// Stub padrão pra chamadas que toda tela logada dispara em background (polling de
// useGame/useDailyReward/useInbox), assim specs que não testam essas telas não precisam
// mockar tudo — só sobrescrever com um cy.intercept mais específico quando o teste
// se importa com o resultado.
//
// `/api/inbox/` SEM stub aqui é uma corrida de verdade, não só falta de mock: o JWT fake
// (fakeJwt() nos specs) não é válido contra a API real, então a chamada de fundo de
// useInbox() 401a — e o interceptor global de 401 em api.js (`clearToken()`) desloga o
// app NO MEIO do teste sempre que essa resposta chega antes da asserção rodar. Sem esse
// stub, toda spec vira uma moeda jogada pro alto (18/08/2026, achado revisando o redesenho
// da recompensa diária — suite inteira ficando flaky por causa disso, não só um spec).
beforeEach(() => {
  cy.intercept("GET", "/api/game/daily-reward", {
    statusCode: 200,
    body: { canClaim: false, minAmount: 25, maxAmount: 25, currentStreak: 1, streakBonusPercent: 0, eggChancePercent: 3, nextAvailableAtUtc: null },
  });
  cy.intercept("GET", "/api/inbox/", { statusCode: 200, body: { entries: [] } });
  // Mesma corrida — StoreView busca isso ao montar, não só em specs de VIP.
  cy.intercept("GET", "/api/vip", { statusCode: 200, body: { active: false, endAt: null, packages: { 7: 7, 15: 10, 30: 15 } } });
});
