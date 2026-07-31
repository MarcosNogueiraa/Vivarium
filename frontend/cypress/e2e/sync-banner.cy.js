// E2E do banner de "sincronização perdida" — precisa ficar em destaque e sugerir F5
// quando o polling em background (heartbeat/refresh do tanque) falha repetidas vezes.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const tankFixture = {
  online: true, maintenanceLevel: 100, capacity: 3, queueCap: 5,
  queue: [], creatures: [], wallet: { SOFT: 100, PREMIUM: 0 },
  coinsPerHour: 0, generationProgressMinutes: 0, generationIntervalMinutes: 60,
};

describe("Banner de sincronização perdida", () => {
  it("some enquanto tudo funciona e aparece em destaque, sugerindo F5, após falhas consecutivas", () => {
    cy.clock(); // controla os timers do polling (heartbeat 60s / refresh do tanque 30s)

    cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");

    let heartbeatCalls = 0;
    cy.intercept("POST", "/api/game/heartbeat", (req) => {
      heartbeatCalls++;
      if (heartbeatCalls === 1) req.reply({ statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
      else req.reply({ statusCode: 500 }); // a partir da 2ª chamada, simula o servidor caindo
    }).as("heartbeat");

    let tankCalls = 0;
    cy.intercept("GET", "/api/game/tank", (req) => {
      tankCalls++;
      if (tankCalls === 1) req.reply({ body: tankFixture });
      else req.reply({ statusCode: 500 });
    }).as("tank");

    cy.visit("/");
    cy.get('input[placeholder="Username ou email"]').type("jogador1");
    cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
    cy.contains("button", "Mergulhar").click();
    cy.wait("@login");
    cy.wait("@tank"); // 1ª chamada, com sucesso — entra no jogo normalmente

    cy.contains("Sincronização perdida").should("not.exist");

    // Avança 60s de tempo simulado: heartbeat (60s) e refresh do tanque (30s) disparam de
    // novo e falham — 2 falhas consecutivas é o limiar que liga o aviso (useGame.js).
    cy.tick(60_000);

    cy.contains("Sincronização perdida").should("be.visible");
    cy.contains("F5").should("be.visible"); // sugestão explícita de recarregar
    cy.contains("button", "Recarregar agora").should("be.visible");
  });
});
