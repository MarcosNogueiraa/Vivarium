import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";

const STATUS_REFRESH_MS = 30_000;

/** Estado da gestação em andamento do usuário (se houver), com polling periódico. */
export function useBreeding() {
  const [status, setStatus] = useState(null);

  const refresh = useCallback(async () => { setStatus(await api.breedingStatus()); }, []);

  useEffect(() => {
    refresh().catch(() => {});
    const timer = setInterval(() => refresh().catch(() => {}), STATUS_REFRESH_MS);
    return () => clearInterval(timer);
  }, [refresh]);

  return { status, refresh };
}
