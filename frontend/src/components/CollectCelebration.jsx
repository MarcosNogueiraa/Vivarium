import { useEffect, useState } from "react";
import { Modal } from "./Modal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";
import { Coin } from "./Coin.jsx";
import { PeekAnchor } from "./PeekPanel.jsx";
import { coinsPerHourOf, traitsOf } from "../lib/generator.js";
import { bandOf, PT } from "../lib/fishRenderer.js";
import { partSummary } from "../lib/format.js";

// Revelação suspense (08/08/2026, pedido do usuário): peixe Épico+ (score ≥
// 9.8, mesmo corte de BANDS/§5) coletado do tanque revela um atributo por vez
// em vez de tudo de uma vez — corpo/brilho → cauda → dorsal → peitoral →
// raridade final. Clicar a qualquer momento pula pro final. Só na coleta do
// tanque (`variant="tank"`) — o Ninho já tem seu próprio ritmo de celebração
// (pais, despedida) e sempre mostra a tela, raro ou não.
const REVEAL_STEPS = ["shimmer", "tail", "dorsal", "pectoral"];
const REVEAL_DELAY_MS = 900;

function useSuspenseReveal(active) {
  const [step, setStep] = useState(active ? 0 : REVEAL_STEPS.length + 1);
  useEffect(() => {
    if (!active || step > REVEAL_STEPS.length) return;
    const t = setTimeout(() => setStep((s) => s + 1), REVEAL_DELAY_MS);
    return () => clearTimeout(t);
  }, [active, step]);
  const done = step > REVEAL_STEPS.length;
  return { step, done, skip: () => setStep(REVEAL_STEPS.length + 1) };
}

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
  const traits = traitsOf(creature);

  const suspense = !isBreeding && score >= 9.8; // Épico+ (BANDS/§5)
  const { step: revealStep, done: revealed, skip: skipReveal } = useSuspenseReveal(suspense);
  const tierColor = revealed ? band.color : "var(--muted)";
  const attrLines = [
    { key: "shimmer", label: "Corpo", value: traits.shimmerTier === "None" ? "Cinza, sem brilho" : `${PT.tier[traits.shimmerTier]} · ${PT.shimmer[traits.shimmerColor]}` },
    { key: "tail", label: "Cauda", value: partSummary(traits.tail) },
    { key: "dorsal", label: "Nadadeira dorsal", value: partSummary(traits.dorsal) },
    { key: "pectoral", label: "Nadadeira peitoral", value: partSummary(traits.pectoral) },
  ];

  function showPeek(e, c) {
    const rect = e.currentTarget.getBoundingClientRect();
    setPeek({ x: rect.left + rect.width / 2, y: rect.top, creature: c });
  }
  function hidePeek() { setPeek(null); }

  return (
    <Modal onClose={onClose} narrow className="celebrate">
      <div className="celebrate-rays" style={{ "--tier": tierColor }} aria-hidden="true" />
      <div className={`celebrate-body${suspense && !revealed ? " is-revealing" : ""}`} onClick={suspense && !revealed ? skipReveal : undefined}>
        <div className="eyebrow" style={{ color: tierColor }}>
          {!revealed ? "✨ Abrindo peixe raro…" : isBreeding ? "🐣 Seu filhote nasceu!" : legendary ? "✦ Lendário! ✦" : "Peixe raro coletado"}
        </div>
        <div className={`celebrate-fish${suspense && !revealed ? " mystery" : ""}`} style={{ "--tier": tierColor }}>
          <FishCanvas seed={creature.seed} width={220} isBred={creature.isBred} parentASeed={creature.parentASeed} parentBSeed={creature.parentBSeed} />
        </div>

        {suspense && !revealed && (
          <>
            <div className="reveal-attrs">
              {attrLines.slice(0, revealStep).map((a) => (
                <div className="reveal-attr" key={a.key}><b>{a.label}:</b> {a.value}</div>
              ))}
            </div>
            <p className="faint" style={{ fontSize: "0.8rem" }}>toque pra revelar tudo</p>
          </>
        )}
        {revealed && suspense && (
          <div className="reveal-attrs revealed">
            {attrLines.map((a) => <div className="reveal-attr" key={a.key}><b>{a.label}:</b> {a.value}</div>)}
          </div>
        )}
        {revealed ? (
          <>
            <span className="badge big reveal-pop" style={{ "--tier": band.color }}>
              <span className="gem" /> {band.name} · {score.toFixed(1)}
            </span>
            <div className="detail-coins"><Coin /> ~{coins.toFixed(1)} <small>soft/h a água cheia</small></div>
          </>
        ) : (
          <span className="badge big mystery-badge" style={{ "--tier": "#3a5560" }}><span className="gem" /> ???</span>
        )}

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

        {revealed && <button className="btn-primary" onClick={onClose}>Maravilha!</button>}
      </div>
      {peek && <PeekAnchor x={peek.x} y={peek.y} creature={peek.creature} />}
    </Modal>
  );
}
