import { traitsOf } from "../lib/generator.js";
import { bandOf, PT } from "../lib/fishRenderer.js";
import { partSummary } from "../lib/format.js";

/** Janela flutuante com raridade + traits resumidos de uma criatura. */
export function PeekPanel({ creature }) {
  const traits = traitsOf(creature);
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  return (
    <div className="peek-card">
      <div className="peek-title" style={{ color: band.color }}>{band.name} · {score.toFixed(2)}</div>
      <div className="peek-row">
        {traits.shimmerTier === "None" ? "Corpo sem brilho" : `${PT.tier[traits.shimmerTier]} · ${PT.shimmer[traits.shimmerColor]}`}
      </div>
      <div className="peek-row">Cauda: {partSummary(traits.tail)}</div>
      <div className="peek-row">Dorsal: {partSummary(traits.dorsal)}</div>
      <div className="peek-row">Nadadeira peitoral: {partSummary(traits.pectoral)}</div>
    </div>
  );
}

/** Ancora o PeekPanel numa posição fixa da tela (ex: acima do elemento sob o mouse). */
export function PeekAnchor({ x, y, creature }) {
  return (
    <div className="peek-anchor" style={{ left: x, top: y }}>
      <PeekPanel creature={creature} />
    </div>
  );
}
