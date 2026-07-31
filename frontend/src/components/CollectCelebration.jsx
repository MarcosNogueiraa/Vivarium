import { Modal } from "./Modal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";
import { Coin } from "./Coin.jsx";
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

/**
 * Momento de celebração ao coletar um peixe raro+ (score ≥ 7.5) do tanque, ou
 * SEMPRE ao coletar um filhote do Ninho (`variant="breeding"`) — é um evento
 * demorado (horas/dias de gestação), merece o mesmo peso mesmo se o filhote
 * não saiu raro. `deadParents` (opcional): os pais (creature completo) que não
 * sobreviveram — mostrados em despedida, não só como uma contagem abstrata.
 */
export function CollectCelebration({ creature, onClose, variant = "tank", deadParents = [] }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  const coins = coinsPerHourOf(score);
  const legendary = score >= 14.0;
  const isBreeding = variant === "breeding";
  const hasLoss = deadParents.length > 0;

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
    </Modal>
  );
}
