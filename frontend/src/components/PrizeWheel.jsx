import { useEffect, useState } from "react";

const SPIN_TURNS = 6;
export const SPIN_DURATION_MS = 3400;
const CX = 100;
const CY = 100;
const R = 92;

function polar(angleDeg, r = R) {
  const rad = ((angleDeg - 90) * Math.PI) / 180;
  return { x: CX + r * Math.cos(rad), y: CY + r * Math.sin(rad) };
}

/**
 * 8 fatias: 6 valores de soft (espalhados ao longo da faixa min–max do dia) + Ovo Comum + Ovo
 * Raro. As 8 fatias têm o MESMO tamanho visual (pedido do usuário: "deixe as faixas da roleta
 * iguais", como uma roleta clássica de verdade) — a chance de cada uma NÃO é igual, mas isso é
 * decidido no servidor antes de girar (ver DailyRewardModal.jsx), não pelo tamanho da fatia na
 * roda. Os dois ovos ficam em lados OPOSTOS da roda (índice 0 e 4 de 8 — 180° de distância),
 * não um do lado do outro (pedido do usuário). Os 6 valores de soft NÃO seguem a ordem
 * crescente ao redor da roda (pedido do usuário: "misture a posição dos valores, não quero
 * eles sequenciais") — `SHUFFLE_ORDER` embaralha o índice de `points` que cai em cada posição.
 */
const SHUFFLE_ORDER = [2, 5, 0, 4, 1, 3];

export function buildWheelSlices(min, max) {
  const points = [0, 0.15, 0.3, 0.5, 0.7, 1].map((p) => Math.round(min + p * (max - min)));
  const shuffled = SHUFFLE_ORDER.map((idx) => points[idx]);
  const weights = [1, 1, 1, 1, 1, 1, 1, 1];
  const softSlice = (i, color) => ({ key: `soft-${i}`, kind: "soft", value: shuffled[i], label: `${shuffled[i]}`, color });
  const raw = [
    { key: "egg-common", kind: "egg", eggKey: "egg_common", label: "Ovo Comum", color: "#3f7a6e" },
    softSlice(0, "#1f5a52"),
    softSlice(1, "#286f63"),
    softSlice(2, "#1f5a52"),
    { key: "egg-rare", kind: "egg", eggKey: "egg_rare", label: "Ovo Raro", color: "#4d8fe0" },
    softSlice(3, "#286f63"),
    softSlice(4, "#1f5a52"),
    softSlice(5, "#286f63"),
  ];
  const total = weights.reduce((a, b) => a + b, 0);
  let acc = 0;
  return raw.map((s, i) => {
    const angle = (weights[i] / total) * 360;
    const startAngle = acc;
    acc += angle;
    return { ...s, weight: weights[i], startAngle, angle, endAngle: acc, centerAngle: startAngle + angle / 2 };
  });
}

/** Escolhe em qual fatia a roda deve parar, a partir do resultado REAL já devolvido pela API
 * (moeda + streak calculados como sempre; ovo é uma rolagem independente, §7.10). */
export function pickWheelTargetIndex(slices, claimed) {
  if (claimed.gotEgg) {
    const idx = slices.findIndex((s) => s.kind === "egg" && s.eggKey === claimed.eggItemKey);
    return idx >= 0 ? idx : slices.findIndex((s) => s.kind === "egg");
  }
  let bestIdx = 0;
  let bestDiff = Infinity;
  slices.forEach((s, i) => {
    if (s.kind !== "soft") return;
    const diff = Math.abs(s.value - claimed.amount);
    if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
  });
  return bestIdx;
}

/** Roleta clássica de 8 fatias, giro de verdade — pedido do usuário: "quero uma roleta com
 * alguns prêmios que gire realmente e o prêmio pare na setinha". A seta fica fixa no topo; a
 * roda gira (várias voltas + freada) até o CENTRO da fatia-alvo (com um jitter pequeno pra não
 * cair sempre no dead-center) ficar embaixo da seta. */
export function PrizeWheel({ slices, targetIndex, spinToken }) {
  const [rotation, setRotation] = useState(0);
  const [transitioning, setTransitioning] = useState(false);

  useEffect(() => {
    if (targetIndex == null) return undefined;
    const slice = slices[targetIndex];
    const jitter = (Math.random() - 0.5) * slice.angle * 0.5;
    const effectiveCenter = slice.centerAngle + jitter;
    setTransitioning(true);
    const id = requestAnimationFrame(() => {
      setRotation(SPIN_TURNS * 360 + (360 - effectiveCenter));
    });
    return () => cancelAnimationFrame(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [spinToken]);

  return (
    <div className="prize-wheel-wrap">
      <div className="prize-wheel-pointer" />
      <svg
        viewBox="0 0 200 200"
        className="prize-wheel"
        style={{
          transform: `rotate(${rotation}deg)`,
          transition: transitioning ? `transform ${SPIN_DURATION_MS}ms cubic-bezier(0.13, 0.66, 0.2, 1)` : "none",
        }}
      >
        {slices.map((s) => {
          const p1 = polar(s.startAngle);
          const p2 = polar(s.endAngle);
          const largeArc = s.angle > 180 ? 1 : 0;
          const d = `M ${CX},${CY} L ${p1.x},${p1.y} A ${R},${R} 0 ${largeArc} 1 ${p2.x},${p2.y} Z`;
          const lp = polar(s.centerAngle, R * 0.62);
          return (
            <g key={s.key}>
              <path d={d} fill={s.color} stroke="#0c1a18" strokeWidth="1.5" />
              {s.kind === "egg"
                ? <text x={lp.x} y={lp.y} textAnchor="middle" dominantBaseline="middle" fontSize="16">🥚</text>
                : <text x={lp.x} y={lp.y} textAnchor="middle" dominantBaseline="middle" fontSize="11" fontWeight="700" fill="#eaf6f2">{s.label}</text>}
            </g>
          );
        })}
        <circle cx={CX} cy={CY} r="10" fill="#0c1a18" stroke="var(--gold)" strokeWidth="2" />
      </svg>
    </div>
  );
}
