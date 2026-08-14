import { useState } from "react";
import { api } from "../lib/api.js";

/** Aberta via link do email (`?resetToken=...`, sem router — só um parâmetro na URL,
 * checado em App.jsx antes de decidir entre AuthView/GameView). Sem estado de auth
 * nenhum — o token já autoriza a troca, não precisa estar logado. */
export function ResetPasswordView({ token }) {
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [done, setDone] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setError(null);
    if (newPassword !== confirmPassword) {
      setError("As duas senhas não coincidem.");
      return;
    }
    setBusy(true);
    try {
      await api.resetPassword(token, newPassword);
      setDone(true);
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  function goToLogin() {
    // Sem router: só limpa o parâmetro da URL e recarrega a tela de auth normal.
    window.location.href = window.location.pathname;
  }

  return (
    <div className="auth-hero">
      <div className="auth-card glass">
        <div className="brand"><span className="dot" />Vivarium</div>
        {done ? (
          <>
            <p className="hint" style={{ padding: 0 }}>Senha redefinida! Já dá pra entrar com a senha nova.</p>
            <button type="button" className="btn-primary" onClick={goToLogin} style={{ marginTop: 12 }}>
              Ir pro login
            </button>
          </>
        ) : (
          <>
            <p className="tagline">Escolha uma senha nova pra sua conta.</p>
            <form onSubmit={submit}>
              <input
                type="password" placeholder="Nova senha (mínimo 8 caracteres)" value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)} required minLength={8} autoComplete="new-password" autoFocus
              />
              <input
                type="password" placeholder="Confirmar nova senha" value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)} required minLength={8} autoComplete="new-password"
              />
              {error && <div className="error">{error}</div>}
              <button type="submit" className="btn-primary" disabled={busy}>
                {busy ? "…" : "Redefinir senha"}
              </button>
            </form>
            <button type="button" className="link-btn" onClick={goToLogin} style={{ marginTop: 12 }}>
              ← Voltar pro login
            </button>
          </>
        )}
      </div>
    </div>
  );
}
