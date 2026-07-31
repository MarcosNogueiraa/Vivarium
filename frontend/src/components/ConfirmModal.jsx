import { useState } from "react";
import { Modal } from "./Modal.jsx";

/**
 * Modal de confirmação simples (mensagem + confirmar/cancelar). `onConfirm()` pode
 * lançar — o erro aparece inline e o modal continua aberto (mesmo padrão do PromptModal).
 */
export function ConfirmModal({ title, message, confirmLabel = "Confirmar", danger = false, onConfirm, onClose }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  async function confirm() {
    setBusy(true);
    setError(null);
    try {
      await onConfirm();
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} narrow>
      <div className="eyebrow">{title}</div>
      <p className="prompt-label">{message}</p>
      {error && <div className="error">{error}</div>}
      <div className="prompt-actions">
        <button type="button" onClick={onClose}>Cancelar</button>
        <button type="button" className={danger ? "btn-danger" : "btn-primary"} disabled={busy} onClick={confirm}>
          {busy ? "…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}
