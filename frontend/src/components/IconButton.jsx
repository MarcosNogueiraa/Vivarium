import { useRef, useState } from "react";

/**
 * Botão-ícone com legenda por long-press (13/08/2026) — `title` só funciona no hover do
 * mouse; em touch não existe hover, então botões só-emoji ficavam sem forma de descobrir o
 * que fazem (feedback do usuário, print de mobile). Segurar ~500ms mostra uma bolha com o
 * rótulo e CANCELA o clique dessa vez (é só descoberta); soltar rápido continua clicando
 * normal, sem mudança de comportamento nenhuma.
 */
export function IconButton({ label, className = "", onClick, children, ...rest }) {
  const [showTip, setShowTip] = useState(false);
  const heldRef = useRef(false);
  const timerRef = useRef(null);

  function startPress() {
    heldRef.current = false;
    timerRef.current = setTimeout(() => { heldRef.current = true; setShowTip(true); }, 500);
  }
  function endPress(e) {
    clearTimeout(timerRef.current);
    if (heldRef.current) { e.preventDefault(); setShowTip(false); }
  }
  function cancelPress() { clearTimeout(timerRef.current); setShowTip(false); }

  return (
    <span className="icon-btn-wrap">
      <button
        className={className} title={label} onClick={onClick}
        onTouchStart={startPress} onTouchEnd={endPress} onTouchMove={cancelPress} onTouchCancel={cancelPress}
        {...rest}
      >
        {children}
      </button>
      {showTip && <span className="icon-btn-tip">{label}</span>}
    </span>
  );
}
