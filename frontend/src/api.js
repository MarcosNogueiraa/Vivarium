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
    let message = `Erro ${response.status}`;
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
  tank: () => request("GET", "/api/game/tank"),
  heartbeat: () => request("POST", "/api/game/heartbeat"),
  collect: (queueItemId) => request("POST", `/api/game/collect/${queueItemId}`),
  listings: () => request("GET", "/api/market/listings"),
  createListing: (creatureInstanceId, priceSoft) =>
    request("POST", "/api/market/listings", { creatureInstanceId, priceSoft }),
  cancelListing: (id) => request("POST", `/api/market/listings/${id}/cancel`),
  buyListing: (id) => request("POST", `/api/market/listings/${id}/buy`),
  items: () => request("GET", "/api/items/"),
  buyItem: (key) => request("POST", `/api/items/${key}/buy`),
  devSpawn: () => request("POST", "/api/dev/spawn"),
};
