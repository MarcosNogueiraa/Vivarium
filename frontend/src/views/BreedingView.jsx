import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { useBreeding } from "../hooks/useBreeding.js";
import { AquariumCanvas } from "../components/AquariumCanvas.jsx";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { bandOf } from "../lib/fishRenderer.js";

function timeLeft(readyAt) {
  const ms = new Date(readyAt).getTime() - Date.now();
  if (ms <= 0) return "pronto!";
  const totalMin = Math.ceil(ms / 60_000);
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return h > 0 ? `${h}h ${m}min` : `${m}min`;
}

export function BreedingView({ tank, refreshTank, notify }) {
  const { status, refresh } = useBreeding();
  const [backpack, setBackpack] = useState(null);
  const [pickA, setPickA] = useState(null);
  const [pickB, setPickB] = useState(null);
  const [, forceTick] = useState(0);

  const loadBackpack = useCallback(async () => { setBackpack(await api.backpack()); }, []);
  useEffect(() => { loadBackpack().catch((e) => notify(e.message)); }, [loadBackpack, notify]);

  // Sem timer de 1s no servidor: só re-renderiza periodicamente pra a contagem
  // regressiva local acompanhar o relógio.
  useEffect(() => {
    const t = setInterval(() => forceTick((n) => n + 1), 30_000);
    return () => clearInterval(t);
  }, []);

  if (status === null || backpack === null) return <p className="hint">Carregando o ninho…</p>;

  async function start() {
    try {
      await api.startBreeding(pickA.id, pickB.id);
      notify("Casal levado pro ninho!");
      setPickA(null);
      setPickB(null);
      await Promise.all([refresh(), refreshTank(), loadBackpack()]);
    } catch (e) { notify(e.message); }
  }

  async function collect() {
    try {
      await api.collectBreeding();
      notify("Filhote coletado!");
      await Promise.all([refresh(), refreshTank(), loadBackpack()]);
    } catch (e) { notify(e.message); }
  }

  async function devFinish() {
    try {
      await api.devFinishBreeding();
      notify("Gestação zerada (dev).");
      await refresh();
    } catch (e) { notify(e.message); }
  }

  function togglePick(c) {
    if (pickA?.id === c.id) { setPickA(null); return; }
    if (pickB?.id === c.id) { setPickB(null); return; }
    if (!pickA) setPickA(c);
    else if (!pickB) setPickB(c);
  }

  if (status.active) {
    const { slot } = status;
    return (
      <>
        <div className="section-head">
          <span className="eyebrow">Ninho</span>
          <span className="count">{slot.isReady ? "pronto!" : timeLeft(slot.readyAt)}</span>
        </div>
        <AquariumCanvas
          creatures={[slot.parentA, slot.parentB]}
          selectedId={null} onSelect={() => {}} interactive={false} ambient theme="breeding"
        />
        <div className="card-row" style={{ justifyContent: "center", gap: 16, marginTop: 12 }}>
          <RarityBadge score={Number(slot.parentA.rarityScore)} />
          <span>+</span>
          <RarityBadge score={Number(slot.parentB.rarityScore)} />
        </div>
        <div className="card-row" style={{ justifyContent: "center", gap: 8, marginTop: 12 }}>
          <button className="btn-primary" disabled={!slot.isReady} onClick={collect}>
            {slot.isReady ? "Coletar filhote" : "Aguardando…"}
          </button>
          {import.meta.env.DEV && !slot.isReady && (
            <button className="dev-btn" onClick={devFinish} title="Só existe em dev">
              Terminar gestação
            </button>
          )}
        </div>
      </>
    );
  }

  const candidates = [...tank.creatures, ...backpack.creatures];

  return (
    <>
      <div className="section-head">
        <span className="eyebrow">Ninho</span>
      </div>
      {candidates.length < 2 ? (
        <p className="hint">Você precisa de pelo menos 2 peixes (no tanque ou na mochila) pra tentar cruzar.</p>
      ) : (
        <>
          <p className="hint">
            Escolha 2 peixes pra levar pro ninho. Quanto mais raro o casal, mais demorada a gestação.
          </p>
          <div className="grid">
            {candidates.map((c) => {
              const picked = pickA?.id === c.id || pickB?.id === c.id;
              return (
                <div
                  key={c.id}
                  className="card"
                  style={{
                    "--tier": bandOf(Number(c.rarityScore)).color,
                    ...(picked ? { borderColor: "var(--tier)", boxShadow: "0 0 0 2px var(--tier)" } : {}),
                  }}
                >
                  <button className="fish-stage as-button" onClick={() => togglePick(c)} title="Selecionar">
                    <FishCanvas seed={c.seed} />
                  </button>
                  <RarityBadge score={Number(c.rarityScore)} />
                </div>
              );
            })}
          </div>
          <div className="card-row" style={{ justifyContent: "center", marginTop: 12 }}>
            <button className="btn-primary" disabled={!pickA || !pickB} onClick={start}>
              Levar pro ninho
            </button>
          </div>
        </>
      )}
    </>
  );
}
