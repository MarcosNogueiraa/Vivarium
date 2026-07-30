import { Modal } from "../components/Modal.jsx";
import { BANDS } from "../lib/fishRenderer.js";
import { RARITY_RANGES } from "../lib/format.js";

export function RarityGuide({ onClose }) {
  return (
    <Modal onClose={onClose} narrow>
      <div className="eyebrow">Como funciona a raridade</div>
      <p className="muted guide-intro">
        Cada peixe nasce de um seed único. Todo atributo — brilho do corpo, cor e padrão de cada parte,
        velocidade de nado — é sorteado com uma probabilidade. Quanto mais improvável o conjunto, maior a
        raridade; e quanto mais raro, mais moedas o peixe produz por hora.
      </p>
      <div className="guide-bands">
        {BANDS.map((b, i) => (
          <div className="guide-band" key={b.name}>
            <span className="gem" style={{ background: b.color, boxShadow: `0 0 10px ${b.color}` }} />
            <span className="gb-name" style={{ color: b.color }}>{b.name}</span>
            <span className="gb-range mono">{RARITY_RANGES[i]}</span>
          </div>
        ))}
      </div>
      <p className="faint guide-foot">Abra qualquer peixe para ver o detalhamento “por que é raro”.</p>
    </Modal>
  );
}
