import { PART_HEX, PT } from "../lib/fishRenderer.js";

/** Nome de uma cor de parte, escrito NA cor correspondente (18/08/2026, pedido do usuário:
 * "mais fácil visualização"). O texto sozinho ficava ilegível pro Preto (quase a mesma cor
 * do fundo escuro do app, mesmo com glow) — swatch com contorno claro sempre visível,
 * qualquer que seja a cor (18/08/2026, feedback do usuário: "PRETO está difícil de enxergar"). */
export function ColorSpan({ color }) {
  return (
    <span className="color-name">
      <span className="color-swatch" style={{ background: PART_HEX[color] }} />
      <span style={{ color: PART_HEX[color] }}>{PT.color[color]}</span>
    </span>
  );
}
