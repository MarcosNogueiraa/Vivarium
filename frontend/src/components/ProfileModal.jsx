import { useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { Modal } from "./Modal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";

/** Editar perfil (14/08/2026): trocar email e trocar senha, cada um exigindo a senha
 * atual — mesmo padrão de qualquer ação sensível já usado no jogo (ex: transferência).
 * Ganhou nível + avatar (18/08/2026, BACKLOG.md #7) — só social/cosmético. */
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

  const [ownedFish, setOwnedFish] = useState(null); // null = ainda carregando
  const [avatarBusy, setAvatarBusy] = useState(false);

  useEffect(() => {
    Promise.all([api.tank(), api.backpack()])
      .then(([tank, backpack]) => {
        // Maior score primeiro (pedido do usuário, 18/08/2026) — facilita achar o peixe
        // mais raro pra usar como avatar sem precisar rolar a lista toda.
        const all = [...tank.creatures, ...backpack.creatures]
          .sort((a, b) => Number(b.rarityScore) - Number(a.rarityScore));
        setOwnedFish(all);
      })
      .catch(() => setOwnedFish([]));
  }, []);

  async function pickAvatar(creatureInstanceId) {
    setAvatarBusy(true);
    try {
      const updated = await api.updateAvatar(creatureInstanceId);
      onUpdated?.(updated);
    } catch (err) {
      notify(err.message);
    } finally {
      setAvatarBusy(false);
    }
  }

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

      {me && (
        <div className="level-bar-wrap" style={{ marginBottom: 16 }}>
          <div className="avatar-picker-current">
            {me.avatar
              ? <FishCanvas creature={me.avatar} width={72} />
              : <div className="avatar-picker-option is-none" style={{ width: 72, height: 72, fontSize: "2rem" }}>👤</div>}
          </div>
          <div className="level-bar-label">Nível <strong>{me.level}</strong> · {me.currentLevelXp}/{me.xpForNextLevel} XP</div>
          <div className="level-bar"><div className="level-bar-fill" style={{ width: `${Math.round(me.progress01 * 100)}%` }} /></div>
        </div>
      )}

      <div className="avatar-section">
        <p className="hint" style={{ padding: 0, marginBottom: 8 }}>Foto de perfil — escolha um peixe seu</p>
        {ownedFish === null ? (
          <p className="hint">Carregando seus peixes…</p>
        ) : (
          <div className="avatar-picker-grid">
            <button
              type="button"
              className={`avatar-picker-option is-none${!me?.avatar ? " is-selected" : ""}`}
              disabled={avatarBusy}
              onClick={() => pickAvatar(null)}
              title="Nenhum"
            >
              👤
            </button>
            {ownedFish.map((c) => (
              <button
                key={c.id}
                type="button"
                className={`avatar-picker-option${me?.avatar?.id === c.id ? " is-selected" : ""}`}
                disabled={avatarBusy}
                onClick={() => pickAvatar(c.id)}
              >
                <FishCanvas creature={c} width={48} />
              </button>
            ))}
          </div>
        )}
      </div>

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
