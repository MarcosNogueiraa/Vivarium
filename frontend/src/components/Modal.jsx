import { useEffect } from "react";

/** Casca de modal: backdrop (clique fecha), Esc fecha, botão de fechar. */
export function Modal({ onClose, narrow = false, className = "", children }) {
  useEffect(() => {
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className={`modal glass${narrow ? " narrow" : ""}${className ? ` ${className}` : ""}`}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
      >
        <button className="modal-close" onClick={onClose} aria-label="Fechar">×</button>
        {children}
      </div>
    </div>
  );
}
