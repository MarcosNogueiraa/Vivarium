import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, clearToken, getToken, setToken } from "./api.js";
import { generateTraits } from "./generator.js";
import { bandOf, drawFish, PT, VIEW_H, VIEW_W } from "./fishRenderer.js";

const HEARTBEAT_MS = 60_000; // CLAUDE.md 8.3
const TANK_REFRESH_MS = 30_000;

function FishCanvas({ seed, width = 240 }) {
  const canvasRef = useRef(null);
  const height = Math.round(width * (VIEW_H / VIEW_W));
  const bigSeed = useMemo(() => BigInt(seed), [seed]);
  const traits = useMemo(() => generateTraits(bigSeed), [bigSeed]);

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    ctx.setTransform(width / VIEW_W, 0, 0, width / VIEW_W, 0, 0);
    ctx.clearRect(0, 0, VIEW_W, VIEW_H);
    drawFish(ctx, bigSeed, traits);
  }, [bigSeed, traits, width]);

  return <canvas ref={canvasRef} width={width} height={height} className="fish-canvas" />;
}

function RarityBadge({ score }) {
  const band = bandOf(score);
  return (
    <span className="badge" style={{ background: band.color }}>
      {band.name} · {score.toFixed(1)}
    </span>
  );
}

function ShimmerLabel({ seed }) {
  const traits = useMemo(() => generateTraits(BigInt(seed)), [seed]);
  if (traits.shimmerTier === "None") return null;
  return (
    <div className="shimmer-label">
      {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}
    </div>
  );
}

function AuthView({ onAuthed }) {
  const [mode, setMode] = useState("login");
  const [form, setForm] = useState({ username: "", email: "", password: "" });
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

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
    <div className="auth">
      <h1>Vivarium</h1>
      <p className="sub">Seu aquário gera peixes enquanto você trabalha.</p>
      <div className="tabs">
        <button className={mode === "login" ? "active" : ""} onClick={() => setMode("login")}>Entrar</button>
        <button className={mode === "register" ? "active" : ""} onClick={() => setMode("register")}>Criar conta</button>
      </div>
      <form onSubmit={submit}>
        <input
          placeholder={mode === "login" ? "Username ou email" : "Username"}
          value={form.username} onChange={set("username")} required
        />
        {mode === "register" && (
          <input type="email" placeholder="Email" value={form.email} onChange={set("email")} required />
        )}
        <input
          type="password" placeholder="Senha (mínimo 8 caracteres)"
          value={form.password} onChange={set("password")} required minLength={8}
        />
        {error && <div className="error">{error}</div>}
        <button type="submit" className="primary" disabled={busy}>
          {mode === "login" ? "Entrar" : "Criar conta"}
        </button>
      </form>
    </div>
  );
}

function TankView({ tank, refresh, notify }) {
  async function collect(itemId) {
    try {
      await api.collect(itemId);
      await refresh();
    } catch (err) {
      notify(err.message);
    }
  }

  async function devSpawn() {
    try {
      await api.devSpawn();
      await refresh();
    } catch (err) {
      notify(err.message);
    }
  }

  async function buyFilter() {
    try {
      await api.buyItem("filter_basic");
      notify("Água restaurada!");
      await refresh();
    } catch (err) {
      notify(err.message);
    }
  }

  async function sell(creature) {
    const price = window.prompt("Preço em moeda soft:", "50");
    if (!price) return;
    try {
      await api.createListing(creature.id, Number(price));
      notify("Peixe listado no mercado.");
      await refresh();
    } catch (err) {
      notify(err.message);
    }
  }

  return (
    <>
      <section className="status-row">
        <span className={`pill ${tank.online ? "on" : "off"}`}>
          {tank.online ? "Online" : "Offline"}
        </span>
        <div className="water">
          <label>Qualidade da água</label>
          <div className="water-bar">
            <div style={{ width: `${tank.maintenanceLevel}%` }} className={tank.maintenanceLevel < 40 ? "low" : ""} />
          </div>
          <span>{Number(tank.maintenanceLevel).toFixed(0)}</span>
        </div>
        <span className="wallet">🪙 {Number(tank.wallet.SOFT ?? 0).toFixed(0)} soft</span>
        <button onClick={buyFilter} title="Restaura a qualidade da água pra 100">
          🧽 Filtro (20)
        </button>
      </section>

      <section>
        <h2>
          Fila de geração ({tank.queue.length}/{tank.queueCap})
          {import.meta.env.DEV && (
            <button className="dev-btn" onClick={devSpawn} title="Só existe em dev">
              🐣 Gerar peixe (dev)
            </button>
          )}
        </h2>
        {tank.queue.length === 0 && <p className="muted">Nada na fila ainda — os peixes surgem com o tempo.</p>}
        <div className="queue">
          {tank.queue.map((item) => (
            <div key={item.id} className={`queue-item ${item.isSick ? "sick" : ""}`}>
              <span>{item.isSick ? "🤒 Doente" : "🐟 Pronto"}</span>
              <button className="primary" disabled={!item.isReady} onClick={() => collect(item.id)}>
                Coletar
              </button>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2>Tanque ({tank.creatures.length}/{tank.capacity})</h2>
        {tank.creatures.length === 0 && <p className="muted">Tanque vazio. Colete da fila!</p>}
        <div className="grid">
          {tank.creatures.map((c) => (
            <div key={c.id} className="card">
              <FishCanvas seed={c.seed} />
              <ShimmerLabel seed={c.seed} />
              <div className="card-row">
                <RarityBadge score={Number(c.rarityScore)} />
                <button onClick={() => sell(c)}>Vender</button>
              </div>
            </div>
          ))}
        </div>
      </section>
    </>
  );
}

function MarketView({ userId, refreshTank, notify }) {
  const [listings, setListings] = useState(null);

  const refresh = useCallback(async () => {
    setListings(await api.listings());
  }, []);

  useEffect(() => {
    refresh().catch((err) => notify(err.message));
  }, [refresh, notify]);

  async function buy(listing) {
    try {
      await api.buyListing(listing.id);
      notify(`Comprado por ${listing.priceSoft} soft!`);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) {
      notify(err.message);
    }
  }

  async function cancel(listing) {
    try {
      await api.cancelListing(listing.id);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) {
      notify(err.message);
    }
  }

  if (listings === null) return <p className="muted">Carregando mercado…</p>;
  if (listings.length === 0) return <p className="muted">Nenhum peixe à venda no momento.</p>;

  return (
    <div className="grid">
      {listings.map((l) => (
        <div key={l.id} className="card">
          <FishCanvas seed={l.seed} />
          <ShimmerLabel seed={l.seed} />
          <div className="card-row">
            <RarityBadge score={Number(l.rarityScore)} />
            <span className="price">🪙 {Number(l.priceSoft).toFixed(0)}</span>
          </div>
          <div className="card-row">
            <span className="muted">de {l.sellerName}</span>
            {l.sellerId === userId
              ? <button onClick={() => cancel(l)}>Cancelar</button>
              : <button className="primary" onClick={() => buy(l)}>Comprar</button>}
          </div>
        </div>
      ))}
    </div>
  );
}

function StoreView({ refreshTank, notify }) {
  const [items, setItems] = useState(null);

  const refresh = useCallback(async () => {
    setItems(await api.items());
  }, []);

  useEffect(() => {
    refresh().catch((err) => notify(err.message));
  }, [refresh, notify]);

  async function buy(item) {
    try {
      await api.buyItem(item.key);
      notify(`${item.name} comprado!`);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) {
      notify(err.message);
    }
  }

  const DESCRIPTIONS = {
    filter_basic: "Restaura a qualidade da água pra 100 na hora.",
    auto_filter: "Permanente: a água degrada na metade da velocidade.",
    tank_upgrade: "+1 de capacidade no tanque (preço sobe a cada compra).",
  };

  if (items === null) return <p className="muted">Carregando loja…</p>;

  return (
    <div className="grid">
      {items.map((item) => (
        <div key={item.key} className="card store-card">
          <strong>{item.name}</strong>
          <p className="muted">{DESCRIPTIONS[item.key] ?? ""}</p>
          <div className="card-row">
            <span className="price">🪙 {Number(item.price).toFixed(0)}</span>
            {item.owned
              ? <span className="muted">Adquirido ✓</span>
              : <button className="primary" onClick={() => buy(item)}>Comprar</button>}
          </div>
        </div>
      ))}
    </div>
  );
}

function GameView({ onLogout }) {
  const [tank, setTank] = useState(null);
  const [tab, setTab] = useState("tank");
  const [toast, setToast] = useState(null);
  const [userId, setUserId] = useState(null);

  const notify = useCallback((message) => {
    setToast(message);
    setTimeout(() => setToast(null), 4000);
  }, []);

  const refreshTank = useCallback(async () => {
    setTank(await api.tank());
  }, []);

  useEffect(() => {
    // sub do JWT = id do usuário (pra saber quais listagens são minhas)
    try {
      setUserId(Number(JSON.parse(atob(getToken().split(".")[1])).sub));
    } catch { /* token malformado cai no 401 do fetch */ }

    const beat = () => api.heartbeat().then(refreshTank).catch(() => {});
    beat();
    const heartbeatTimer = setInterval(beat, HEARTBEAT_MS);
    const tankTimer = setInterval(() => refreshTank().catch(() => {}), TANK_REFRESH_MS);
    return () => {
      clearInterval(heartbeatTimer);
      clearInterval(tankTimer);
    };
  }, [refreshTank]);

  if (tank === null) return <p className="muted center">Carregando aquário…</p>;

  return (
    <div className="game">
      <header>
        <h1>Vivarium</h1>
        <nav className="tabs">
          <button className={tab === "tank" ? "active" : ""} onClick={() => setTab("tank")}>Tanque</button>
          <button className={tab === "market" ? "active" : ""} onClick={() => setTab("market")}>Mercado</button>
          <button className={tab === "store" ? "active" : ""} onClick={() => setTab("store")}>Loja</button>
        </nav>
        <button onClick={() => { clearToken(); onLogout(); }}>Sair</button>
      </header>
      {tab === "tank" && <TankView tank={tank} refresh={refreshTank} notify={notify} />}
      {tab === "market" && <MarketView userId={userId} refreshTank={refreshTank} notify={notify} />}
      {tab === "store" && <StoreView refreshTank={refreshTank} notify={notify} />}
      {toast && <div className="toast">{toast}</div>}
    </div>
  );
}

export default function App() {
  const [authed, setAuthed] = useState(() => Boolean(getToken()));
  return authed
    ? <GameView onLogout={() => setAuthed(false)} />
    : <AuthView onAuthed={() => setAuthed(true)} />;
}
