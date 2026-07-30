import { useCallback, useEffect, useState } from "react";
import { api, getToken } from "../lib/api.js";

const HEARTBEAT_MS = 60_000; // CLAUDE.md 8.3
const TANK_REFRESH_MS = 30_000;

/**
 * Estado de jogo do usuário logado: tanque, id do usuário e o loop de heartbeat
 * (marca online) + refresh periódico do tanque. `refreshTank` recarrega sob demanda.
 */
export function useGame() {
  const [tank, setTank] = useState(null);
  const [userId, setUserId] = useState(null);

  const refreshTank = useCallback(async () => { setTank(await api.tank()); }, []);

  useEffect(() => {
    try {
      setUserId(Number(JSON.parse(atob(getToken().split(".")[1])).sub));
    } catch { /* token malformado cai no 401 do fetch */ }

    const beat = () => api.heartbeat().then(refreshTank).catch(() => {});
    beat();
    const heartbeatTimer = setInterval(beat, HEARTBEAT_MS);
    const tankTimer = setInterval(() => refreshTank().catch(() => {}), TANK_REFRESH_MS);
    return () => { clearInterval(heartbeatTimer); clearInterval(tankTimer); };
  }, [refreshTank]);

  return { tank, userId, refreshTank };
}
