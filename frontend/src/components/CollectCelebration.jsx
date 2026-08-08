import { useState } from "react";
import { Modal } from "./Modal.jsx";
import { FishCanvas, REVEAL_STEP_COUNT } from "./FishCanvas.jsx";
import { Coin } from "./Coin.jsx";
import { PeekAnchor } from "./PeekPanel.jsx";
import { coinsPerHourOf, rarityBreakdownOf, traitsOf } from "../lib/generator.js";
import { bandOf, PT } from "../lib/fishRenderer.js";
import { partSummary } from "../lib/format.js";

// Revelação suspense (08/08/2026, pedido do usuário; estendida a Raro+ e ao
// Ninho no mesmo dia; depois trocada de automática pra CLIQUE-A-CLIQUE, a
// pedido do usuário): peixe/filhote Raro+ (score ≥ 7.5, mesmo corte de
// BANDS/§5 que já abre a celebração) é MONTADO na tela parte a parte — cada
// clique no peixe desenha uma parte a mais de verdade no canvas (corpo, que
// já começa visível como "base", → brilho → cauda → dorsal → peitoral), via
// `FishCanvas revealStep` (novo prop, `fishRenderer.drawFish` ganhou um
// parâmetro `layers` pra desenhar só um subconjunto das partes). Raridade
// final some atrás de "???" até a última parte. No Ninho (`variant=
// "breeding"`), só o FILHOTE em si passa pela revelação — os retratos dos
// pais e a seção de despedida (se algum não sobreviveu) continuam visíveis
// desde o início, já que eles não são o "mistério" do momento.
const REVEAL_LABELS = [
  { key: "shimmer", label: "Corpo" },
  { key: "tail", label: "Cauda" },
  { key: "dorsal", label: "Nadadeira dorsal" },
  { key: "pectoral", label: "Nadadeira peitoral" },
];

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

  // Raro+ (BANDS/§5) — no Ninho o filhote também revela (pais/despedida
  // continuam visíveis na hora, só o filhote em si é o "mistério").
  const suspense = score >= 7.5;
  const [step, setStep] = useState(0);
  const revealed = !suspense || step >= REVEAL_STEP_COUNT;
  function revealNext() { if (!revealed) setStep((s) => Math.min(s + 1, REVEAL_STEP_COUNT)); }
  const tierColor = revealed ? band.color : "var(--muted)";

  // Pontos de raridade por parte revelada (pedido do usuário) — soma os
  // fatores de `rarityBreakdownOf` (mesmo cálculo de "por que é raro" do
  // FishDetail) que pertencem a cada grupo. `part: null` = fatores do corpo
  // (shimmerTier) ou de conjunto (samePattern/sameColor, mostrados à parte
  // como bônus só no reveal final, já que dependem das 3 partes juntas).
  const breakdown = rarityBreakdownOf(creature);
  const pointsOf = (pred) => breakdown.factors.filter(pred).reduce((sum, f) => sum + f.points, 0);
  const bonusPoints = pointsOf((f) => f.key === "samePattern" || f.key === "sameColor");
  const attrLines = [
    { key: "shimmer", label: "Corpo", value: traits.shimmerTier === "None" ? "Cinza, sem brilho" : `${PT.tier[traits.shimmerTier]} · ${PT.shimmer[traits.shimmerColor]}`, points: pointsOf((f) => f.key === "shimmerTier") },
    { key: "tail", label: "Cauda", value: partSummary(traits.tail), points: pointsOf((f) => f.part === "tail") },
    { key: "dorsal", label: "Nadadeira dorsal", value: partSummary(traits.dorsal), points: pointsOf((f) => f.part === "dorsal") },
    { key: "pectoral", label: "Nadadeira peitoral", value: partSummary(traits.pectoral), points: pointsOf((f) => f.part === "pectoral" || f.part === "fin") },
  ];

  function showPeek(e, c) {
    const rect = e.currentTarget.getBoundingClientRect();
    setPeek({ x: rect.left + rect.width / 2, y: rect.top, creature: c });
  }
  function hidePeek() { setPeek(null); }

  return (
    <Modal onClose={onClose} narrow className="celebrate">
      <div className="celebrate-rays" style={{ "--tier": tierColor }} aria-hidden="true" />
      <div className="celebrate-body">
        <div className="eyebrow" style={{ color: tierColor }}>
          {!revealed ? "✨ Abrindo peixe raro…" : isBreeding ? "🐣 Seu filhote nasceu!" : legendary ? "✦ Lendário! ✦" : "Peixe raro coletado"}
        </div>
        <div
          className={`celebrate-fish${suspense && !revealed ? " tap-to-reveal" : ""}`}
          style={{ "--tier": tierColor }}
          onClick={suspense && !revealed ? revealNext : undefined}
        >
          <FishCanvas
            seed={creature.seed} width={220} isBred={creature.isBred}
            parentASeed={creature.parentASeed} parentBSeed={creature.parentBSeed}
            revealStep={suspense ? step : null}
          />
        </div>

        {suspense && !revealed && (
          <>
            <div className="reveal-attrs">
              {attrLines.slice(0, step).map((a) => (
                <div className="reveal-attr" key={a.key}>
                  <b>{a.label}:</b> {a.value} <span className="reveal-pts">+{a.points.toFixed(2)}</span>
                </div>
              ))}
            </div>
            <p className="faint" style={{ fontSize: "0.8rem" }}>
              toque no peixe pra revelar {REVEAL_LABELS[step]?.label.toLowerCase()}
            </p>
          </>
        )}
        {revealed && suspense && (
          <div className="reveal-attrs revealed">
            {attrLines.map((a) => (
              <div className="reveal-attr" key={a.key}>
                <b>{a.label}:</b> {a.value} <span className="reveal-pts">+{a.points.toFixed(2)}</span>
              </div>
            ))}
            {bonusPoints > 0 && (
              <div className="reveal-attr"><b>Conjunto combinando:</b> <span className="reveal-pts">+{bonusPoints.toFixed(2)}</span></div>
            )}
            <div className="reveal-attr reveal-total"><b>Score total:</b> {breakdown.total.toFixed(2)}</div>
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
