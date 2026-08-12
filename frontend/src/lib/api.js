// Cliente da API. Em dev o Vite faz proxy de /api; em produção (Cloudflare
// Pages) definir VITE_API_URL apontando pro backend no Oracle Cloud.
const BASE = import.meta.env.VITE_API_URL ?? "";

export const getToken = () => localStorage.getItem("vivarium_token");
export const setToken = (t) => localStorage.setItem("vivarium_token", t);
export const clearToken = () => localStorage.removeItem("vivarium_token");

async function request(method, path, body) {
  const headers = { "Content-Type": "application/json" };
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(BASE + path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 401 && token) {
    clearToken();
    window.location.reload();
    return null;
  }
  if (!response.ok) {
    let message = "Não foi possível completar a ação. Tente novamente.";
    try {
      const data = await response.json();
      if (data?.error) message = data.error;
    } catch { /* corpo não-JSON */ }
    throw new Error(message);
  }
  if (response.status === 204) return null;
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

export const api = {
  register: (username, email, password) =>
    request("POST", "/api/auth/register", { username, email, password }),
  login: (usernameOrEmail, password) =>
    request("POST", "/api/auth/login", { usernameOrEmail, password }),
  me: () => request("GET", "/api/auth/me"),
  tank: () => request("GET", "/api/game/tank"),
  heartbeat: () => request("POST", "/api/game/heartbeat"),
  collect: (queueItemId) => request("POST", `/api/game/collect/${queueItemId}`),
  rushQueueItem: (queueItemId) => request("POST", `/api/game/queue/${queueItemId}/rush`),
  rushBreeding: () => request("POST", "/api/breeding/rush"),
  listings: () => request("GET", "/api/market/listings"),
  createListing: (creatureInstanceId, priceSoft) =>
    request("POST", "/api/market/listings", { creatureInstanceId, priceSoft }),
  cancelListing: (id) => request("POST", `/api/market/listings/${id}/cancel`),
  buyListing: (id) => request("POST", `/api/market/listings/${id}/buy`),
  items: () => request("GET", "/api/items/"),
  buyItem: (key) => request("POST", `/api/items/${key}/buy`),
  backpack: () => request("GET", "/api/game/backpack"),
  storeCreature: (id) => request("POST", `/api/game/creatures/${id}/store`),
  deployCreature: (id) => request("POST", `/api/game/creatures/${id}/deploy`),
  sellToVendor: (id) => request("POST", `/api/game/creatures/${id}/sell-vendor`),
  devSpawn: () => request("POST", "/api/dev/spawn"),
  devClear: () => request("POST", "/api/dev/clear"),
  devCoins: (amount = 1000, currency = "SOFT") => request("POST", `/api/dev/coins?amount=${amount}&currency=${currency}`),
  adminGiveStarterFishAll: () => request("POST", "/api/admin/give-starter-fish-all"),
  transferCreature: (id, toUsername) =>
    request("POST", `/api/game/creatures/${id}/transfer`, { toUsername }),
  breedingStatus: () => request("GET", "/api/breeding"),
  breedingQuote: (parentAId, parentBId) =>
    request("GET", `/api/breeding/quote?parentAId=${parentAId}&parentBId=${parentBId}`),
  startBreeding: (parentAId, parentBId, { useStabilizer = false, useInsurance = false } = {}) =>
    request("POST", "/api/breeding/start", { parentAId, parentBId, useStabilizer, useInsurance }),
  collectBreeding: () => request("POST", "/api/breeding/collect"),
  devFinishBreeding: () => request("POST", "/api/dev/breeding/finish"),
  dailyRewardStatus: () => request("GET", "/api/game/daily-reward"),
  claimDailyReward: () => request("POST", "/api/game/daily-reward/claim"),
  leaderboard: (metric) => request("GET", `/api/leaderboard/${metric}`),
  spectatorTank: (username) => request("GET", `/api/leaderboard/visit/${encodeURIComponent(username)}`),
  vipStatus: () => request("GET", "/api/vip"),
  subscribeVip: (days) => request("POST", "/api/vip/subscribe", { days }),
  setAutoCleanTrigger: (percent) => request("POST", "/api/game/water-sensor/trigger", { percent }),
};
