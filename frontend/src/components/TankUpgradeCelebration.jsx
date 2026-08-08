import { Modal } from "./Modal.jsx";
import { decorTierOf } from "../lib/fishRenderer.js";

const FLAVOR = {
  1: "Mais espaço, mais peixes raros combinando no mesmo tanque.",
  2: "O tanque dos sonhos — o topo da capacidade do aquário.",
};

// Mesma paleta de raridade já usada em toda a UI (--r-raro/--r-lendario, styles.css)
// pra reforçar a mesma progressão visual que o jogador já reconhece.
const ACCENT = { 1: "#4d8fe0", 2: "#f0b93b" };

/** Celebração única ao cruzar de faixa de capacidade do tanque (CLAUDE.md §8.16). */
export function TankUpgradeCelebration({ bandName, onClose }) {
  const tier = decorTierOf(bandName);
  const color = ACCENT[tier] ?? "#54e6d1";

  return (
    <Modal onClose={onClose} narrow className="celebrate">
      <div className="celebrate-rays" style={{ "--tier": color }} aria-hidden="true" />
      <div className="celebrate-body">
        <div className="eyebrow" style={{ color }}>🐠 Seu aquário cresceu!</div>
        <span className="badge big" style={{ "--tier": color }}>
          <span className="gem" /> {bandName}
        </span>
        <p className="bd-help" style={{ textAlign: "center" }}>{FLAVOR[tier] ?? "Seu tanque está maior agora."}</p>
        <button className="btn-primary" onClick={onClose}>Uhul!</button>
      </div>
    </Modal>
  );
}
