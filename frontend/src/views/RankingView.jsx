import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { decorTierOf } from "../lib/fishRenderer.js";
import { AquariumCanvas } from "../components/AquariumCanvas.jsx";
import { Coin } from "../components/Coin.jsx";
import { FishDetail } from "./FishDetail.jsx";

const METRICS = [
  // toFixed(3), não (1): score guarda 4 casas decimais no banco (HasPrecision(10,4)) — com 1
  // casa só, muitos jogadores empatavam no ranking à medida que a base crescia (pedido do
  // usuário, 11/08/2026; 2→3 casas no mesmo dia, ainda mais improvável de empatar). Renda
  // continua em 1 casa — ali a chance de empate é bem menor (função exponencial contínua,
  // não soma de vários scores discretos).
  { key: "rarity", label: "Raridade total", suffix: "", icon: "🏆", format: (v) => v.toFixed(3) },
  { key: "income", label: "Renda por hora", suffix: "/h", icon: null, format: (v) => v.toFixed(1) },
];

export function RankingView({ notify }) {
  const [metric, setMetric] = useState("rarity");
  const [data, setData] = useState(null);
  const [spectator, setSpectator] = useState(null);
  const [selectedId, setSelectedId] = useState(null);

  const refresh = useCallback(async (m) => {
    setData(null);
    setData(await api.leaderboard(m));
  }, []);

  useEffect(() => { refresh(metric).catch((err) => notify(err.message)); }, [metric, refresh, notify]);

  async function visit(username) {
    try {
      setSpectator(await api.spectatorTank(username));
      setSelectedId(null);
    } catch (err) { notify(err.message); }
  }

  if (spectator) {
    const decorTier = decorTierOf(spectator.capacityBandName);
    const selected = spectator.creatures.find((c) => c.id === selectedId) ?? null;
    return (
      <div className="ranking-spectator">
        <div className="spectator-header">
          <button onClick={() => { setSpectator(null); setSelectedId(null); }}>← Voltar ao ranking</button>
          <strong>Aquário de {spectator.username}</strong>
          <span className="hint">{spectator.capacityBandName} · {spectator.creatures.length} peixe(s) · só visualização</span>
        </div>
        <div className="spectator-stats">
          <span className="stat-chip">🏆 {Number(spectator.rarityTotal).toFixed(3)} <small>raridade total</small></span>
          <span className="stat-chip"><Coin />{Number(spectator.coinsPerHour).toFixed(1)} <small>/h</small></span>
          <span className="stat-chip">💧 {Number(spectator.maintenanceLevel).toFixed(0)} <small>água</small></span>
        </div>
        <AquariumCanvas
          creatures={spectator.creatures}
          selectedId={selectedId}
          onSelect={setSelectedId}
          interactive
          quality={Number(spectator.maintenanceLevel)}
          decorTier={decorTier}
        />
        {selected && <FishDetail creature={selected} onClose={() => setSelectedId(null)} />}
      </div>
    );
  }

  const active = METRICS.find((m) => m.key === metric);

  return (
    <div className="ranking-view">
      <nav className="nav-pills">
        {METRICS.map((m) => (
          <button key={m.key} className={metric === m.key ? "active" : ""} onClick={() => setMetric(m.key)}>
            {m.label}
          </button>
        ))}
      </nav>

      {data === null && <p className="hint">Carregando ranking…</p>}

      {data && (
        <div className="leaderboard glass">
          {data.entries.map((e) => (
            <div key={e.rank} className={`leaderboard-row${e.isSelf ? " is-self" : ""}`}>
              <span className="rank">#{e.rank}</span>
              <span className="username">{e.username}{e.isSelf && " (você)"}</span>
              <span className="value mono">{active.icon ? active.icon : <Coin />}{active.format(Number(e.value))}<small>{active.suffix}</small></span>
              {!e.isSelf && <button onClick={() => visit(e.username)}>Visitar</button>}
            </div>
          ))}
          {data.selfOutsideTop && (
            <div className="leaderboard-row is-self leaderboard-self-outside">
              <span className="rank">#{data.selfOutsideTop.rank}</span>
              <span className="username">{data.selfOutsideTop.username} (você)</span>
              <span className="value mono">{active.icon ? active.icon : <Coin />}{active.format(Number(data.selfOutsideTop.value))}<small>{active.suffix}</small></span>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
