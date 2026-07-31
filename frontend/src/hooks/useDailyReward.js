import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";

const REFRESH_MS = 5 * 60_000; // não é urgente (é baseado em dia calendário) — só cobre virar o dia com a aba aberta

/** Status da recompensa diária (8.10) + ação de resgatar. */
export function useDailyReward() {
  const [status, setStatus] = useState(null);

  const refresh = useCallback(async () => { setStatus(await api.dailyRewardStatus()); }, []);

  useEffect(() => {
    refresh().catch(() => {});
    const timer = setInterval(() => refresh().catch(() => {}), REFRESH_MS);
    return () => clearInterval(timer);
  }, [refresh]);

  return { status, refresh };
}
