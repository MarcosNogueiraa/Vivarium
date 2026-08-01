import { useState } from "react";
import { Modal } from "./Modal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";
import { Coin } from "./Coin.jsx";
import { PeekAnchor } from "./PeekPanel.jsx";
import { coinsPerHourOf } from "../lib/generator.js";
import { bandOf } from "../lib/fishRenderer.js";

/** Um pai que não sobreviveu à gestação — retrato + mensagem de despedida. */
function Farewell({ creature }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  return (
    <div className="farewell-fish">
      <div className="farewell-portrait" style={{ "--tier": band.color }}>
        <FishCanvas seed={creature.seed} width={120} isBred={creature.isBred} parentASeed={creature.parentASeed} parentBSeed={creature.parentBSeed} />
      </div>
      <span className="badge" style={{ "--tier": band.color }}>
        <span className="gem" /> {band.name} · {score.toFixed(1)}
      </span>
    </div>
  );
}

/** Retrato pequeno de um pai/mãe na celebração; hover mostra as estatísticas (PeekPanel). */
function ParentChip({ creature, dead, onEnter, onLeave }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  return (
    <div
      className={`parent-chip${dead ? " parent-chip-dead" : ""}`}
      style={{ "--tier": band.color }}
      onMouseEnter={onEnter}
      onMouseLeave={onLeave}
    >
      <FishCanvas seed={creature.seed} width={64} isBred={creature.isBred} parentASeed={creature.parentASeed} parentBSeed={creature.parentBSeed} />
    </div>
  );
}

/**
 * Momento de celebração ao coletar um peixe raro+ (score ≥ 7.5) do tanque, ou
 * SEMPRE ao coletar um filhote do Ninho (`variant="breeding"`) — é um evento
 * demorado (horas/dias de gestação), merece o mesmo peso mesmo se o filhote
 * não saiu raro. `deadParents` (opcional): os pais (creature completo) que não
 * sobreviveram — mostrados em despedida, não só como uma contagem abstrata.
 * `parentA`/`parentB` (opcional, breeding): os pais, vivos ou mortos — pequenos
 * retratos hoverable com as estatísticas, sempre visíveis nesta tela.
 */
export function CollectCelebration({ creature, onClose, variant = "tank", deadParents = [], parentA = null, parentB = null }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  const coins = coinsPerHourOf(score);
  const legendary = score >= 14.0;
  const isBreeding = variant === "breeding";
  const hasLoss = deadParents.length > 0;
  const parents = [parentA, parentB].filter(Boolean);
  const [peek, setPeek] = useState(null);

  function showPeek(e, c) {
    const rect = e.currentTarget.getBoundingClientRect();
    setPeek({ x: rect.left + rect.width / 2, y: rect.top, creature: c });
  }
  function hidePeek() { setPeek(null); }

  return (
    <Modal onClose={onClose} narrow className="celebrate">
      <div className="celebrate-rays" style={{ "--tier": band.color }} aria-hidden="true" />
      <div className="celebrate-body">
        <div className="eyebrow" style={{ color: band.color }}>
          {isBreeding ? "🐣 Seu filhote nasceu!" : legendary ? "✦ Lendário! ✦" : "Peixe raro coletado"}
        </div>
        <div className="celebrate-fish" style={{ "--tier": band.color }}>
          <FishCanvas seed={creature.seed} width={220} isBred={creature.isBred} parentASeed={creature.parentASeed} parentBSeed={creature.parentBSeed} />
        </div>
        <span className="badge big" style={{ "--tier": band.color }}>
          <span className="gem" /> {band.name} · {score.toFixed(1)}
        </span>
        <div className="detail-coins"><Coin /> ~{coins.toFixed(1)} <small>soft/h a água cheia</small></div>

        {isBreeding && parents.length > 0 && (
          <div className="parents-row">
            <span className="eyebrow" style={{ fontSize: "0.7rem" }}>Pais</span>
            <div className="parents-chips">
              {parents.map((p) => (
                <ParentChip
                  key={p.id}
                  creature={p}
                  dead={deadParents.some((d) => d.id === p.id)}
                  onEnter={(e) => showPeek(e, p)}
                  onLeave={hidePeek}
                />
              ))}
            </div>
          </div>
        )}

        {hasLoss && (
          <div className="farewell-section">
            <div className="farewell-divider" />
            <p className="farewell-title">
              🕊️ {deadParents.length === 2 ? "Despedida" : "Uma despedida"}
            </p>
            <p className="bd-help" style={{ textAlign: "center" }}>
              {deadParents.length === 2
                ? "Nenhum dos pais resistiu à gestação — mas a linhagem segue no filhote."
                : "Um dos pais não resistiu à gestação — mas a linhagem segue no filhote."}
            </p>
            <div className="farewell-row">
              {deadParents.map((p) => <Farewell key={p.id} creature={p} />)}
            </div>
          </div>
        )}

        <button className="btn-primary" onClick={onClose}>Maravilha!</button>
      </div>
      {peek && <PeekAnchor x={peek.x} y={peek.y} creature={peek.creature} />}
    </Modal>
  );
}
