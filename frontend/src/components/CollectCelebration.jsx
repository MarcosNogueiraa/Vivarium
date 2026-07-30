import { Modal } from "./Modal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";
import { Coin } from "./Coin.jsx";
import { coinsPerHourOf } from "../lib/generator.js";
import { bandOf } from "../lib/fishRenderer.js";

/**
 * Momento de celebração ao coletar um peixe raro+ (score ≥ 7.5) do tanque, ou
 * SEMPRE ao coletar um filhote do Ninho (`variant="breeding"`) — é um evento
 * demorado (horas/dias de gestação), merece o mesmo peso mesmo se o filhote
 * não saiu raro. `parentLosses` (opcional): quais pais não sobreviveram.
 */
export function CollectCelebration({ creature, onClose, variant = "tank", parentLosses = [] }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  const coins = coinsPerHourOf(score);
  const legendary = score >= 14.0;
  const isBreeding = variant === "breeding";

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
        {parentLosses.length > 0 && (
          <p className="bd-help" style={{ textAlign: "center" }}>
            🕊️ {parentLosses.length === 2 ? "Nenhum dos pais sobreviveu" : "Um dos pais não sobreviveu"} à gestação.
          </p>
        )}
        <button className="btn-primary" onClick={onClose}>Maravilha!</button>
      </div>
    </Modal>
  );
}
