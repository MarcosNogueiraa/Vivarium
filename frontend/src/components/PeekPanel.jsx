import { createPortal } from "react-dom";
import { traitsOf } from "../lib/generator.js";
import { bandOf, PT } from "../lib/fishRenderer.js";
import { partSummary, PART_PT } from "../lib/format.js";

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
      <div className="peek-row">{PART_PT.tail}: {partSummary(traits.tail)}</div>
      <div className="peek-row">{PART_PT.dorsal}: {partSummary(traits.dorsal)}</div>
      <div className="peek-row">{PART_PT.pectoral}: {partSummary(traits.pectoral)}</div>
    </div>
  );
}

/**
 * Ancora o PeekPanel numa posição fixa da tela (ex: acima do elemento sob o mouse).
 * Portal pra `document.body` (12/08/2026, bug real: dentro de `CollectCelebration`, esse
 * painel é filho de `.modal.celebrate`, que tem `overflow:hidden` pros raios de luz da
 * animação — mesmo sendo `position:fixed`, um ancestral com overflow:hidden ainda corta a
 * pintura de um descendente fixed que continua no mesmo DOM. Clicar no pai registrava o
 * clique (o estado `peek` mudava), só o painel nunca aparecia. Mesmo princípio já usado em
 * `Modal.jsx` pro modal em si.
 */
export function PeekAnchor({ x, y, creature }) {
  return createPortal(
    <div className="peek-anchor" style={{ left: x, top: y }}>
      <PeekPanel creature={creature} />
    </div>,
    document.body,
  );
}
