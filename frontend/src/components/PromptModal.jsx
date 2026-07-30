import { useState } from "react";
import { Modal } from "./Modal.jsx";

/**
 * Modal de entrada (substitui window.prompt): título, um campo e confirmar/cancelar.
 * `onConfirm(value)` pode lançar — o erro aparece inline e o modal continua aberto.
 */
export function PromptModal({
  title, label, defaultValue = "", type = "text", placeholder,
  confirmLabel = "Confirmar", onConfirm, onClose,
}) {
  const [value, setValue] = useState(defaultValue);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  async function submit(e) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await onConfirm(value);
    } catch (err) {
      setError(err.message);
      setBusy(false);
    }
  }

  return (
    <Modal onClose={onClose} narrow>
      <div className="eyebrow">{title}</div>
      <form className="prompt-form" onSubmit={submit}>
        <label className="prompt-label">{label}</label>
        <input
          type={type} value={value} placeholder={placeholder} autoFocus
          min={type === "number" ? 1 : undefined}
          onChange={(e) => setValue(e.target.value)}
        />
        {error && <div className="error">{error}</div>}
        <div className="prompt-actions">
          <button type="button" onClick={onClose}>Cancelar</button>
          <button type="submit" className="btn-primary" disabled={busy || !String(value).trim()}>
            {busy ? "…" : confirmLabel}
          </button>
        </div>
      </form>
    </Modal>
  );
}
