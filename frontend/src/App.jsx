import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, clearToken, getToken, setToken } from "./api.js";
import { generateTraits, roll01 } from "./generator.js";
import {
  bandOf, drawFish, drawTankBackground, drawTankForeground,
  PT, swimSpeedOf, VIEW_H, VIEW_W,
} from "./fishRenderer.js";

const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;

// Centro aproximado do peixe nas coordenadas do renderizador (pra girar/espelhar)
const FISH_CX = 290;
const FISH_CY = 210;

function AquariumCanvas({ creatures, selectedId, onSelect, interactive = true, ambient = false }) {
  const W = 960;
  const H = 480;
  const SCALE = 0.34;
  const canvasRef = useRef(null);
  const statesRef = useRef(new Map());
  const creaturesRef = useRef([]);
  const selectedRef = useRef(null);

  creaturesRef.current = useMemo(
    () => creatures.map((c) => {
      const bigSeed = BigInt(c.seed);
      return { ...c, bigSeed, traits: generateTraits(bigSeed) };
    }),
    [creatures],
  );
  selectedRef.current = selectedId;

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    let raf;
    let last = performance.now();

    function frame(now) {
      const dt = reducedMotion ? 0 : Math.min((now - last) / 1000, 0.1);
      last = now;
      const time = reducedMotion ? 1 : now; // 1 (não 0) pra o ambiente aparecer estático

      ctx.setTransform(1, 0, 0, 1, 0, 0);
      drawTankBackground(ctx, W, H, time);

      for (const c of creaturesRef.current) {
        let s = statesRef.current.get(c.id);
        if (!s) {
          s = {
            x: 120 + roll01(c.bigSeed, "pos_x") * (W - 240),
            y: 100 + roll01(c.bigSeed, "pos_y") * (H - 200),
            vx: (roll01(c.bigSeed, "dir") < 0.5 ? -1 : 1) * swimSpeedOf(c.traits),
            phase: roll01(c.bigSeed, "phase") * Math.PI * 2,
          };
          statesRef.current.set(c.id, s);
        }

        s.x += s.vx * dt;
        if (s.x < 90) { s.x = 90; s.vx = Math.abs(s.vx); }
        if (s.x > W - 90) { s.x = W - 90; s.vx = -Math.abs(s.vx); }
        const y = s.y + Math.sin(time / 900 + s.phase) * 7;

        if (c.id === selectedRef.current) {
          ctx.save();
          ctx.strokeStyle = "rgba(84, 230, 209, 0.9)";
          ctx.lineWidth = 2;
          ctx.shadowColor = "rgba(84, 230, 209, 0.8)";
          ctx.shadowBlur = 16;
          ctx.beginPath();
          ctx.ellipse(s.x, y + 36, 64, 15, 0, 0, Math.PI * 2);
          ctx.stroke();
          ctx.restore();
        }

        ctx.save();
        ctx.translate(s.x, y);
        ctx.scale(s.vx > 0 ? -SCALE : SCALE, SCALE);
        ctx.translate(-FISH_CX, -FISH_CY);
        drawFish(ctx, c.bigSeed, c.traits, time, s.phase);
        ctx.restore();
      }

      drawTankForeground(ctx, W, H, time);

      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }

    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, []);

  function handleClick(e) {
    if (!interactive) return;
    const rect = canvasRef.current.getBoundingClientRect();
    const px = (e.clientX - rect.left) * (W / rect.width);
    const py = (e.clientY - rect.top) * (H / rect.height);
    const hit = creaturesRef.current.find((c) => {
      const s = statesRef.current.get(c.id);
      return s && Math.abs(px - s.x) < 70 && Math.abs(py - s.y) < 55;
    });
    onSelect(hit ? hit.id : null);
  }

  return (
    <canvas
      ref={canvasRef} width={W} height={H}
      className={`aquarium${ambient ? " ambient" : ""}`}
      onClick={interactive ? handleClick : undefined}
      role="img" aria-label="Aquário com seus peixes"
    />
  );
}

const HEARTBEAT_MS = 60_000; // CLAUDE.md 8.3
const TANK_REFRESH_MS = 30_000;

function FishCanvas({ seed, width = 220 }) {
  const canvasRef = useRef(null);
  const height = Math.round(width * (VIEW_H / VIEW_W));
  const bigSeed = useMemo(() => BigInt(seed), [seed]);
  const traits = useMemo(() => generateTraits(bigSeed), [bigSeed]);

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    let raf;
    function frame(now) {
      const time = reducedMotion ? 0 : now;
      ctx.setTransform(width / VIEW_W, 0, 0, width / VIEW_W, 0, 0);
      ctx.clearRect(0, 0, VIEW_W, VIEW_H);
      drawFish(ctx, bigSeed, traits, time);
      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }
    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, [bigSeed, traits, width]);

  return <canvas ref={canvasRef} width={width} height={height} className="fish-canvas" />;
}

function RarityBadge({ score }) {
  const band = bandOf(score);
  return (
    <span className="badge" style={{ "--tier": band.color }}>
      <span className="gem" /> {band.name} · {score.toFixed(1)}
    </span>
  );
}

function ShimmerLabel({ seed }) {
  const traits = useMemo(() => generateTraits(BigInt(seed)), [seed]);
  if (traits.shimmerTier === "None") return null;
  return (
    <span className="shimmer-label">
      ✦ {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}
    </span>
  );
}

function Coin() { return <span className="coin" aria-hidden="true" />; }

function randomDemoSeed() {
  return String(Math.floor(Math.random() * 9_007_199_254_740_991));
}

function AuthView({ onAuthed }) {
  const [mode, setMode] = useState("login");
  const [form, setForm] = useState({ username: "", email: "", password: "" });
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const demoFish = useMemo(
    () => Array.from({ length: 6 }, (_, i) => ({ id: i, seed: randomDemoSeed() })),
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
          <input
            type="password" placeholder="Senha (mínimo 8 caracteres)"
            value={form.password} onChange={set("password")} required minLength={8}
            autoComplete={mode === "login" ? "current-password" : "new-password"}
          />
          {error && <div className="error">{error}</div>}
          <button type="submit" className="btn-primary" disabled={busy}>
            {busy ? "…" : mode === "login" ? "Mergulhar" : "Criar meu aquário"}
          </button>
        </form>
      </div>
    </div>
  );
}

function TankView({ tank, refresh, notify }) {
  const [selectedId, setSelectedId] = useState(null);
  const selected = tank.creatures.find((c) => c.id === selectedId) ?? null;
  const lowWater = Number(tank.maintenanceLevel) < 40;

  async function collect(itemId) {
    try { await api.collect(itemId); await refresh(); }
    catch (err) { notify(err.message); }
  }
  async function devSpawn() {
    try { await api.devSpawn(); await refresh(); }
    catch (err) { notify(err.message); }
  }
  async function devClear() {
    try {
      const { removed } = await api.devClear();
      setSelectedId(null);
      notify(`${removed} peixe(s) removido(s).`);
      await refresh();
    } catch (err) { notify(err.message); }
  }
  async function buyFilter() {
    try { await api.buyItem("filter_basic"); notify("Água restaurada!"); await refresh(); }
    catch (err) { notify(err.message); }
  }
  async function sell(creature) {
    const price = window.prompt("Preço em moeda soft:", "50");
    if (!price) return;
    try {
      await api.createListing(creature.id, Number(price));
      notify("Peixe listado no mercado.");
      setSelectedId(null);
      await refresh();
    } catch (err) { notify(err.message); }
  }
  async function transfer(creature) {
    const toUsername = window.prompt("Transferir para qual jogador (username)?");
    if (!toUsername) return;
    try {
      await api.transferCreature(creature.id, toUsername.trim());
      notify(`Transferido para ${toUsername.trim()}.`);
      setSelectedId(null);
      await refresh();
    } catch (err) { notify(err.message); }
  }

  return (
    <div className="tank-layout">
      <div className="tank-stage">
        <div className="tank-hud">
          <span className={`status-pill ${tank.online ? "on" : "off"}`}>
            <span className="led" />{tank.online ? "Online" : "Offline"}
          </span>
          <span className="spacer" style={{ flex: 1 }} />
          <span className="water-gauge">
            <span className="label">Água</span>
            <span className="water-track">
              <span className={`water-fill${lowWater ? " low" : ""}`} style={{ width: `${tank.maintenanceLevel}%` }} />
            </span>
            <span className="val">{Number(tank.maintenanceLevel).toFixed(0)}</span>
          </span>
          <button onClick={buyFilter} title="Restaura a qualidade da água pra 100">Filtro · 20</button>
        </div>

        <AquariumCanvas creatures={tank.creatures} selectedId={selectedId} onSelect={setSelectedId} />

        {tank.creatures.length === 0 && (
          <div className="tank-empty">
            <strong>Seu aquário está esperando</strong>
            <span className="muted">Colete um peixe da fila para começar.</span>
          </div>
        )}
      </div>

      {selected ? (
        <div className="selected-bar glass">
          <RarityBadge score={Number(selected.rarityScore)} />
          <ShimmerLabel seed={selected.seed} />
          <span className="faint mono">seed {selected.seed}</span>
          <span className="spacer" />
          <button onClick={() => sell(selected)}>Vender no mercado</button>
          <button onClick={() => transfer(selected)}>Transferir</button>
        </div>
      ) : (
        tank.creatures.length > 0 && (
          <p className="hint">Clique num peixe para ver detalhes, vender ou transferir.</p>
        )
      )}

      <section>
        <div className="section-head">
          <span className="eyebrow">Fila de criação</span>
          <span className="count">{tank.queue.length}/{tank.queueCap}</span>
          <span className="spacer" />
          {import.meta.env.DEV && (
            <button className="dev-btn" onClick={devSpawn} title="Só existe em dev">Gerar peixe (dev)</button>
          )}
          {import.meta.env.DEV && tank.creatures.length > 0 && (
            <button className="dev-btn" onClick={devClear} title="Só existe em dev">Limpar (dev)</button>
          )}
        </div>
        {tank.queue.length === 0 && <p className="hint">Nada na fila ainda — os peixes surgem com o tempo.</p>}
        <div className="queue">
          {tank.queue.map((item) => (
            <div key={item.id} className={`queue-item glass ${item.isSick ? "sick" : ""}`}>
              <span className="q-label">
                <span>{item.isSick ? "Doente" : "Pronto"}</span>
                <small>{item.isSick ? "raridade reduzida" : "aguardando coleta"}</small>
              </span>
              <button className="btn-primary" disabled={!item.isReady} onClick={() => collect(item.id)}>
                Coletar
              </button>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

function MarketView({ userId, refreshTank, notify }) {
  const [listings, setListings] = useState(null);

  const refresh = useCallback(async () => {
    setListings(await api.listings());
  }, []);

  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  async function buy(listing) {
    try {
      await api.buyListing(listing.id);
      notify(`Comprado por ${listing.priceSoft} soft!`);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }
  async function cancel(listing) {
    try { await api.cancelListing(listing.id); await Promise.all([refresh(), refreshTank()]); }
    catch (err) { notify(err.message); }
  }

  if (listings === null) return <p className="hint">Carregando mercado…</p>;
  if (listings.length === 0) return <p className="hint">Nenhum peixe à venda no momento — seja o primeiro a listar.</p>;

  return (
    <div className="grid">
      {listings.map((l) => (
        <div key={l.id} className="card" style={{ "--tier": bandOf(Number(l.rarityScore)).color }}>
          <div className="fish-stage"><FishCanvas seed={l.seed} /></div>
          <div className="card-row">
            <RarityBadge score={Number(l.rarityScore)} />
            <span className="price"><Coin />{Number(l.priceSoft).toFixed(0)}</span>
          </div>
          <ShimmerLabel seed={l.seed} />
          <div className="card-row">
            <span className="seller">de {l.sellerName}</span>
            {l.sellerId === userId
              ? <button onClick={() => cancel(l)}>Cancelar</button>
              : <button className="btn-primary" onClick={() => buy(l)}>Comprar</button>}
          </div>
        </div>
      ))}
    </div>
  );
}

function StoreView({ refreshTank, notify }) {
  const [items, setItems] = useState(null);

  const refresh = useCallback(async () => { setItems(await api.items()); }, []);
  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  async function buy(item) {
    try {
      await api.buyItem(item.key);
      notify(`${item.name} comprado!`);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }

  const DESCRIPTIONS = {
    filter_basic: "Restaura a qualidade da água para 100 na hora.",
    auto_filter: "Permanente: a água degrada na metade da velocidade.",
    tank_upgrade: "+1 de capacidade no tanque (preço sobe a cada compra).",
  };

  if (items === null) return <p className="hint">Carregando loja…</p>;

  return (
    <div className="grid">
      {items.map((item) => (
        <div key={item.key} className="card store-card">
          <strong>{item.name}</strong>
          <p className="muted">{DESCRIPTIONS[item.key] ?? ""}</p>
          <div className="card-row">
            <span className="price"><Coin />{Number(item.price).toFixed(0)}</span>
            {item.owned
              ? <span className="owned">Adquirido ✓</span>
              : <button className="btn-primary" onClick={() => buy(item)}>Comprar</button>}
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

  const refreshTank = useCallback(async () => { setTank(await api.tank()); }, []);

  async function devCoins() {
    try { await api.devCoins(1000); notify("+1000 fichas"); await refreshTank(); }
    catch (err) { notify(err.message); }
  }

  useEffect(() => {
    try {
      setUserId(Number(JSON.parse(atob(getToken().split(".")[1])).sub));
    } catch { /* token malformado cai no 401 do fetch */ }

    const beat = () => api.heartbeat().then(refreshTank).catch(() => {});
    beat();
    const heartbeatTimer = setInterval(beat, HEARTBEAT_MS);
    const tankTimer = setInterval(() => refreshTank().catch(() => {}), TANK_REFRESH_MS);
    return () => { clearInterval(heartbeatTimer); clearInterval(tankTimer); };
  }, [refreshTank]);

  if (tank === null) return <div className="loading">Enchendo o aquário…</div>;

  const soft = Number(tank.wallet?.SOFT ?? 0);

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand"><span className="dot" />Vivarium <small>aquário vivo</small></div>
        <span className="spacer" />
        <nav className="nav-pills">
          <button className={tab === "tank" ? "active" : ""} onClick={() => setTab("tank")}>Tanque</button>
          <button className={tab === "market" ? "active" : ""} onClick={() => setTab("market")}>Mercado</button>
          <button className={tab === "store" ? "active" : ""} onClick={() => setTab("store")}>Loja</button>
        </nav>
        <span className="spacer" />
        <span className="wallet-chip"><Coin />{soft.toFixed(0)} <small>soft</small></span>
        {import.meta.env.DEV && (
          <button className="dev-btn" onClick={devCoins} title="Só existe em dev">+1000 fichas</button>
        )}
        <button onClick={() => { clearToken(); onLogout(); }}>Sair</button>
      </header>

      <main className="content">
        {tab === "tank" && <TankView tank={tank} refresh={refreshTank} notify={notify} />}
        {tab === "market" && <MarketView userId={userId} refreshTank={refreshTank} notify={notify} />}
        {tab === "store" && <StoreView refreshTank={refreshTank} notify={notify} />}
      </main>

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
