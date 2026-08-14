import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";

const REFRESH_MS = 30_000; // mesma cadência de useBreeding.js — compra/transferência/mensagem
// admin precisam aparecer com uma frescor razoável.
const SYNC_ERROR_THRESHOLD = 2;

/**
 * Caixa de Entrada (CLAUDE.md §8.23/§8.24), com polling periódico — molde de useBreeding.js.
 * A lista já traz TODAS as entradas (pendentes + já resgatadas, que ficam visíveis até
 * "Apagar mensagens lidas"); a contagem do badge é derivada aqui mesmo (sem endpoint à parte).
 */
export function useInbox() {
  const [entries, setEntries] = useState(null);
  const [syncError, setSyncError] = useState(false);
  const failCountRef = useRef(0);

  const refresh = useCallback(async () => {
    const { entries } = await api.inboxList();
    setEntries(entries);
  }, []);

  useEffect(() => {
    function onSuccess() { failCountRef.current = 0; setSyncError(false); }
    function onFailure() {
      failCountRef.current += 1;
      if (failCountRef.current >= SYNC_ERROR_THRESHOLD) setSyncError(true);
    }

    refresh().then(onSuccess).catch(onFailure);
    const timer = setInterval(() => refresh().then(onSuccess).catch(onFailure), REFRESH_MS);
    return () => clearInterval(timer);
  }, [refresh]);

  const unclaimedCount = entries?.filter((e) => !e.claimedAt).length ?? 0;

  return { entries, unclaimedCount, refresh, syncError };
}
