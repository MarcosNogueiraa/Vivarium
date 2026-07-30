import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { useBreeding } from "../hooks/useBreeding.js";
import { AquariumCanvas } from "../components/AquariumCanvas.jsx";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Modal } from "../components/Modal.jsx";
import { Coin } from "../components/Coin.jsx";
import { CollectCelebration } from "../components/CollectCelebration.jsx";
import { bandOf, PT } from "../lib/fishRenderer.js";

function timeLeft(readyAt) {
  const ms = new Date(readyAt).getTime() - Date.now();
  if (ms <= 0) return "pronto!";
  const totalMin = Math.ceil(ms / 60_000);
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return h > 0 ? `${h}h ${m}min` : `${m}min`;
}

function hoursLabel(h) {
  if (h < 24) return `${h.toFixed(1)}h`;
  return `${(h / 24).toFixed(1)} dias`;
}

export function BreedingView({ tank, refreshTank, notify }) {
  const { status, refresh } = useBreeding();
  const [backpack, setBackpack] = useState(null);
  const [pickA, setPickA] = useState(null);
  const [pickB, setPickB] = useState(null);
  const [quote, setQuote] = useState(null); // null = fechado, "loading" = carregando, objeto = pronto
  const [celebrate, setCelebrate] = useState(null); // { child, parentLosses }
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
      setQuote(null);
      await Promise.all([refresh(), refreshTank(), loadBackpack()]);
    } catch (e) { notify(e.message); }
  }

  async function collect() {
    try {
      const result = await api.collectBreeding();
      const parentLosses = [result.parentADied, result.parentBDied].filter(Boolean);
      setCelebrate({ child: result.child, parentLosses });
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

  async function openQuote() {
    setQuote("loading");
    try {
      setQuote(await api.breedingQuote(pickA.id, pickB.id));
    } catch (e) {
      notify(e.message);
      setQuote(null);
    }
  }

  async function confirmStart() {
    await start();
  }

  const candidates = [...tank.creatures, ...backpack.creatures];
  const bothPicked = pickA && pickB;

  const content = status.active ? (
    <>
      <div className="section-head">
        <span className="eyebrow">Ninho</span>
        <span className="count">{status.slot.isReady ? "pronto!" : timeLeft(status.slot.readyAt)}</span>
      </div>
      <AquariumCanvas
        creatures={[status.slot.parentA, status.slot.parentB]}
        selectedId={null} onSelect={() => {}} interactive={false} ambient theme="breeding"
      />
      <div className="card-row" style={{ justifyContent: "center", gap: 16, marginTop: 12 }}>
        <RarityBadge score={Number(status.slot.parentA.rarityScore)} />
        <span>+</span>
        <RarityBadge score={Number(status.slot.parentB.rarityScore)} />
      </div>
      <p className="hint" style={{ textAlign: "center" }}>Custo pago: <Coin /> {Number(status.slot.costPaid).toFixed(0)} soft</p>
      <div className="card-row" style={{ justifyContent: "center", gap: 8, marginTop: 12 }}>
        <button className="btn-primary" disabled={!status.slot.isReady} onClick={collect}>
          {status.slot.isReady ? "Coletar filhote" : "Aguardando…"}
        </button>
        {import.meta.env.DEV && !status.slot.isReady && (
          <button className="dev-btn" onClick={devFinish} title="Só existe em dev">
            Terminar gestação
          </button>
        )}
      </div>
    </>
  ) : (
    <>
      <div className="section-head">
        <span className="eyebrow">Ninho</span>
      </div>
      {candidates.length < 2 ? (
        <p className="hint">Você precisa de pelo menos 2 peixes (no tanque ou na mochila) pra tentar cruzar.</p>
      ) : (
        <>
          <p className="hint">
            Escolha 2 peixes pra levar pro ninho. Quanto mais raro o casal, mais demorada (e cara) a gestação.
          </p>
          <div className="grid" style={{ paddingBottom: bothPicked ? 70 : 0 }}>
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
                    <FishCanvas seed={c.seed} isBred={c.isBred} parentASeed={c.parentASeed} parentBSeed={c.parentBSeed} />
                  </button>
                  <RarityBadge score={Number(c.rarityScore)} />
                </div>
              );
            })}
          </div>
        </>
      )}

      {bothPicked && (
        <div className="sticky-bar">
          <span className="hint" style={{ padding: 0 }}>2 peixes selecionados</span>
          <button className="btn-primary" onClick={openQuote}>Ver chances e cruzar</button>
        </div>
      )}
    </>
  );

  return (
    <>
      {content}

      {quote && (
        <Modal onClose={() => setQuote(null)}>
          <div className="eyebrow">Prévia do cruzamento</div>
          {quote === "loading" ? (
            <p className="hint">Calculando…</p>
          ) : (
            <>
              <div className="card-row" style={{ marginTop: 10 }}>
                <span>Custo</span>
                <span className="mono"><Coin /> {quote.costSoft.toFixed(0)} soft</span>
              </div>
              <div className="card-row">
                <span>Gestação</span>
                <span className="mono">{hoursLabel(quote.gestationHours)}</span>
              </div>

              <div className="detail-section">
                <div className="eyebrow">Chance do brilho do filhote</div>
                <div className="breakdown">
                  {Object.entries(quote.childTierProbabilities)
                    .sort((a, b) => b[1] - a[1])
                    .map(([tier, p]) => (
                      <div className="bd-row" key={tier}>
                        <span className="bd-label">{PT.tier[tier] ?? tier}</span>
                        <span className="bd-bar"><span style={{ width: `${p * 100}%` }} /></span>
                        <span className="bd-prob mono">{(p * 100).toFixed(1)}%</span>
                      </div>
                    ))}
                </div>
              </div>

              <div className="detail-section">
                <div className="eyebrow">Risco de morte dos pais</div>
                <p className="bd-help">Cada cruzamento completado aumenta o risco do próximo — nunca garantido, mas cresce com o uso.</p>
                <div className="card-row">
                  <span>Pai A ({quote.parentABreedCount}× cruzado)</span>
                  <span className="mono">{(quote.parentADeathChance * 100).toFixed(0)}%</span>
                </div>
                <div className="card-row">
                  <span>Pai B ({quote.parentBBreedCount}× cruzado)</span>
                  <span className="mono">{(quote.parentBDeathChance * 100).toFixed(0)}%</span>
                </div>
              </div>

              <div className="detail-actions">
                <button className="btn-primary" onClick={confirmStart}>Confirmar cruzamento</button>
                <button onClick={() => setQuote(null)}>Cancelar</button>
              </div>
            </>
          )}
        </Modal>
      )}

      {celebrate && (
        <CollectCelebration
          creature={celebrate.child}
          variant="breeding"
          parentLosses={celebrate.parentLosses}
          onClose={() => setCelebrate(null)}
        />
      )}
    </>
  );
}
