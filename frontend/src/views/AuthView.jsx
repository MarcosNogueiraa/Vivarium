import { useMemo, useState } from "react";
import { AquariumCanvas } from "../components/AquariumCanvas.jsx";
import { api, setToken } from "../lib/api.js";
import { generateTraits } from "../lib/generator.js";

function randomDemoSeed() {
  return String(Math.floor(Math.random() * 9_007_199_254_740_991));
}

export function AuthView({ onAuthed }) {
  const [mode, setMode] = useState("login");
  const [form, setForm] = useState({ username: "", email: "", password: "" });
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  // Decorativo só (nenhum peixe real) — traits congelados no nascimento (13/08/2026) tiraram
  // o fallback de derivar do seed em traitsOf, então precisa vir pronto aqui, igual a API manda.
  const demoFish = useMemo(
    () => Array.from({ length: 6 }, (_, i) => {
      const seed = randomDemoSeed();
      return { id: i, seed, traits: generateTraits(BigInt(seed)) };
    }),
    [],
  );

  async function submit(e) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const auth = mode === "login"
        ? await api.login(form.username, form.password)
        : await api.register(form.username, form.email, form.password);
      setToken(auth.token);
      onAuthed();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  return (
    <div className="auth-hero">
      <AquariumCanvas creatures={demoFish} selectedId={null} onSelect={() => {}} interactive={false} ambient />
      <div className="auth-card glass">
        <div className="brand"><span className="dot" />Vivarium</div>
        <p className="tagline">Seu aquário vivo cultiva peixes raros — mesmo enquanto você trabalha.</p>
        <div className="segmented">
          <button className={mode === "login" ? "active" : ""} onClick={() => setMode("login")} type="button">Entrar</button>
          <button className={mode === "register" ? "active" : ""} onClick={() => setMode("register")} type="button">Criar conta</button>
        </div>
        <form onSubmit={submit}>
          <input
            placeholder={mode === "login" ? "Username ou email" : "Username"}
            value={form.username} onChange={set("username")} required autoComplete="username"
          />
          {mode === "register" && (
            <input type="email" placeholder="Email" value={form.email} onChange={set("email")} required autoComplete="email" />
          )}
          <div className="password-field">
            <input
              type={showPassword ? "text" : "password"} placeholder="Senha (mínimo 8 caracteres)"
              value={form.password} onChange={set("password")} required minLength={8}
              autoComplete={mode === "login" ? "current-password" : "new-password"}
            />
            <button
              type="button" className="password-toggle"
              onClick={() => setShowPassword((s) => !s)}
              title={showPassword ? "Ocultar senha" : "Mostrar senha"}
              aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
            >
              {showPassword ? "🙈" : "👁️"}
            </button>
          </div>
          {error && <div className="error">{error}</div>}
          <button type="submit" className="btn-primary" disabled={busy}>
            {busy ? "…" : mode === "login" ? "Mergulhar" : "Criar meu aquário"}
          </button>
        </form>
      </div>
    </div>
  );
}
