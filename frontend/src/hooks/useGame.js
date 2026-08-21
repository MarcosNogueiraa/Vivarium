import { useCallback, useEffect, useRef, useState } from "react";
import { api, getToken } from "../lib/api.js";

const HEARTBEAT_MS = 60_000; // CLAUDE.md 8.3
const TANK_REFRESH_MS = 30_000;
const SYNC_ERROR_THRESHOLD = 2; // falhas consecutivas antes de avisar (evita alarme por 1 soluço de rede)

/**
 * Estado de jogo do usuário logado: tanque, id do usuário e o loop de heartbeat
 * (marca online) + refresh periódico do tanque. `refreshTank` recarrega sob demanda.
 * `syncError` liga depois de falhas consecutivas no polling em segundo plano — sem isso,
 * quem deixa a aba aberta como tela de fundo não percebe que parou de sincronizar
 * (rede caiu, token expirou etc.) até voltar e notar que nada mudou.
 */
export function useGame() {
  const [tank, setTank] = useState(null);
  const [userId, setUserId] = useState(null);
  const [syncError, setSyncError] = useState(false);
  const [bandUpgrade, setBandUpgrade] = useState(null);
  const failCountRef = useRef(0);
  const prevBandRef = useRef(null);

  const refreshTank = useCallback(async () => {
    const next = await api.tank();
    setTank(next);
    // Celebra só quando a faixa realmente MUDA pra uma capacidade maior — não no
    // primeiro load da sessão (prevBandRef ainda null) nem em refreshes normais.
    const prevBand = prevBandRef.current;
    if (prevBand && next.capacityBandName && next.capacityBandName !== prevBand.name
      && next.capacity > prevBand.capacity) {
      setBandUpgrade(next.capacityBandName);
    }
    prevBandRef.current = { name: next.capacityBandName, capacity: next.capacity };
  }, []);

  const dismissBandUpgrade = useCallback(() => setBandUpgrade(null), []);

  useEffect(() => {
    try {
      setUserId(Number(JSON.parse(atob(getToken().split(".")[1])).sub));
    } catch { /* token malformado cai no 401 do fetch */ }

    function onSuccess() { failCountRef.current = 0; setSyncError(false); }
    function onFailure() {
      failCountRef.current += 1;
      if (failCountRef.current >= SYNC_ERROR_THRESHOLD) setSyncError(true);
    }

    // 21/08/2026 (perto do teto de "Network Transfer" do Neon free tier): heartbeat e
    // refresh do tanque eram dois timers INDEPENDENTES que os dois buscavam o tanque
    // completo — a cada 60s, o tanque era buscado 3x (2 do tankTimer de 30s + 1 encadeada
    // aqui no heartbeat), sem nenhuma precisar de fato de dado mais fresco que a outra.
    // Heartbeat agora só marca online (POST leve, sem buscar o tanque); quem mantém o
    // tanque atualizado é só o tankTimer de 30s — corta 1/3 do tráfego de tanque de graça.
    // A carga INICIAL continua precisando buscar o tanque na hora (heartbeat sozinho não
    // devolve o tanque, e o primeiro tick do tankTimer só dispara 30s depois do mount).
    api.heartbeat().then(refreshTank).then(onSuccess).catch(onFailure);
    const heartbeatTimer = setInterval(() => api.heartbeat().then(onSuccess).catch(onFailure), HEARTBEAT_MS);
    const tankTimer = setInterval(() => refreshTank().then(onSuccess).catch(onFailure), TANK_REFRESH_MS);
    return () => { clearInterval(heartbeatTimer); clearInterval(tankTimer); };
  }, [refreshTank]);

  return { tank, userId, refreshTank, syncError, bandUpgrade, dismissBandUpgrade };
}
