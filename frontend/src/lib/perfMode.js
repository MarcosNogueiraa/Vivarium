// "Modo econômico" — preferência do jogador (localStorage, não por tanque), pra tablets/
// notebooks fracos que travam com o aquário em resolução/qualidade cheia (relato real,
// CLAUDE.md/memória de sessão 19/08/2026). AquariumCanvas lê via ref (não prop encadeada por
// toda view) pra qualquer instância na tela (Tanque/Ninho/Ranking/login) reagir junto quando
// o jogador muda a preferência em qualquer uma delas.
const KEY = "vivarium_economy_mode";
const listeners = new Set();

export function getEconomyMode() {
  return typeof localStorage !== "undefined" && localStorage.getItem(KEY) === "1";
}

export function setEconomyMode(on) {
  localStorage.setItem(KEY, on ? "1" : "0");
  for (const fn of listeners) fn(on);
}

export function subscribeEconomyMode(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}
