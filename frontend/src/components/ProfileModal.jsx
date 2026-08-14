import { useState } from "react";
import { api } from "../lib/api.js";
import { Modal } from "./Modal.jsx";

/** Editar perfil (14/08/2026): trocar email e trocar senha, cada um exigindo a senha
 * atual — mesmo padrão de qualquer ação sensível já usado no jogo (ex: transferência). */
export function ProfileModal({ me, onClose, onUpdated, notify }) {
  const [email, setEmail] = useState(me?.email ?? "");
  const [emailPassword, setEmailPassword] = useState("");
  const [emailBusy, setEmailBusy] = useState(false);
  const [emailError, setEmailError] = useState(null);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [passwordBusy, setPasswordBusy] = useState(false);
  const [passwordError, setPasswordError] = useState(null);

  async function submitEmail(e) {
    e.preventDefault();
    setEmailBusy(true);
    setEmailError(null);
    try {
      const updated = await api.updateEmail(email.trim(), emailPassword);
      setEmailPassword("");
      onUpdated?.(updated);
      notify("Email atualizado.");
    } catch (err) {
      setEmailError(err.message);
    } finally {
      setEmailBusy(false);
    }
  }

  async function submitPassword(e) {
    e.preventDefault();
    setPasswordError(null);
    if (newPassword !== confirmPassword) {
      setPasswordError("As duas senhas novas não coincidem.");
      return;
    }
    setPasswordBusy(true);
    try {
      await api.updatePassword(currentPassword, newPassword);
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
      notify("Senha atualizada.");
    } catch (err) {
      setPasswordError(err.message);
    } finally {
      setPasswordBusy(false);
    }
  }

  return (
    <Modal onClose={onClose}>
      <div className="eyebrow">Editar perfil</div>

      <form className="prompt-form" onSubmit={submitEmail}>
        <p className="hint" style={{ padding: 0, marginBottom: 4 }}>Trocar email</p>
        <label className="prompt-label">Novo email</label>
        <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
        <label className="prompt-label">Senha atual (pra confirmar)</label>
        <input
          type="password" value={emailPassword} onChange={(e) => setEmailPassword(e.target.value)}
          required autoComplete="current-password" placeholder="Sua senha atual"
        />
        {emailError && <div className="error">{emailError}</div>}
        <div className="prompt-actions">
          <button type="submit" className="btn-primary" disabled={emailBusy || !email.trim() || !emailPassword}>
            {emailBusy ? "…" : "Salvar email"}
          </button>
        </div>
      </form>

      <div className="detail-section">
        <form className="prompt-form" onSubmit={submitPassword}>
          <p className="hint" style={{ padding: 0, marginBottom: 4 }}>Trocar senha</p>
          <label className="prompt-label">Senha atual</label>
          <input
            type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)}
            required autoComplete="current-password"
          />
          <label className="prompt-label">Nova senha (mínimo 8 caracteres)</label>
          <input
            type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
            required minLength={8} autoComplete="new-password"
          />
          <label className="prompt-label">Confirmar nova senha</label>
          <input
            type="password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)}
            required minLength={8} autoComplete="new-password"
          />
          {passwordError && <div className="error">{passwordError}</div>}
          <div className="prompt-actions">
            <button
              type="submit" className="btn-primary"
              disabled={passwordBusy || !currentPassword || !newPassword || !confirmPassword}
            >
              {passwordBusy ? "…" : "Salvar senha"}
            </button>
          </div>
        </form>
      </div>
    </Modal>
  );
}
