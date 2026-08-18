import { PART_HEX, PT } from "../lib/fishRenderer.js";

/** Nome de uma cor de parte (18/08/2026, pedido do usuário: "mais fácil visualização").
 * Testando SÓ a bolinha com a cor real + contorno claro (sempre visível, resolve o Preto
 * de vez) e texto na cor normal — versão anterior colorindo o próprio texto ficava
 * ilegível pro Preto mesmo com glow. */
export function ColorSpan({ color }) {
  return (
    <span className="color-name">
      <span className="color-swatch" style={{ background: PART_HEX[color] }} />
      {PT.color[color]}
    </span>
  );
}
