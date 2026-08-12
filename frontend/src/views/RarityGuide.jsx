import { Modal } from "../components/Modal.jsx";
import { BANDS, PART_HEX, PT } from "../lib/fishRenderer.js";
import { CONFIG } from "../lib/generator.js";
import { RARITY_RANGES } from "../lib/format.js";

const fmtWeight = (w) => (Number.isInteger(w) ? w.toFixed(0) : String(w));

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

      <div className="eyebrow" style={{ marginTop: 22 }}>Raridade das cores</div>
      <p className="muted guide-intro">
        Cauda, nadadeira dorsal e nadadeira peitoral sorteiam a cor de forma independente, cada uma nesta
        mesma tabela. Quanto menor o percentual, mais rara — e mais valorizada no mercado — a cor.
      </p>
      <div className="guide-bands">
        {CONFIG.partColors.map(([key, weight]) => (
          <div className="guide-band" key={key}>
            <span className="gem" style={{ background: PART_HEX[key], boxShadow: `0 0 10px ${PART_HEX[key]}` }} />
            <span className="gb-name">{PT.color[key]}</span>
            <span className="gb-range mono">{weight.toFixed(0)}%</span>
          </div>
        ))}
      </div>
      <p className="faint guide-foot">
        Se o corpo do peixe sair com brilho vibrante, raro ou lendário, a cor mais parecida com a do
        brilho fica temporariamente mais provável nas 3 partes — o jogo tenta formar peixes com um
        "conjunto" visualmente combinando.
      </p>

      <div className="eyebrow" style={{ marginTop: 22 }}>Raridade dos padrões</div>
      <p className="muted guide-intro">
        Cada parte também sorteia, de forma independente, um padrão sobre a cor de base — a maioria dos
        peixes não tem nenhum. Quanto menor o percentual, mais raro (e mais valorizado) o padrão.
      </p>
      <div className="guide-bands">
        {CONFIG.patternTypes.map(([key, weight]) => (
          <div className="guide-band" key={key}>
            <span className="gb-name">{PT.pattern[key]}</span>
            <span className="gb-range mono">{fmtWeight(weight)}%</span>
          </div>
        ))}
      </div>
      <p className="faint guide-foot">
        Quando há padrão, o tamanho e a opacidade também são sorteados — valores muito extremos (muito
        pequeno/grande, ou muito fraco/forte) contam como raros no cálculo.
      </p>

      <p className="faint guide-foot">Abra qualquer peixe para ver o detalhamento “por que é raro”.</p>
    </Modal>
  );
}
