import { useMemo } from "react";
import { Modal } from "../components/Modal.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Coin } from "../components/Coin.jsx";
import { TraitRow } from "../components/TraitRow.jsx";
import { CollapsibleSection } from "../components/CollapsibleSection.jsx";
import { ColorSpan } from "../components/ColorSpan.jsx";
import { coinsPerHourOf, rarityBreakdownOf, traitsOf, waterDegradationPerFishPerHour } from "../lib/generator.js";
import { bandOf, PT, swimSpeedOf } from "../lib/fishRenderer.js";
import { ageOf, factorLabel, speedWord, PART_PT } from "../lib/format.js";

/** Mesmo resumo de `partSummary` (format.js), mas com a cor base e a cor do padrão escritas
 * na própria cor (18/08/2026, pedido do usuário) — precisa ser JSX, por isso vive aqui e não
 * em format.js (que é propositalmente "sem React", ver CLAUDE.md §12). */
function PartSummary({ part }) {
  if (part.pattern === "None") return <><ColorSpan color={part.color} /> · sem padrão</>;
  return (
    <>
      <ColorSpan color={part.color} /> · {PT.pattern[part.pattern].toLowerCase()} <ColorSpan color={part.patternColor} />
      {` (tam ${part.patternSize.toFixed(0)}, op ${part.patternOpacity.toFixed(0)}%)`}
    </>
  );
}

export function FishDetail({ creature, onClose, children, inTank = false, bandFactor = 1 }) {
  const seed = creature.seed;
  const traits = useMemo(() => traitsOf(creature), [creature]);
  const breakdown = useMemo(() => rarityBreakdownOf(creature), [creature]);
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  const coins = coinsPerHourOf(score);
  const maxPoints = Math.max(...breakdown.factors.map((f) => f.points), 0.001);

  return (
    <Modal onClose={onClose}>
      <div className="detail-head">
        <div className="detail-fish">
          <FishCanvas creature={creature} width={280} />
        </div>
        <div className="detail-meta">
          <span className="badge big" style={{ "--tier": band.color }}><span className="gem" /> {band.name}</span>
          {creature.isBred && <span className="bred-tag">🐣 Filhote (nascido do ninho)</span>}
          <div className="detail-score">Raridade <b>{score.toFixed(2)}</b></div>
          <div className="detail-coins"><Coin /> ~{coins.toFixed(1)} <small>soft/h a água cheia</small></div>
          {traits.shimmerTier !== "None" && (
            <div className="shimmer-label">✦ {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}</div>
          )}
          {inTank && (
            <div className="faint water-impact">
              💧 −{waterDegradationPerFishPerHour(score, bandFactor).toFixed(1)} água/h <small>(peixes mais raros sujam mais rápido)</small>
            </div>
          )}
          <div className="faint mono">seed {seed}{ageOf(creature.createdAt) ? ` · ${ageOf(creature.createdAt)}` : ""}</div>
        </div>
      </div>

      <CollapsibleSection title="Atributos" defaultOpen>
        <TraitRow label="Corpo" value={traits.shimmerTier === "None"
          ? "Cinza, sem brilho"
          : `${PT.tier[traits.shimmerTier]} · ${PT.shimmer[traits.shimmerColor]} ${traits.shimmerOpacity.toFixed(0)}%`} />
        <TraitRow label={PART_PT.tail} value={<PartSummary part={traits.tail} />} />
        <TraitRow label={PART_PT.dorsal} value={<PartSummary part={traits.dorsal} />} />
        <TraitRow label={PART_PT.pectoral} value={<PartSummary part={traits.pectoral} />} />
        <TraitRow label="Movimento" value={`cauda ${speedWord(traits.movement.tailSpeed)}, `
          + `nadadeira ${speedWord(traits.movement.finSpeed)} · nado ${swimSpeedOf(traits).toFixed(0)} px/s`} />
      </CollapsibleSection>

      <CollapsibleSection title="Por que é raro">
        <p className="bd-help">Cada atributo soma pontos conforme quão improvável é. Quanto mais raro o conjunto, maior o score.</p>
        <div className="breakdown">
          {breakdown.factors.map((f, i) => (
            <div className="bd-row" key={i}>
              <span className="bd-label">{factorLabel(f)}</span>
              <span className="bd-bar"><span style={{ width: `${(f.points / maxPoints) * 100}%` }} /></span>
              <span className="bd-prob mono">{f.probPct == null ? "—" : `${f.probPct < 1 ? f.probPct.toFixed(1) : f.probPct.toFixed(0)}%`}</span>
              <span className="bd-points mono">+{f.points.toFixed(2)}</span>
            </div>
          ))}
        </div>
        <div className="bd-total">Score total <b>{breakdown.total.toFixed(2)}</b></div>
      </CollapsibleSection>

      {children && <div className="detail-actions">{children}</div>}
    </Modal>
  );
}
