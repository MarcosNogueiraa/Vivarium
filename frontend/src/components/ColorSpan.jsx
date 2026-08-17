import { PART_HEX, PT } from "../lib/fishRenderer.js";

/** Nome de uma cor de parte, escrito NA cor correspondente (18/08/2026, pedido do usuário:
 * "mais fácil visualização") — glow sutil garante contraste mesmo pra cores escuras
 * (ex: Preto) sobre o fundo escuro do app. */
export function ColorSpan({ color }) {
  return (
    <span className="color-name" style={{ color: PART_HEX[color] }}>
      {PT.color[color]}
    </span>
  );
}
