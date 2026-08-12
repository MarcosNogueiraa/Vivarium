import { Modal } from "../components/Modal.jsx";
import { BANDS, PART_HEX, PT } from "../lib/fishRenderer.js";
import { CONFIG } from "../lib/generator.js";
import { RARITY_RANGES } from "../lib/format.js";

const fmtWeight = (w) => (Number.isInteger(w) ? w.toFixed(0) : String(w));
const GRADIENT_MIX_LABEL = {
  BaseDominant: "Base predominante",
  Even: "Equilibrado (50/50)",
  PatternDominant: "Padrão predominante",
};

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

      <div className="eyebrow" style={{ marginTop: 22 }}>Raridade do brilho (corpo)</div>
      <p className="muted guide-intro">
        O corpo é sempre desenhado sobre a mesma base cinza — o que varia é uma camada de
        brilho por cima. É o trait que mais pesa no score (2.5× o peso dos demais), então
        raramente um peixe sem brilho chega perto de raro.
      </p>
      <div className="guide-bands">
        {CONFIG.shimmerTiers.map(([key, weight]) => (
          <div className="guide-band" key={key}>
            <span className="gb-name">{PT.tier[key]}</span>
            <span className="gb-range mono">{fmtWeight(weight)}%</span>
          </div>
        ))}
      </div>
      <p className="faint guide-foot">
        Brilho vibrante, raro ou lendário puxa a cor mais parecida com o tom do brilho pra
        cima nas 3 partes (cauda/dorsal/peitoral) — mas isso torna essa cor específica
        <i> mais comum</i>, não mais rara, pra esse peixe; as demais cores ficam ainda mais
        raras em compensação.
      </p>

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

      <div className="eyebrow" style={{ marginTop: 22 }}>Degradê — mistura de duas cores</div>
      <p className="muted guide-intro">
        O Degradê é especial: além de sortear a cor de base e a cor do padrão, ele também sorteia
        <b> como as duas se misturam</b> visualmente na parte.
      </p>
      <div className="guide-bands">
        {CONFIG.gradientMixRatios.map(([key, weight]) => (
          <div className="guide-band" key={key}>
            <span className="gb-name">{GRADIENT_MIX_LABEL[key]}</span>
            <span className="gb-range mono">{fmtWeight(weight)}%</span>
          </div>
        ))}
      </div>
      <p className="faint guide-foot">
        Quando uma cor domina a outra, só ela conta pra raridade — a minoritária fica de fora do
        cálculo. No equilibrado (o mais raro dos três), as duas cores contam juntas.
      </p>

      <div className="eyebrow" style={{ marginTop: 22 }}>Bônus de conjunto</div>
      <p className="muted guide-intro">
        Peixes com as 3 partes combinando ganham pontos extras no score, direto — não é probabilidade,
        é um bônus fixo por parecer um "conjunto" visualmente coeso.
      </p>
      <div className="guide-bands">
        <div className="guide-band">
          <span className="gb-name">Mesmo padrão em 2 partes</span>
          <span className="gb-range mono">+{CONFIG.setBonus.samePattern2.toFixed(1)}</span>
        </div>
        <div className="guide-band">
          <span className="gb-name">Mesmo padrão nas 3 partes</span>
          <span className="gb-range mono">+{CONFIG.setBonus.samePattern3.toFixed(1)}</span>
        </div>
        <div className="guide-band">
          <span className="gb-name">Mesma cor em 2 partes</span>
          <span className="gb-range mono">+{CONFIG.setBonus.sameColor2.toFixed(1)}</span>
        </div>
        <div className="guide-band">
          <span className="gb-name">Mesma cor nas 3 partes</span>
          <span className="gb-range mono">+{CONFIG.setBonus.sameColor3.toFixed(1)}</span>
        </div>
      </div>

      <div className="eyebrow" style={{ marginTop: 22 }}>Velocidade de nado</div>
      <p className="muted guide-intro">
        Velocidade de cauda e de nadadeira também são sorteadas — só os extremos (muito lenta ou muito
        rápida, abaixo de {CONFIG.movement.speedExtremeLow.toFixed(0)} ou acima de{" "}
        {CONFIG.movement.speedExtremeHigh.toFixed(0)}, numa escala de 0 a 100) entram no score, com peso
        reduzido ({CONFIG.movement.scoreWeight.toFixed(1)}×). Cauda mais rápida também deixa o peixe
        mais ágil no aquário — é o mesmo valor, não é só estética.
      </p>

      <p className="faint guide-foot">Abra qualquer peixe para ver o detalhamento “por que é raro”.</p>
    </Modal>
  );
}
