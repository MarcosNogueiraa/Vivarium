import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";

const STATUS_REFRESH_MS = 30_000;
const SYNC_ERROR_THRESHOLD = 2;

/**
 * Estado da gestação em andamento do usuário (se houver), com polling periódico.
 * `syncError` liga após falhas consecutivas (mesmo raciocínio de useGame.js).
 */
export function useBreeding() {
  const [status, setStatus] = useState(null);
  const [syncError, setSyncError] = useState(false);
  const failCountRef = useRef(0);

  const refresh = useCallback(async () => { setStatus(await api.breedingStatus()); }, []);

  useEffect(() => {
    function onSuccess() { failCountRef.current = 0; setSyncError(false); }
    function onFailure() {
      failCountRef.current += 1;
      if (failCountRef.current >= SYNC_ERROR_THRESHOLD) setSyncError(true);
    }

    refresh().then(onSuccess).catch(onFailure);
    const timer = setInterval(() => refresh().then(onSuccess).catch(onFailure), STATUS_REFRESH_MS);
    return () => clearInterval(timer);
  }, [refresh]);

  return { status, refresh, syncError };
}
