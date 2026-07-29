import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, clearToken, getToken, setToken } from "./api.js";
import { coinsPerHourOf, generateTraits, rarityBreakdown, roll01, synergyMultiplier } from "./generator.js";
import {
  BANDS, bandOf, drawFish, drawTankBackground, drawTankForeground,
  PART_HEX, PT, swimSpeedOf, VIEW_H, VIEW_W,
} from "./fishRenderer.js";

const PART_PT = { tail: "Cauda", dorsal: "Nadadeira dorsal", pectoral: "Barbatana peitoral" };

// Rótulo humano de um fator do breakdown de raridade
function factorLabel(f) {
  switch (f.key) {
    case "shimmerTier":
      return f.value === "None" ? "Corpo sem brilho" : `Brilho ${PT.tier[f.value].toLowerCase()}`;
    case "partColor":
      return `${PART_PT[f.part]}: ${PT.color[f.value]}`;
    case "patternType":
      return `${PART_PT[f.part]}: ${PT.pattern[f.value].toLowerCase()}`;
    case "patternColor":
      return `Cor do padrão (${PART_PT[f.part].toLowerCase()}): ${PT.color[f.value]}`;
    case "patternSizeExtreme":
      return `Padrão ${f.value} (${PART_PT[f.part].toLowerCase()})`;
    case "patternOpacityExtreme":
      return `Opacidade ${f.value} do padrão (${PART_PT[f.part].toLowerCase()})`;
    case "speedExtreme":
      return `${f.part === "tail" ? "Cauda" : "Nadadeira"} ${f.value}`;
    case "samePattern":
      return `Conjunto: mesmo padrão em ${f.value} partes`;
    case "sameColor":
      return `Conjunto: mesma cor em ${f.value} partes`;
    default:
      return f.key;
  }
}

const speedWord = (s) => (s < 10 ? "muito lenta" : s < 35 ? "lenta" : s > 90 ? "muito rápida" : s > 65 ? "rápida" : "normal");

function ageOf(createdAt) {
  const mins = Math.max(0, (Date.now() - new Date(createdAt).getTime()) / 60000);
  if (mins < 60) return `${Math.floor(mins)} min`;
  const h = mins / 60;
  if (h < 24) return `${Math.floor(h)} h`;
  return `${Math.floor(h / 24)} d`;
}

// Sinergia de cor de cauda no tanque: grupos de 2+ com o bônus resultante.
function tankSynergy(creatures) {
  const counts = {};
  for (const c of creatures) {
    const color = generateTraits(BigInt(c.seed)).tail.color;
    counts[color] = (counts[color] || 0) + 1;
  }
  return Object.entries(counts)
    .filter(([, n]) => n >= 2)
    .map(([color, n]) => ({ color, n, bonus: synergyMultiplier(n) - 1 }))
    .sort((a, b) => b.n - a.n);
}

// Estimativa do próximo peixe na fila a partir do progresso de geração.
function nextFishEta(tank) {
  if (tank.queue.length >= tank.queueCap) return { full: true };
  const interval = tank.generationIntervalMinutes || 15;
  const progress = Number(tank.generationProgressMinutes ?? 0);
  const waterFactor = Number(tank.maintenanceLevel) < 40 ? 0.5 : 1;
  const rate = (tank.online ? 1.0 : 0.45) * waterFactor; // min efetivos por min real
  const mins = rate > 0 ? Math.max(0, interval - progress) / rate : Infinity;
  return { full: false, mins, fraction: Math.min(1, progress / interval) };
}

const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;

// Centro aproximado do peixe nas coordenadas do renderizador (pra girar/espelhar)
const FISH_CX = 290;
const FISH_CY = 210;

// Aura que segue o CONTORNO do peixe: rasteriza a silhueta uma vez, tinge na cor
// e aplica blur; desenhada atrás do peixe vira um brilho abraçando a forma.
const AURA_SCALE = 0.34;
const AURA_PAD = 36;
const AURA_BLUR = 11;
const AURA_CX = AURA_PAD + FISH_CX * AURA_SCALE;
const AURA_CY = AURA_PAD + FISH_CY * AURA_SCALE;
const auraCache = new Map();

function buildAuraSprite(bigSeed, traits, color) {
  const w = Math.ceil(VIEW_W * AURA_SCALE) + AURA_PAD * 2;
  const h = Math.ceil(VIEW_H * AURA_SCALE) + AURA_PAD * 2;

  // 1. desenha o peixe (pose estática) e tinge tudo na cor (mantém o alpha da forma)
  const base = document.createElement("canvas");
  base.width = w; base.height = h;
  const bctx = base.getContext("2d");
  bctx.save();
  bctx.translate(AURA_PAD, AURA_PAD);
  bctx.scale(AURA_SCALE, AURA_SCALE);
  drawFish(bctx, bigSeed, traits, 0);
  bctx.restore();
  bctx.globalCompositeOperation = "source-in";
  bctx.fillStyle = color;
  bctx.fillRect(0, 0, w, h);

  // 2. borra a silhueta tingida → contorno suave
  const out = document.createElement("canvas");
  out.width = w; out.height = h;
  const octx = out.getContext("2d");
  octx.filter = `blur(${AURA_BLUR}px)`;
  octx.drawImage(base, 0, 0);
  return out;
}

function getAuraSprite(c, color) {
  const key = `${c.id}:${color}`;
  let sp = auraCache.get(key);
  if (!sp) { sp = buildAuraSprite(c.bigSeed, c.traits, color); auraCache.set(key, sp); }
  return sp;
}

function drawAura(ctx, sprite, cx, cy, flip, alpha) {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  ctx.globalAlpha = alpha;
  ctx.translate(cx, cy);
  if (flip) ctx.scale(-1, 1);
  ctx.drawImage(sprite, -AURA_CX, -AURA_CY);
  ctx.restore();
}

function AquariumCanvas({ creatures, selectedId, onSelect, interactive = true, ambient = false, quality = 100 }) {
  const W = 960;
  const H = 480;
  const SCALE = 0.34;
  const canvasRef = useRef(null);
  const statesRef = useRef(new Map());
  const creaturesRef = useRef([]);
  const selectedRef = useRef(null);
  const qualityRef = useRef(100);
  const [hover, setHover] = useState(null);

  creaturesRef.current = useMemo(
    () => creatures.map((c) => {
      const bigSeed = BigInt(c.seed);
      return { ...c, bigSeed, traits: generateTraits(bigSeed) };
    }),
    [creatures],
  );
  selectedRef.current = selectedId;
  qualityRef.current = quality;

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    let raf;
    let last = performance.now();

    function frame(now) {
      const dt = reducedMotion ? 0 : Math.min((now - last) / 1000, 0.1);
      last = now;
      const time = reducedMotion ? 1 : now; // 1 (não 0) pra o ambiente aparecer estático

      const q = qualityRef.current;
      const speedFactor = 0.5 + 0.5 * (q / 100); // água suja → peixes mais lentos

      ctx.setTransform(1, 0, 0, 1, 0, 0);
      drawTankBackground(ctx, W, H, time, q);

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

        s.x += s.vx * dt * speedFactor;
        if (s.x < 90) { s.x = 90; s.vx = Math.abs(s.vx); }
        if (s.x > W - 90) { s.x = W - 90; s.vx = -Math.abs(s.vx); }
        const y = s.y + Math.sin(time / 900 + s.phase) * 7;

        // Aura no contorno pra peixes raros+ (lendário reluz de leve)
        const rscore = Number(c.rarityScore);
        if (rscore >= 6.7) {
          const legendary = rscore >= 11.2;
          const pulse = legendary ? 0.82 + 0.18 * Math.sin(time / 650 + s.phase) : 1;
          drawAura(ctx, getAuraSprite(c, bandOf(rscore).color), s.x, y, s.vx > 0, (legendary ? 0.55 : 0.36) * pulse);
        }
        // Aura de seleção (aqua, seguindo o contorno)
        if (c.id === selectedRef.current) {
          drawAura(ctx, getAuraSprite(c, "#54e6d1"), s.x, y, s.vx > 0, 0.5);
        }

        ctx.save();
        ctx.translate(s.x, y);
        ctx.scale(s.vx > 0 ? -SCALE : SCALE, SCALE);
        ctx.translate(-FISH_CX, -FISH_CY);
        drawFish(ctx, c.bigSeed, c.traits, time, s.phase);
        ctx.restore();
      }

      drawTankForeground(ctx, W, H, time, q);

      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }

    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, []);

  function hitTest(e) {
    const rect = canvasRef.current.getBoundingClientRect();
    const px = (e.clientX - rect.left) * (W / rect.width);
    const py = (e.clientY - rect.top) * (H / rect.height);
    const hit = creaturesRef.current.find((c) => {
      const s = statesRef.current.get(c.id);
      return s && Math.abs(px - s.x) < 70 && Math.abs(py - s.y) < 55;
    });
    return { rect, hit };
  }

  function handleClick(e) {
    if (!interactive) return;
    onSelect(hitTest(e).hit?.id ?? null);
  }

  function handleMove(e) {
    if (!interactive) return;
    const { rect, hit } = hitTest(e);
    if (hit) {
      const band = bandOf(Number(hit.rarityScore));
      setHover({ name: band.name, color: band.color, x: e.clientX - rect.left, y: e.clientY - rect.top });
    } else if (hover) {
      setHover(null);
    }
  }

  return (
    <>
      <canvas
        ref={canvasRef} width={W} height={H}
        className={`aquarium${ambient ? " ambient" : ""}`}
        onClick={interactive ? handleClick : undefined}
        onMouseMove={interactive ? handleMove : undefined}
        onMouseLeave={() => hover && setHover(null)}
        role="img" aria-label="Aquário com seus peixes"
      />
      {hover && (
        <div className="fish-tip" style={{ left: hover.x, top: hover.y, "--tier": hover.color }}>{hover.name}</div>
      )}
    </>
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

function TraitRow({ label, value }) {
  return (
    <div className="trait-row">
      <span className="tr-label">{label}</span>
      <span className="tr-value">{value}</span>
    </div>
  );
}

function partSummary(part) {
  const c = PT.color[part.color];
  if (part.pattern === "None") return `${c} · sem padrão`;
  return `${c} · ${PT.pattern[part.pattern].toLowerCase()} ${PT.color[part.patternColor]} `
    + `(tam ${part.patternSize.toFixed(0)}, op ${part.patternOpacity.toFixed(0)}%)`;
}

function FishDetail({ creature, onClose, children }) {
  const seed = creature.seed;
  const bigSeed = useMemo(() => BigInt(seed), [seed]);
  const traits = useMemo(() => generateTraits(bigSeed), [bigSeed]);
  const breakdown = useMemo(() => rarityBreakdown(bigSeed), [bigSeed]);
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  const coins = coinsPerHourOf(score);
  const maxPoints = Math.max(...breakdown.factors.map((f) => f.points), 0.001);

  useEffect(() => {
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal glass" onClick={(e) => e.stopPropagation()}>
        <button className="modal-close" onClick={onClose} aria-label="Fechar">×</button>

        <div className="detail-head">
          <div className="detail-fish"><FishCanvas seed={seed} width={280} /></div>
          <div className="detail-meta">
            <span className="badge big" style={{ "--tier": band.color }}><span className="gem" /> {band.name}</span>
            <div className="detail-score">Raridade <b>{score.toFixed(2)}</b></div>
            <div className="detail-coins"><Coin /> ~{coins.toFixed(1)} <small>soft/h a água cheia</small></div>
            {traits.shimmerTier !== "None" && (
              <div className="shimmer-label">✦ {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}</div>
            )}
            <div className="faint mono">seed {seed} · {ageOf(creature.createdAt)}</div>
          </div>
        </div>

        <div className="detail-section">
          <div className="eyebrow">Atributos</div>
          <TraitRow label="Corpo" value={traits.shimmerTier === "None"
            ? "Cinza, sem brilho"
            : `${PT.tier[traits.shimmerTier]} · ${PT.shimmer[traits.shimmerColor]} ${traits.shimmerOpacity.toFixed(0)}%`} />
          <TraitRow label="Cauda" value={partSummary(traits.tail)} />
          <TraitRow label="Nadadeira dorsal" value={partSummary(traits.dorsal)} />
          <TraitRow label="Barbatana peitoral" value={partSummary(traits.pectoral)} />
          <TraitRow label="Movimento" value={`cauda ${speedWord(traits.movement.tailSpeed)}, `
            + `nadadeira ${speedWord(traits.movement.finSpeed)} · nado ${swimSpeedOf(traits).toFixed(0)} px/s`} />
        </div>

        <div className="detail-section">
          <div className="eyebrow">Por que é raro</div>
          <p className="bd-help">Cada atributo soma pontos conforme quão improvável é. Quanto mais raro o conjunto, maior o score.</p>
          <div className="breakdown">
            {breakdown.factors.map((f, i) => (
              <div className="bd-row" key={i}>
                <span className="bd-label">{factorLabel(f)}</span>
                <span className="bd-bar"><span style={{ width: `${(f.points / maxPoints) * 100}%` }} /></span>
                <span className="bd-prob mono">{f.probPct == null ? "—" : `${f.probPct < 1 ? f.probPct.toFixed(1) : f.probPct.toFixed(0)}%`}</span>
                <span className="bd-points mono">+{f.points.toFixed(2)}</span>
              </div>
            ))}
          </div>
          <div className="bd-total">Score total <b>{breakdown.total.toFixed(2)}</b></div>
        </div>

        {children && <div className="detail-actions">{children}</div>}
      </div>
    </div>
  );
}

const RARITY_RANGES = ["menos de 5.4", "5.4 – 7.5", "7.5 – 9.8", "9.8 – 14.0", "14.0 ou mais"];

function RarityGuide({ onClose }) {
  useEffect(() => {
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal glass narrow" onClick={(e) => e.stopPropagation()}>
        <button className="modal-close" onClick={onClose} aria-label="Fechar">×</button>
        <div className="eyebrow">Como funciona a raridade</div>
        <p className="muted guide-intro">
          Cada peixe nasce de um seed único. Todo atributo — brilho do corpo, cor e padrão de cada parte,
          velocidade de nado — é sorteado com uma probabilidade. Quanto mais improvável o conjunto, maior a
          raridade; e quanto mais raro, mais moedas o peixe produz por hora.
        </p>
        <div className="guide-bands">
          {BANDS.map((b, i) => (
            <div className="guide-band" key={b.name}>
              <span className="gem" style={{ background: b.color, boxShadow: `0 0 10px ${b.color}` }} />
              <span className="gb-name" style={{ color: b.color }}>{b.name}</span>
              <span className="gb-range mono">{RARITY_RANGES[i]}</span>
            </div>
          ))}
        </div>
        <p className="faint guide-foot">Abra qualquer peixe para ver o detalhamento “por que é raro”.</p>
      </div>
    </div>
  );
}

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
  const [showGuide, setShowGuide] = useState(false);
  const [listOpen, setListOpen] = useState(() => localStorage.getItem("tankListOpen") !== "false");
  const toggleList = () => setListOpen((v) => { localStorage.setItem("tankListOpen", String(!v)); return !v; });
  const selected = tank.creatures.find((c) => c.id === selectedId) ?? null;
  const lowWater = Number(tank.maintenanceLevel) < 40;

  const current = Number(tank.coinsPerHour ?? 0);
  const synergy = tankSynergy(tank.creatures);
  // potencial já com sinergia, a água cheia (pra comparar com o atual da API)
  const colorCounts = {};
  for (const c of tank.creatures) {
    const col = generateTraits(BigInt(c.seed)).tail.color;
    colorCounts[col] = (colorCounts[col] || 0) + 1;
  }
  const potential = tank.creatures.reduce((s, c) => {
    const col = generateTraits(BigInt(c.seed)).tail.color;
    return s + coinsPerHourOf(Number(c.rarityScore), synergyMultiplier(colorCounts[col]));
  }, 0);
  const nextFish = nextFishEta(tank);

  // Lista ordenada por produção (farm) desc — que já reflete raridade × sinergia
  const listFish = tank.creatures
    .map((c) => {
      const traits = generateTraits(BigInt(c.seed));
      const col = traits.tail.color;
      const mult = synergyMultiplier(colorCounts[col] ?? 1);
      return { c, traits, col, prod: coinsPerHourOf(Number(c.rarityScore), mult) };
    })
    .sort((a, b) => b.prod - a.prod || Number(b.c.rarityScore) - Number(a.c.rarityScore));

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

  async function store(creature) {
    try {
      await api.storeCreature(creature.id);
      notify("Guardado na mochila.");
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
          <span className="capacity-chip" title="Peixes ativos no tanque / capacidade">
            🐟 {tank.creatures.length}/{tank.capacity}
          </span>
          <button className="guide-btn" onClick={() => setShowGuide(true)} title="Como funciona a raridade">?</button>
          <span className="spacer" style={{ flex: 1 }} />
          <span className="income-chip" title={
            potential > current + 0.05
              ? `Produção atual (com a água). Potencial a água cheia: ${potential.toFixed(1)}/h`
              : "Moedas por hora que seu tanque farma"
          }>
            <Coin />+{current.toFixed(1)}<small>/h</small>
            {potential > current + 0.05 && <em className="of-potential"> de {potential.toFixed(0)}</em>}
          </span>
          <span className="water-gauge">
            <span className="label">Água</span>
            <span className="water-track">
              <span className={`water-fill${lowWater ? " low" : ""}`} style={{ width: `${tank.maintenanceLevel}%` }} />
            </span>
            <span className="val">{Number(tank.maintenanceLevel).toFixed(0)}</span>
          </span>
          <button onClick={buyFilter} title="Restaura a qualidade da água pra 100">Filtro · 20</button>
        </div>

        <AquariumCanvas
          creatures={tank.creatures} selectedId={selectedId}
          onSelect={setSelectedId} quality={Number(tank.maintenanceLevel)}
        />

        {tank.creatures.length === 0 && (
          <div className="tank-empty">
            <strong>Seu aquário está esperando</strong>
            <span className="muted">Colete um peixe da fila para começar.</span>
          </div>
        )}
      </div>

      {synergy.length > 0 && (
        <div className="synergy-bar glass">
          <span className="eyebrow">Sinergia de cor</span>
          {synergy.map((g) => (
            <span key={g.color} className="synergy-chip" style={{ "--tier": PART_HEX[g.color] }}>
              <span className="dot-color" /> {PT.color[g.color]} ×{g.n} <em>+{(g.bonus * 100).toFixed(0)}%</em>
            </span>
          ))}
        </div>
      )}
      {tank.creatures.length > 0 && !selected && (
        <p className="hint">Clique num peixe para ver os detalhes, guardar, vender ou transferir.</p>
      )}
      {selected && (
        <FishDetail creature={selected} onClose={() => setSelectedId(null)}>
          <button onClick={() => store(selected)} title="Guardar na mochila (não farma)">Guardar</button>
          <button onClick={() => sell(selected)}>Vender</button>
          <button onClick={() => transfer(selected)}>Transferir</button>
        </FishDetail>
      )}
      {showGuide && <RarityGuide onClose={() => setShowGuide(false)} />}

      {tank.creatures.length > 0 && (
        <section>
          <div className="section-head">
            <button className="collapse-btn" onClick={toggleList} aria-expanded={listOpen}
              title={listOpen ? "Recolher lista" : "Expandir lista"}>
              <span className={`chevron${listOpen ? " open" : ""}`}>▾</span>
            </button>
            <span className="eyebrow">Peixes no tanque</span>
            <span className="count">{tank.creatures.length}/{tank.capacity}</span>
            <span className="spacer" />
            {listOpen && <span className="faint" style={{ fontSize: "0.82rem" }}>clique para detalhes</span>}
          </div>
          {listOpen && (
          <div className="fish-list">
            {listFish.map(({ c, traits, col, prod }) => {
              const band = bandOf(Number(c.rarityScore));
              return (
                <button key={c.id} className="fish-row" onClick={() => setSelectedId(c.id)} style={{ "--tier": band.color }}>
                  <span className="fr-thumb"><FishCanvas seed={c.seed} width={72} /></span>
                  <span className="fr-body">
                    <span className="fr-line1">
                      <span className="badge" style={{ "--tier": band.color }}><span className="gem" /> {band.name}</span>
                      <span className="fr-score mono">{Number(c.rarityScore).toFixed(1)}</span>
                    </span>
                    <span className="fr-line2">
                      <span className="fr-color"><span className="dot-color" style={{ background: PART_HEX[col] }} /> {PT.color[col]}</span>
                      {traits.shimmerTier !== "None" && <span className="shimmer-label">✦ {PT.shimmer[traits.shimmerColor]}</span>}
                    </span>
                  </span>
                  <span className="fr-prod mono"><Coin /> ~{prod.toFixed(1)}<small>/h</small></span>
                </button>
              );
            })}
          </div>
          )}
        </section>
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
        {nextFish.full ? (
          <p className="hint">Fila cheia — colete peixes para liberar espaço.</p>
        ) : (
          <div className="next-fish">
            <span className="nf-label">
              Próximo peixe {nextFish.mins < 1 ? "em instantes" : `em ~${Math.ceil(nextFish.mins)} min`}
              {!tank.online && " (offline, mais devagar)"}
            </span>
            <span className="nf-track"><span style={{ width: `${(nextFish.fraction * 100).toFixed(0)}%` }} /></span>
          </div>
        )}
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
  const [detail, setDetail] = useState(null);

  const refresh = useCallback(async () => {
    setListings(await api.listings());
  }, []);

  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  async function buy(listing) {
    try {
      await api.buyListing(listing.id);
      notify(`Comprado por ${listing.priceSoft} soft!`);
      setDetail(null);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }
  async function cancel(listing) {
    try {
      await api.cancelListing(listing.id);
      setDetail(null);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }

  if (listings === null) return <p className="hint">Carregando mercado…</p>;
  if (listings.length === 0) return <p className="hint">Nenhum peixe à venda no momento — seja o primeiro a listar.</p>;

  return (
    <>
      <div className="grid">
        {listings.map((l) => (
          <div key={l.id} className="card" style={{ "--tier": bandOf(Number(l.rarityScore)).color }}>
            <button className="fish-stage as-button" onClick={() => setDetail(l)} title="Ver detalhes">
              <FishCanvas seed={l.seed} />
            </button>
            <div className="card-row">
              <RarityBadge score={Number(l.rarityScore)} />
              <span className="price"><Coin />{Number(l.priceSoft).toFixed(0)}</span>
            </div>
            <div className="card-row">
              <span className="produces mono">~{coinsPerHourOf(Number(l.rarityScore)).toFixed(1)}/h</span>
              <span className="seller">de {l.sellerName}</span>
            </div>
            <div className="card-row">
              <button onClick={() => setDetail(l)}>Detalhes</button>
              {l.sellerId === userId
                ? <button onClick={() => cancel(l)}>Cancelar</button>
                : <button className="btn-primary" onClick={() => buy(l)}>Comprar</button>}
            </div>
          </div>
        ))}
      </div>
      {detail && (
        <FishDetail creature={detail} onClose={() => setDetail(null)}>
          {detail.sellerId === userId
            ? <button onClick={() => cancel(detail)}>Cancelar listagem</button>
            : <button className="btn-primary" onClick={() => buy(detail)}>Comprar por {Number(detail.priceSoft).toFixed(0)} soft</button>}
        </FishDetail>
      )}
    </>
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

function BackpackView({ refreshTank, notify }) {
  const [data, setData] = useState(null);
  const [detail, setDetail] = useState(null);

  const refresh = useCallback(async () => { setData(await api.backpack()); }, []);
  useEffect(() => { refresh().catch((e) => notify(e.message)); }, [refresh, notify]);

  async function deploy(c) {
    try {
      await api.deployCreature(c.id);
      notify("Peixe de volta ao tanque!");
      setDetail(null);
      await Promise.all([refresh(), refreshTank()]);
    } catch (e) { notify(e.message); }
  }
  async function sell(c) {
    const price = window.prompt("Preço em moeda soft:", "50");
    if (!price) return;
    try { await api.createListing(c.id, Number(price)); notify("Listado no mercado."); setDetail(null); await refresh(); }
    catch (e) { notify(e.message); }
  }
  async function transfer(c) {
    const to = window.prompt("Transferir para qual jogador (username)?");
    if (!to) return;
    try { await api.transferCreature(c.id, to.trim()); notify(`Transferido para ${to.trim()}.`); setDetail(null); await refresh(); }
    catch (e) { notify(e.message); }
  }

  if (data === null) return <p className="hint">Carregando mochila…</p>;

  return (
    <>
      <div className="section-head">
        <span className="eyebrow">Mochila</span>
        <span className="count">{data.creatures.length}/{data.capacity}</span>
      </div>
      {data.creatures.length === 0 ? (
        <p className="hint">Mochila vazia. Guarde peixes do tanque aqui — eles ficam seguros (mas não farmam).</p>
      ) : (
        <div className="grid">
          {data.creatures.map((c) => (
            <div key={c.id} className="card" style={{ "--tier": bandOf(Number(c.rarityScore)).color }}>
              <button className="fish-stage as-button" onClick={() => setDetail(c)} title="Ver detalhes">
                <FishCanvas seed={c.seed} />
              </button>
              <div className="card-row">
                <RarityBadge score={Number(c.rarityScore)} />
                <span className="produces mono">~{coinsPerHourOf(Number(c.rarityScore)).toFixed(1)}/h</span>
              </div>
              <div className="card-row">
                <button className="btn-primary" onClick={() => deploy(c)}>Pro tanque</button>
                <button onClick={() => sell(c)}>Vender</button>
                <button onClick={() => transfer(c)}>Transferir</button>
              </div>
            </div>
          ))}
        </div>
      )}
      {detail && (
        <FishDetail creature={detail} onClose={() => setDetail(null)}>
          <button className="btn-primary" onClick={() => deploy(detail)}>Pro tanque</button>
          <button onClick={() => sell(detail)}>Vender</button>
          <button onClick={() => transfer(detail)}>Transferir</button>
        </FishDetail>
      )}
    </>
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
          <button className={tab === "backpack" ? "active" : ""} onClick={() => setTab("backpack")}>Mochila</button>
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
        {tab === "backpack" && <BackpackView refreshTank={refreshTank} notify={notify} />}
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
