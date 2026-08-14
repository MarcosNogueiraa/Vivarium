import { useState } from "react";
import { getToken } from "./lib/api.js";
import { AuthView } from "./views/AuthView.jsx";
import { GameView } from "./views/GameView.jsx";
import { ResetPasswordView } from "./views/ResetPasswordView.jsx";

export default function App() {
  const [authed, setAuthed] = useState(() => Boolean(getToken()));
  // Sem router: o link do email de redefinição de senha só carrega `?resetToken=...`
  // na URL — checado uma vez aqui, antes de qualquer outra coisa, mesmo se o usuário
  // já estiver logado noutra sessão/aba (o link ainda precisa funcionar).
  const resetToken = new URLSearchParams(window.location.search).get("resetToken");
  if (resetToken) return <ResetPasswordView token={resetToken} />;

  return authed
    ? <GameView onLogout={() => setAuthed(false)} />
    : <AuthView onAuthed={() => setAuthed(true)} />;
}
