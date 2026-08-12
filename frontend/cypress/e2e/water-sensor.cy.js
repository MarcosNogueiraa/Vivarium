// E2E do Sensor de Qualidade da Água + Limpeza Automática (§8.18): compra do sensor,
// slider aparece só depois de comprado, e o gatilho é salvo (com debounce) e persiste
// ao recarregar. API mockada via cy.intercept.

function fakeJwt(sub = "1", username = "jogador1") {
  const b64 = (obj) => btoa(JSON.stringify(obj)).replace(/=+$/, "");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub, unique_name: username })}.sig`;
}

const vipStatus = { active: false, endAt: null, packages: { 7: 7, 15: 10, 30: 15 } };

const itemsNaoComprado = [
  { key: "filter_basic", name: "Filtro", price: 20, owned: false, locked: false },
  { key: "water_sensor", name: "Sensor de Qualidade da Água", price: 800, owned: false, locked: false },
];

const itemsComprado = [
  { key: "filter_basic", name: "Filtro", price: 20, owned: false, locked: false },
  { key: "water_sensor", name: "Sensor de Qualidade da Água", price: 800, owned: true, locked: false },
];

function login(tankOverrides = {}, items = itemsNaoComprado) {
  cy.intercept("POST", "/api/auth/login", { statusCode: 200, body: { token: fakeJwt() } }).as("login");
  cy.intercept("POST", "/api/game/heartbeat", { statusCode: 200, body: { online: true, maintenanceLevel: 100 } });
  cy.fixture("tank-empty.json").then((base) => {
    cy.intercept("GET", "/api/game/tank", {
      body: { ...base, hasWaterSensor: false, autoCleanTriggerPercent: 0, waterSensorMaxTriggerPercent: 80, ...tankOverrides },
    }).as("tank");
  });
  cy.intercept("GET", "/api/vip", { body: vipStatus }).as("vip");
  cy.intercept("GET", "/api/items/", { body: items }).as("items");

  cy.visit("/");
  cy.get('input[placeholder="Username ou email"]').type("jogador1");
  cy.get('input[placeholder^="Senha"]').type("senha-forte-123");
  cy.contains("button", "Mergulhar").click();
  cy.wait("@login");
  cy.wait("@tank");
  cy.contains("button", "Loja").click();
  cy.wait(["@vip", "@items"]);
}

describe("Sensor de Qualidade da Água", () => {
  it("sem o sensor: mostra preço e botão de compra, sem slider", () => {
    login();

    cy.contains(".card", "Sensor de Qualidade da Água").within(() => {
      cy.contains("800");
      cy.contains("button", "Comprar");
      cy.get('input[type="range"]').should("not.exist");
    });
  });

  it("comprar o sensor troca o botão pelo slider do gatilho", () => {
    login();

    cy.intercept("POST", "/api/items/water_sensor/buy", { statusCode: 200, body: {} }).as("buy");
    cy.fixture("tank-empty.json").then((base) => {
      cy.intercept("GET", "/api/game/tank", {
        body: { ...base, hasWaterSensor: true, autoCleanTriggerPercent: 0, waterSensorMaxTriggerPercent: 80 },
      }).as("tankAfter");
    });
    cy.intercept("GET", "/api/items/", { body: itemsComprado }).as("itemsAfter");

    cy.contains(".card", "Sensor de Qualidade da Água").contains("button", "Comprar").click();
    cy.wait("@buy");
    cy.contains("Sensor de Qualidade da Água comprado!");

    cy.contains(".card", "Sensor de Qualidade da Água").within(() => {
      cy.get('input[type="range"]').should("exist");
      cy.contains("button", "Comprar").should("not.exist");
    });
  });

  it("mover o slider salva o gatilho (com debounce) e não dispara request a cada pixel", () => {
    cy.clock();
    login({ hasWaterSensor: true, autoCleanTriggerPercent: 0 }, itemsComprado);

    cy.intercept("POST", "/api/game/water-sensor/trigger", { statusCode: 200, body: { autoCleanTriggerPercent: 60 } }).as("setTrigger");

    cy.contains(".card", "Sensor de Qualidade da Água").within(() => {
      cy.get('input[type="range"]').invoke("val", 60).trigger("input", { force: true });
    });

    // Antes do debounce (400ms), nenhuma request ainda.
    cy.tick(200);
    cy.get("@setTrigger.all").should("have.length", 0);

    cy.tick(300);
    cy.wait("@setTrigger").its("request.body").should("deep.equal", { percent: 60 });
  });

  it("sem VIP ativo, avisa que o sensor comprado ainda não tem efeito", () => {
    login({ hasWaterSensor: true, autoCleanTriggerPercent: 30, isVip: false }, itemsComprado);

    cy.contains(".card", "Sensor de Qualidade da Água").within(() => {
      cy.contains("Só tem efeito com VIP ativo");
    });
  });

  it("com VIP ativo, o indicador de gatilho aparece no tanque", () => {
    login({ hasWaterSensor: true, autoCleanTriggerPercent: 45, isVip: true }, itemsComprado);

    cy.contains("button", "Tanque").click();
    cy.contains(".capacity-chip", "45%");
  });
});
