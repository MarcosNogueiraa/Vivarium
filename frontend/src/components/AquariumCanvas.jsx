import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState } from "react";
import { roll01, traitsOf } from "../lib/generator.js";
import {
  bandOf, drawFish, drawFishShadow, drawTankBackground, drawTankForeground,
  SUBSTRATE_BAND_H, swimSpeedOf, VIEW_H, VIEW_W,
} from "../lib/fishRenderer.js";
import { reducedMotion } from "../lib/motion.js";

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

export const PIP_SUPPORTED = typeof window !== "undefined" && "documentPictureInPicture" in window;

/** O aquário animado: peixes nadando, aura pra raros+, seleção por clique. */
export const AquariumCanvas = forwardRef(function AquariumCanvas({
  creatures, selectedId, onSelect, interactive = true, ambient = false, quality = 100, theme = "default",
  decorTier = 0, onPipChange,
}, ref) {
  const W = 960;
  const H = 480;
  const SCALE = 0.34;
  const wrapRef = useRef(null);
  const canvasRef = useRef(null);
  const pipWindowRef = useRef(null);
  const statesRef = useRef(new Map());
  const creaturesRef = useRef([]);
  const selectedRef = useRef(null);
  const qualityRef = useRef(100);
  const themeRef = useRef("default");
  const decorTierRef = useRef(0);
  const [hover, setHover] = useState(null);
  const [pipActive, setPipActive] = useState(false);
  const resScaleRef = useRef(1);

  creaturesRef.current = useMemo(
    () => creatures.map((c) => {
      const bigSeed = BigInt(c.seed);
      return { ...c, bigSeed, traits: traitsOf(c) };
    }),
    [creatures],
  );
  selectedRef.current = selectedId;
  qualityRef.current = quality;
  themeRef.current = theme;
  decorTierRef.current = decorTier;

  // Resolução do canvas (backing store) acompanha o tamanho exibido × DPR, senão
  // ele fica com resolução fixa 960×480 esticada — nítido no card, borrado em tela cheia.
  // Coordenadas de jogo continuam em W×H lógicos (960×480); só o raster final ganha nitidez.
  useEffect(() => {
    const el = canvasRef.current;
    function updateResolution() {
      const rect = el.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      const dpr = window.devicePixelRatio || 1;
      const scale = Math.min(2.5, Math.max(1, (rect.width * dpr) / W));
      if (Math.abs(scale - resScaleRef.current) > 0.05) {
        resScaleRef.current = scale;
        el.width = Math.round(W * scale);
        el.height = Math.round(H * scale);
      }
    }
    updateResolution();
    const ro = new ResizeObserver(updateResolution);
    ro.observe(el);
    window.addEventListener("resize", updateResolution);
    return () => { ro.disconnect(); window.removeEventListener("resize", updateResolution); };
  }, []);

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

      const th = themeRef.current;
      const decor = decorTierRef.current;
      ctx.setTransform(resScaleRef.current, 0, 0, resScaleRef.current, 0, 0);
      drawTankBackground(ctx, W, H, time, q, th, decor);

      // Garante o estado de cada peixe e agrupa por cor de cauda pra uma leve
      // coesão de cardume — mesmo campo/limiar (n >= 2) que tankSynergy usa pro
      // bônus de renda (tankMath.js), só aplicado aqui visualmente.
      const schoolCentroids = new Map(); // cor -> { sumX, sumY, count }
      for (const c of creaturesRef.current) {
        let s = statesRef.current.get(c.id);
        if (!s) {
          s = {
            x: 120 + roll01(c.bigSeed, "pos_x") * (W - 240),
            y: 100 + roll01(c.bigSeed, "pos_y") * (H - 200),
            vx: (roll01(c.bigSeed, "dir") < 0.5 ? -1 : 1) * swimSpeedOf(c.traits),
            phase: roll01(c.bigSeed, "phase") * Math.PI * 2,
            nextTurnAt: time + 8000 + roll01(c.bigSeed, "turn0") * 12000,
          };
          statesRef.current.set(c.id, s);
        }
        const color = c.traits.tail.color;
        const g = schoolCentroids.get(color) ?? { sumX: 0, sumY: 0, count: 0 };
        g.sumX += s.x; g.sumY += s.y; g.count++;
        schoolCentroids.set(color, g);
      }

      for (const c of creaturesRef.current) {
        const s = statesRef.current.get(c.id);

        // Coesão de cardume: puxão bem sutil na posição-base em direção ao
        // centro do grupo de mesma cor (não mexe em vx — evita acelerar sem
        // limite; a nadada continua independente, só a posição converge um
        // pouco).
        const group = schoolCentroids.get(c.traits.tail.color);
        if (group && group.count >= 2) {
          const avgX = group.sumX / group.count;
          const avgY = group.sumY / group.count;
          s.x += (avgX - s.x) * 0.06 * dt;
          s.y += (avgY - s.y) * 0.03 * dt;
        }

        s.x += s.vx * dt * speedFactor;
        if (s.x < 90) { s.x = 90; s.vx = Math.abs(s.vx); }
        if (s.x > W - 90) { s.x = W - 90; s.vx = -Math.abs(s.vx); }

        // Vira sozinho de vez em quando no meio do tanque também (não só nas
        // paredes) — evita o ping-pong mecânico. Próximo giro reagendado sempre
        // que acontece um (seeded pelo instante atual, continua determinístico).
        if (time > s.nextTurnAt) {
          s.vx = -s.vx;
          s.nextTurnAt = time + 8000 + roll01(c.bigSeed, `turn_${Math.floor(time / 1000)}`) * 12000;
        }

        const y = s.y + Math.sin(time / 900 + s.phase) * 7;

        // Sombra no substrato — quanto mais fundo o peixe, maior/mais escura.
        drawFishShadow(ctx, s.x, y, H - SUBSTRATE_BAND_H);

        // Aura no contorno pra peixes raros+ (lendário reluz de leve).
        // Cortes seguem as faixas de raridade v2 (Raro ≥ 7.5, Lendário ≥ 14.0).
        const rscore = Number(c.rarityScore);
        if (rscore >= 7.5) {
          const legendary = rscore >= 14.0;
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

      drawTankForeground(ctx, W, H, time, q, th, decor);

      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }

    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, []);

  function closePip() {
    try {
      if (wrapRef.current && canvasRef.current && canvasRef.current.parentNode !== wrapRef.current) {
        canvasRef.current.classList.remove("aquarium-pip");
        wrapRef.current.appendChild(canvasRef.current);
      }
    } catch { /* pip window/canvas already gone */ }
    pipWindowRef.current = null;
    setPipActive(false);
    onPipChange?.(false);
  }

  async function openPip() {
    if (!PIP_SUPPORTED || pipActive) return;
    let pipWindow;
    try {
      pipWindow = await window.documentPictureInPicture.requestWindow({ width: 480, height: 260 });
    } catch { return; } // usuário cancelou ou navegador recusou
    pipWindowRef.current = pipWindow;
    for (const sheet of document.styleSheets) {
      try {
        const css = [...sheet.cssRules].map((r) => r.cssText).join("\n");
        const style = document.createElement("style");
        style.textContent = css;
        pipWindow.document.head.appendChild(style);
      } catch {
        if (sheet.href) {
          const link = document.createElement("link");
          link.rel = "stylesheet";
          link.href = sheet.href;
          pipWindow.document.head.appendChild(link);
        }
      }
    }
    pipWindow.document.title = "Vivarium — Aquário";
    pipWindow.document.body.style.margin = "0";
    pipWindow.document.body.style.background = "#04181f";
    canvasRef.current.classList.add("aquarium-pip");
    pipWindow.document.body.appendChild(canvasRef.current);
    pipWindow.addEventListener("pagehide", closePip, { once: true });
    setPipActive(true);
    onPipChange?.(true);
  }

  useImperativeHandle(ref, () => ({
    togglePip() {
      if (pipActive) pipWindowRef.current?.close();
      else openPip();
    },
    pipSupported: PIP_SUPPORTED,
  }));

  useEffect(() => () => {
    if (pipWindowRef.current && !pipWindowRef.current.closed) pipWindowRef.current.close();
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
    <div ref={wrapRef} className="aquarium-wrap">
      <canvas
        ref={canvasRef} width={W} height={H}
        className={`aquarium${ambient ? " ambient" : ""}${!interactive ? " no-click" : ""}`}
        onClick={interactive ? handleClick : undefined}
        onMouseMove={interactive ? handleMove : undefined}
        onMouseLeave={() => hover && setHover(null)}
        role="img" aria-label="Aquário com seus peixes"
      />
      {hover && (
        <div className="fish-tip" style={{ left: hover.x, top: hover.y, "--tier": hover.color }}>{hover.name}</div>
      )}
      {pipActive && (
        <div className="pip-placeholder">
          <span>🐠 Aquário aberto em pop-up</span>
        </div>
      )}
    </div>
  );
});
