import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { bandOf, decorTierOf, PART_HEX, PT } from "../lib/fishRenderer.js";
import { tankFishSorted, tankPotential } from "../lib/tankMath.js";
import { AquariumCanvas } from "../components/AquariumCanvas.jsx";
import { Coin } from "../components/Coin.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
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

export function RankingView({ notify, exitSpectatorSignal }) {
  const [metric, setMetric] = useState("rarity");
  const [data, setData] = useState(null);
  const [spectator, setSpectator] = useState(null);
  const [selectedId, setSelectedId] = useState(null);
  // Lista "Peixes no tanque" do espectador nasce minimizada (pedido do usuário, 12/08/2026)
  // — facilita analisar os peixes de quem você está visitando sem a lista ocupando a tela.
  const [listOpen, setListOpen] = useState(false);

  const refresh = useCallback(async (m) => {
    setData(null);
    setData(await api.leaderboard(m));
  }, []);

  useEffect(() => { refresh(metric).catch((err) => notify(err.message)); }, [metric, refresh, notify]);

  // Clicar de novo na aba "Ranking" enquanto visita um aquário volta pra lista, sem precisar
  // clicar em "Voltar" (pedido do usuário, 12/08/2026) — GameView incrementa esse sinal a cada
  // clique na aba já ativa; aqui só reage à mudança, não zera o resto do estado (metric fica).
  useEffect(() => {
    if (exitSpectatorSignal) { setSpectator(null); setSelectedId(null); }
  }, [exitSpectatorSignal]);

  async function visit(username) {
    try {
      setSpectator(await api.spectatorTank(username));
      setSelectedId(null);
      setListOpen(false);
    } catch (err) { notify(err.message); }
  }

  if (spectator) {
    const decorTier = decorTierOf(spectator.capacityBandName);
    const selected = spectator.creatures.find((c) => c.id === selectedId) ?? null;
    const listFish = tankFishSorted(spectator.creatures);
    // Perda por água suja (mesmo indicador do próprio tanque, TankView.jsx §8.6) — a renda
    // real já vem do servidor descontando a água; o "potencial" é recalculado client-side
    // (água a 100%) só pra mostrar a diferença. Pedido do usuário, 12/08/2026.
    const current = Number(spectator.coinsPerHour);
    const potential = tankPotential(spectator.creatures);
    const waterLossPerHour = Math.max(0, potential - current);
    return (
      <div className="ranking-spectator">
        <div className="spectator-header">
          <button onClick={() => { setSpectator(null); setSelectedId(null); }}>← Voltar ao ranking</button>
          <strong>Aquário de {spectator.username}</strong>
          <span className="hint">{spectator.capacityBandName} · {spectator.creatures.length} peixe(s) · só visualização</span>
        </div>
        <div className="spectator-stats">
          <span className="stat-chip">🏆 {Number(spectator.rarityTotal).toFixed(3)} <small>raridade total</small></span>
          <span className="stat-chip" title={
            waterLossPerHour > 0.05
              ? `Produção real agora: ${current.toFixed(1)}/h (já descontando a água suja). Com a água a 100% seria ${potential.toFixed(1)}/h.`
              : "Moedas por hora que o tanque farma"
          }>
            <Coin />{current.toFixed(1)} <small>/h</small>
            {waterLossPerHour > 0.05 && <em className="of-potential"> de {potential.toFixed(0)}</em>}
          </span>
          <span className="stat-chip">💧 {Number(spectator.maintenanceLevel).toFixed(0)} <small>água</small></span>
          {waterLossPerHour > 0.05 && (
            <span
              className="water-loss"
              title={`A água suja está custando ${waterLossPerHour.toFixed(1)}/h de renda pra ${spectator.username} — produziria ${potential.toFixed(1)}/h com a água a 100%.`}
            >
              −{waterLossPerHour.toFixed(1)}<small>/h</small>
            </span>
          )}
        </div>
        <AquariumCanvas
          creatures={spectator.creatures}
          selectedId={selectedId}
          onSelect={setSelectedId}
          interactive
          quality={Number(spectator.maintenanceLevel)}
          decorTier={decorTier}
        />
        {spectator.creatures.length > 0 && (
          <section className="cinema-dim">
            <div className="section-head">
              <button className="collapse-btn" onClick={() => setListOpen((v) => !v)} aria-expanded={listOpen}
                title={listOpen ? "Recolher lista" : "Expandir lista"}>
                <span className={`chevron${listOpen ? " open" : ""}`}>▾</span>
              </button>
              <span className="eyebrow">Peixes no tanque</span>
              <span className="count">{spectator.creatures.length}</span>
              <span className="spacer" />
              {listOpen && <span className="faint" style={{ fontSize: "0.82rem" }}>clique para detalhes</span>}
            </div>
            {listOpen && (
              <div className="fish-list">
                {listFish.map(({ c, traits, col, colorLabel, prod }) => {
                  const band = bandOf(Number(c.rarityScore));
                  return (
                    <button key={c.id} className="fish-row" onClick={() => setSelectedId(c.id)} style={{ "--tier": band.color }}>
                      <span className="fr-thumb">
                        <FishCanvas creature={c} width={72} />
                      </span>
                      <span className="fr-body">
                        <span className="fr-line1">
                          <span className="badge" style={{ "--tier": band.color }}><span className="gem" /> {band.name}</span>
                          <span className="fr-score mono">{Number(c.rarityScore).toFixed(1)}</span>
                        </span>
                        <span className="fr-line2">
                          <span className="fr-color"><span className="dot-color" style={{ background: PART_HEX[col] }} /> {colorLabel}</span>
                          {traits.shimmerTier !== "None" && <span className="shimmer-label">✦ {PT.shimmer[traits.shimmerColor]}</span>}
                          {c.isBred && <span className="bred-tag">🐣 Filhote</span>}
                        </span>
                      </span>
                      <span className="fr-prod mono"><Coin /> ~{prod.toFixed(1)}<small>/h</small></span>
                    </button>
                  );
                })}
              </div>
            )}
          </section>
        )}
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
              {/* Sempre reserva o espaço do botão, mesmo na própria linha (sem "Visitar")
                  — senão .username (flex:1) absorve esse espaço e o valor pula pra mais
                  perto da borda direita, desalinhando a coluna de valores das outras
                  linhas (achado real ao revisar o Ranking). */}
              <button
                className={e.isSelf ? "invisible" : undefined}
                tabIndex={e.isSelf ? -1 : 0}
                aria-hidden={e.isSelf || undefined}
                onClick={() => !e.isSelf && visit(e.username)}
              >
                Visitar
              </button>
            </div>
          ))}
          {data.selfOutsideTop && (
            <div className="leaderboard-row is-self leaderboard-self-outside">
              <span className="rank">#{data.selfOutsideTop.rank}</span>
              <span className="username">{data.selfOutsideTop.username} (você)</span>
              <span className="value mono">{active.icon ? active.icon : <Coin />}{active.format(Number(data.selfOutsideTop.value))}<small>{active.suffix}</small></span>
              <button className="invisible" tabIndex={-1} aria-hidden="true">Visitar</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
