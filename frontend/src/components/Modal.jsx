import { useEffect } from "react";
import { createPortal } from "react-dom";

/** Casca de modal: backdrop (clique fecha), Esc fecha, botão de fechar. */
export function Modal({ onClose, narrow = false, className = "", children }) {
  useEffect(() => {
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  // Portal pra `document.body` (11/08/2026) — bug real achado na revisão mobile:
  // `.tank-layout` (aba Tanque) tem `isolation: isolate` (pro efeito "cinema",
  // pra conter o brilho z-index:-2 do `::before` sem vazar pro resto da página —
  // ver comentário em styles.css). `isolation` cria um stacking context LOCAL —
  // qualquer modal renderizado como filho de `.tank-layout` (FishDetail,
  // ConfirmModal, etc., todos abertos a partir da aba Tanque) fica PRESO nesse
  // contexto, mesmo sendo `position:fixed` com z-index alto: por fora,
  // `.tank-layout` inteiro compete pela ordem de pintura como uma unidade só
  // (z-index efetivo 0, por não ter z-index próprio), e perde pro `.topbar`
  // (z-index 20, ficou mais alto ainda no cabeçalho mobile novo, 3 linhas) —
  // sempre que o modal ficava perto do topo da tela (comum em viewport curto),
  // o topbar desenhava POR CIMA do ×, tornando-o inclicável. Renderizar via
  // portal tira o modal inteiro da árvore de `.tank-layout` — escapa de
  // qualquer stacking context/overflow de ancestral, presente ou futuro,
  // não só desse caso específico.
  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className={`modal glass${narrow ? " narrow" : ""}${className ? ` ${className}` : ""}`}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
      >
        {/* `.modal-close` fica fora de `.modal-body` (a única parte que rola) —
            assim continua alcançável mesmo com o conteúdo maior que a tela
            (bug real 11/08/2026: em mobile o × rolava junto com o conteúdo e
            sumia, o modal virava uma "página infinita" sem borda perceptível). */}
        <button className="modal-close" onClick={onClose} aria-label="Fechar">×</button>
        <div className="modal-body">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
