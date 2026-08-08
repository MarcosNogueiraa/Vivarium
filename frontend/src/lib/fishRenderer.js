// Renderização do peixe em camadas (mesma geometria do protótipo):
// cauda → dorsal → corpo cinza → shimmer (blend overlay) → olho → peitoral.
// Coordenadas fixas 560x420; use ctx.scale antes pra outros tamanhos.
import { roll01 } from "./generator.js";

export const VIEW_W = 560;
export const VIEW_H = 420;

export const PART_HEX = {
  Orange: "#e8813a", Blue: "#3d84dc", Red: "#d94f4f", Yellow: "#ecc94b",
  Green: "#4fae6e", Purple: "#925cd1", Black: "#23272e", PureWhite: "#f2f5f7",
};
const SHIMMER_HEX = {
  Gold: "#f7c948", Silver: "#dde3e8", Bluish: "#86bcdd", Emerald: "#34d399",
  Purple: "#a06ae0", Pink: "#f472b6", AbsoluteBlack: "#000000",
};

export const PT = {
  tier: { None: "Sem brilho", Subtle: "Brilho sutil", Vibrant: "Brilho vibrante", Rare: "Brilho raro", Legendary: "Brilho lendário" },
  shimmer: { Gold: "Dourado", Silver: "Prateado", Bluish: "Azulado", Emerald: "Verde-esmeralda", Purple: "Roxo", Pink: "Rosa", Rainbow: "Arco-íris", AbsoluteBlack: "Preto absoluto", Iridescent: "Iridescente" },
  color: { Orange: "Laranja", Blue: "Azul", Red: "Vermelho", Yellow: "Amarelo", Green: "Verde", Purple: "Roxo", Black: "Preto", PureWhite: "Branco puro" },
  pattern: {
    None: "Sem padrão", Stripe: "Estria", Dot: "Bolinha", Gradient: "Degradê", Mottled: "Manchado",
    Scales: "Escamas", Chevron: "Ziguezague", Net: "Rede", Rays: "Raios", Ocellus: "Ocelo", Marble: "Mármore",
  },
};

// Faixas calibradas via simulação (CLAUDE.md seção 5). Cores claras/vibrantes,
// tunadas pra ler bem no aquário escuro e nas superfícies escuras da UI
// (espelham os --r-* do styles.css).
export const BANDS = [
  { max: 5.4, name: "Comum", color: "#93a7b0" },
  { max: 7.5, name: "Incomum", color: "#57b876" },
  { max: 9.8, name: "Raro", color: "#4d8fe0" },
  { max: 14.0, name: "Épico", color: "#a86ce4" },
  { max: Infinity, name: "Lendário", color: "#f0b93b" },
];
export const bandOf = (score) => BANDS.find((b) => score < b.max);

// Nomes das faixas de capacidade do tanque, na ordem (espelha CapacityBands.All
// no backend, TickConfig.cs — só os nomes de exibição, não a tabela inteira).
const CAPACITY_BAND_NAMES = ["Aquário", "Aquário Grande", "Aquário Master"];
/** Nome da faixa (vindo de `tank.capacityBandName`) → tier de decoração 0/1/2. */
export const decorTierOf = (capacityBandName) => {
  const i = CAPACITY_BAND_NAMES.indexOf(capacityBandName);
  return i < 0 ? 0 : i;
};

const bodyPath = new Path2D(
  "M 130 210 C 155 150 235 125 305 138 C 348 147 376 178 384 210 " +
  "C 376 242 348 273 305 282 C 235 295 155 270 130 210 Z");
const tailPath = new Path2D(
  "M 378 200 C 405 185 432 168 452 152 C 440 172 426 192 422 210 " +
  "C 426 228 440 248 452 268 C 432 252 405 235 378 220 Q 371 210 378 200 Z");
const dorsalPath = new Path2D(
  "M 228 138 C 243 100 283 80 330 92 C 330 107 326 126 318 143 " +
  "C 290 132 255 132 228 138 Z");
const pectoralPath = new Path2D(
  "M 228 244 C 253 250 275 266 285 292 C 268 290 243 282 230 262 Q 224 250 228 244 Z");

const TAIL_BBOX = [372, 148, 84, 124];
const DORSAL_BBOX = [224, 78, 110, 68];
const PECTORAL_BBOX = [220, 240, 70, 56];
const BODY_BBOX = [128, 124, 260, 172];

// Mapeamento visual dos traits de movimento (velocidade 0-100 vem do motor)
export const MOVEMENT_TUNING = {
  tailPeriodMax: 260, tailPeriodMin: 40,   // v=0 → Max (lenta), v=100 → Min (rápida)
  finPeriodMax: 260, finPeriodMin: 40,
  swimBase: 12, swimRange: 70, swimTailWeight: 0.75,

  // Onda viajante por parte: fatias verticais deslocadas por uma senoide que
  // "viaja" da base (u=0) à ponta (u=1), formando a curva em S. ampBase é o
  // deslocamento na ponta em px (multiplicado pela amplitude sorteada do peixe).
  // jointX/len definem o eixo ao longo do qual a onda viaja.
  tailWave: { ampBase: 34, waveNumber: 3.2, exp: 1.4, strips: 28, jointX: 380, len: 78, yBias: 0 },
  // dorsal: baixa 3px (afunda a base no corpo, que é desenhado por cima) e onda
  // pequena, pra a borda de baixo nunca subir e abrir vão com o corpo
  dorsalWave: { ampBase: 4, waveNumber: 2.2, exp: 1.1, strips: 20, jointX: 224, len: 110, yBias: 3 },
  pectoralWave: { ampBase: 14, waveNumber: 2.0, exp: 1.2, strips: 18, jointX: 220, len: 72, yBias: 0 },
  spriteMargin: 3,
};

const periodOf = (speed, max, min) => max - (speed / 100) * (max - min);

/** Velocidade de nado em px/s, calculada dos traits (cauda rápida = peixe rápido). */
export function swimSpeedOf(traits) {
  const w = MOVEMENT_TUNING.swimTailWeight;
  const factor = w * (traits.movement.tailSpeed / 100) + (1 - w) * (traits.movement.finSpeed / 100);
  return MOVEMENT_TUNING.swimBase + factor * MOVEMENT_TUNING.swimRange;
}

function drawPattern(ctx, seed, part, path, bbox) {
  if (part.pattern === "None") return;
  const color = PART_HEX[part.patternColor];
  const size = part.patternSize;
  const [bx, by, bw, bh] = bbox;

  ctx.save();
  ctx.clip(path);
  ctx.globalAlpha = part.patternOpacity / 100;
  ctx.fillStyle = color;

  if (part.pattern === "Stripe") {
    const width = 3 + size * 0.12;
    const gap = width * 1.7;
    ctx.translate(bx + bw / 2, by + bh / 2);
    ctx.rotate(-0.35);
    for (let x = -bw; x < bw; x += width + gap)
      ctx.fillRect(x, -bh, width, bh * 2);
  } else if (part.pattern === "Dot") {
    const r = 1.5 + size * 0.07;
    const step = Math.max(6, r * 3.2);
    for (let row = 0; row * step < bh + step; row++) {
      const offset = row % 2 === 0 ? 0 : step / 2;
      for (let x = bx + offset; x < bx + bw + step; x += step) {
        ctx.beginPath();
        ctx.arc(x, by + row * step, r, 0, Math.PI * 2);
        ctx.fill();
      }
    }
  } else if (part.pattern === "Gradient") {
    const extent = 0.35 + (size / 100) * 0.65;
    const grad = ctx.createLinearGradient(0, by, 0, by + bh / extent);
    grad.addColorStop(0, color + "00");
    grad.addColorStop(1, color);
    ctx.fillStyle = grad;
    ctx.fillRect(bx, by, bw, bh);
  } else if (part.pattern === "Mottled") {
    const visualRoll = (salt) => roll01(seed, "visual_" + salt);
    for (let i = 0; i < 9; i++) {
      const cx = bx + visualRoll(`blob${i}x`) * bw;
      const cy = by + visualRoll(`blob${i}y`) * bh;
      const r = (3 + size * 0.09) * (0.6 + 0.8 * visualRoll(`blob${i}r`));
      for (let j = 0; j < 3; j++) {
        ctx.beginPath();
        ctx.arc(cx + (j - 1) * r * 0.5, cy + (j % 2) * r * 0.4, r * (1 - j * 0.2), 0, Math.PI * 2);
        ctx.fill();
      }
    }
  } else if (part.pattern === "Scales") {
    const r = 4 + size * 0.09;
    const stepX = r * 1.8, stepY = r * 1.05;
    ctx.lineWidth = Math.max(1, r * 0.16);
    ctx.strokeStyle = color;
    for (let row = 0, y = by; y < by + bh + stepY; row++, y += stepY) {
      const off = row % 2 ? stepX / 2 : 0;
      for (let x = bx + off; x < bx + bw + stepX; x += stepX) {
        ctx.beginPath();
        ctx.arc(x, y, r, Math.PI * 0.15, Math.PI * 0.85);
        ctx.stroke();
      }
    }
  } else if (part.pattern === "Chevron") {
    const amp = 5 + size * 0.07;
    const wl = amp * 1.8;
    ctx.lineWidth = Math.max(1.5, 2 + size * 0.03);
    ctx.strokeStyle = color;
    ctx.lineJoin = "round";
    for (let y = by; y < by + bh + amp * 2; y += amp * 2.2) {
      ctx.beginPath();
      let up = true;
      for (let x = bx - wl; x <= bx + bw + wl; x += wl, up = !up)
        ctx.lineTo(x, y + (up ? -amp : amp));
      ctx.stroke();
    }
  } else if (part.pattern === "Net") {
    const gap = 7 + size * 0.1;
    ctx.lineWidth = 1;
    ctx.strokeStyle = color;
    for (const dir of [1, -1])
      for (let c = -bh; c < bw + bh; c += gap) {
        ctx.beginPath();
        ctx.moveTo(bx + c, by);
        ctx.lineTo(bx + c + dir * bh, by + bh);
        ctx.stroke();
      }
  } else if (part.pattern === "Rays") {
    const baseX = bx + bw * 0.08, baseY = by + bh / 2;
    const rays = Math.round(7 + size * 0.08);
    ctx.lineWidth = Math.max(1, 1.5 + size * 0.02);
    ctx.strokeStyle = color;
    for (let i = 0; i < rays; i++) {
      ctx.beginPath();
      ctx.moveTo(baseX, baseY);
      ctx.lineTo(bx + bw, by + (i / (rays - 1)) * bh);
      ctx.stroke();
    }
  } else if (part.pattern === "Ocellus") {
    const visualRoll = (salt) => roll01(seed, "visual_" + salt);
    const r = 5 + size * 0.1;
    const dark = mixHex(color, "#04121a", 0.55);
    for (let i = 0; i < 3; i++) {
      const cx = bx + (0.25 + 0.5 * visualRoll(`oc${i}x`)) * bw;
      const cy = by + (0.25 + 0.5 * visualRoll(`oc${i}y`)) * bh;
      ctx.fillStyle = color;
      ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = dark;
      ctx.beginPath(); ctx.arc(cx, cy, r * 0.55, 0, Math.PI * 2); ctx.fill();
    }
  } else if (part.pattern === "Marble") {
    const visualRoll = (salt) => roll01(seed, "visual_" + salt);
    ctx.strokeStyle = color;
    ctx.lineCap = "round";
    for (let i = 0; i < 4; i++) {
      ctx.lineWidth = 1.5 + visualRoll(`mv${i}w`) * (1 + size * 0.03);
      ctx.beginPath();
      ctx.moveTo(bx, by + visualRoll(`mv${i}a`) * bh);
      ctx.bezierCurveTo(
        bx + bw * 0.33, by + visualRoll(`mv${i}b`) * bh,
        bx + bw * 0.66, by + visualRoll(`mv${i}c`) * bh,
        bx + bw, by + visualRoll(`mv${i}d`) * bh);
      ctx.stroke();
    }
  }
  ctx.restore();
}

function fillPart(ctx, path, color) {
  ctx.fillStyle = PART_HEX[color];
  ctx.fill(path);
  ctx.strokeStyle = "rgba(0,0,0,0.25)";
  ctx.lineWidth = 1.5;
  ctx.stroke(path);
}

// Rasteriza a parte (fill + padrão) uma vez num canvas offscreen e cacheia por
// seed+nome. A onda viajante depois só reposiciona fatias desse sprite — o padrão
// acompanha de graça, sem redesenhar por frame.
const spriteCache = new Map();

function getPartSprite(seed, name, part, path, bbox) {
  const key = `${seed}:${name}`;
  const cached = spriteCache.get(key);
  if (cached) return cached;

  const M = MOVEMENT_TUNING.spriteMargin;
  const [bx, by, bw, bh] = bbox;
  const canvas = document.createElement("canvas");
  canvas.width = bw + 2 * M;
  canvas.height = bh + 2 * M;
  const sctx = canvas.getContext("2d");
  sctx.translate(-bx + M, -by + M); // ponto absoluto (X,Y) → (X-bx+M, Y-by+M)
  fillPart(sctx, path, part.color);
  drawPattern(sctx, seed, part, path, bbox);

  const sprite = { canvas, ox: bx - M, oy: by - M };
  spriteCache.set(key, sprite);
  return sprite;
}

/**
 * Blita o sprite em fatias verticais, cada coluna deslocada por uma onda que
 * viaja de jointX (u=0) até jointX+len (u=1). ampTip = deslocamento na ponta (px).
 */
function wavyBlit(ctx, sprite, cfg, ampTip, period, time, phaseArg) {
  const { canvas, ox, oy } = sprite;
  const yBias = cfg.yBias ?? 0;
  if (time === 0 || ampTip === 0) {
    ctx.drawImage(canvas, ox, oy + yBias);
    return;
  }
  const basePhase = time / period + phaseArg;
  const stripW = canvas.width / cfg.strips;
  for (let i = 0; i < cfg.strips; i++) {
    const sx = i * stripW;
    const u = Math.min(1, Math.max(0, (ox + sx + stripW / 2 - cfg.jointX) / cfg.len));
    const amp = ampTip * Math.pow(u, cfg.exp);
    const offset = yBias + amp * Math.sin(basePhase - cfg.waveNumber * u);
    // +1 de largura pra sobrepor as fatias e não deixar costura
    ctx.drawImage(canvas, sx, 0, stripW + 1, canvas.height, ox + sx, oy + offset, stripW + 1, canvas.height);
  }
}

function drawShimmer(ctx, traits, time) {
  if (traits.shimmerTier === "None") return;
  const [bx, , bw] = BODY_BBOX;
  ctx.save();
  ctx.clip(bodyPath);
  ctx.globalAlpha = traits.shimmerOpacity / 100;
  ctx.globalCompositeOperation = "overlay";

  if (traits.shimmerColor === "Rainbow") {
    const grad = ctx.createLinearGradient(bx, 0, bx + bw, 0);
    ["#ff5252", "#ffb142", "#ffe94d", "#5ee07a", "#4dc4ff", "#9c6bff"].forEach((c, i, arr) =>
      grad.addColorStop(i / (arr.length - 1), c));
    ctx.fillStyle = grad;
  } else if (traits.shimmerColor === "Iridescent") {
    const hue = (time / 30) % 360;
    const grad = ctx.createLinearGradient(bx, 0, bx + bw, 0);
    for (let i = 0; i <= 4; i++)
      grad.addColorStop(i / 4, `hsl(${(hue + i * 45) % 360} 90% 65%)`);
    ctx.fillStyle = grad;
  } else if (traits.shimmerColor === "Gold" || traits.shimmerColor === "Silver") {
    // Metálico: realça pra não confundir com o cinza do peixe comum
    ctx.globalAlpha = Math.min(1, (traits.shimmerOpacity / 100) * 2.2);
    const base = SHIMMER_HEX[traits.shimmerColor];
    const bright = traits.shimmerColor === "Gold" ? "#fff2c2" : "#ffffff";
    const grad = ctx.createLinearGradient(bx, BODY_BBOX[1], bx + bw, BODY_BBOX[1] + BODY_BBOX[3]);
    grad.addColorStop(0, base);
    grad.addColorStop(0.5, bright);
    grad.addColorStop(1, base);
    ctx.fillStyle = grad;
  } else {
    ctx.fillStyle = SHIMMER_HEX[traits.shimmerColor];
  }
  ctx.fillRect(...BODY_BBOX);
  ctx.restore();
}

/**
 * Desenha o peixe inteiro. `seed` (BigInt) só é usado pra manchas determinísticas;
 * `time` anima cauda/peitoral e o shimmer iridescente; `phase` dessincroniza
 * o nado quando há vários peixes na tela.
 */
export function drawFish(ctx, seed, traits, time = 0, phase = 0) {
  const m = traits.movement;
  const tailPeriod = periodOf(m.tailSpeed, MOVEMENT_TUNING.tailPeriodMax, MOVEMENT_TUNING.tailPeriodMin);
  const finPeriod = periodOf(m.finSpeed, MOVEMENT_TUNING.finPeriodMax, MOVEMENT_TUNING.finPeriodMin);

  const prevSmoothing = ctx.imageSmoothingEnabled;
  ctx.imageSmoothingEnabled = true;

  // Cauda: onda viajante (a estrela do efeito), fase e período do tailSpeed
  wavyBlit(ctx, getPartSprite(seed, "tail", traits.tail, tailPath, TAIL_BBOX),
    MOVEMENT_TUNING.tailWave, MOVEMENT_TUNING.tailWave.ampBase * m.tailAmplitude,
    tailPeriod, time, phase);

  // Dorsal: flutter sutil ondulando junto com o corpo (fase da cauda)
  wavyBlit(ctx, getPartSprite(seed, "dorsal", traits.dorsal, dorsalPath, DORSAL_BBOX),
    MOVEMENT_TUNING.dorsalWave, MOVEMENT_TUNING.dorsalWave.ampBase * m.tailAmplitude,
    tailPeriod, time, phase);

  const bodyGrad = ctx.createLinearGradient(0, BODY_BBOX[1], 0, BODY_BBOX[1] + BODY_BBOX[3]);
  bodyGrad.addColorStop(0, "#9aa1a9");
  bodyGrad.addColorStop(0.55, "#7d848d");
  bodyGrad.addColorStop(1, "#5f666f");
  ctx.fillStyle = bodyGrad;
  ctx.fill(bodyPath);
  ctx.strokeStyle = "rgba(0,0,0,0.3)";
  ctx.lineWidth = 2;
  ctx.stroke(bodyPath);

  drawShimmer(ctx, traits, time);

  ctx.fillStyle = "#f2f5f7";
  ctx.beginPath(); ctx.arc(184, 198, 9, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = "#14181d";
  ctx.beginPath(); ctx.arc(186, 199, 4.5, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = "rgba(255,255,255,0.85)";
  ctx.beginPath(); ctx.arc(184, 196.5, 1.6, 0, Math.PI * 2); ctx.fill();

  // Peitoral: flutter sutil, período e fase próprios do finSpeed
  wavyBlit(ctx, getPartSprite(seed, "pectoral", traits.pectoral, pectoralPath, PECTORAL_BBOX),
    MOVEMENT_TUNING.pectoralWave, MOVEMENT_TUNING.pectoralWave.ampBase * m.finAmplitude,
    finPeriod, time, phase * 1.7);

  ctx.imageSmoothingEnabled = prevSmoothing;
}

// ==================== Ambiente do aquário ====================
// Cena imersiva: profundidade de água, raios de luz cáustica, plantas, substrato,
// partículas e vidro. Deterministic por índice → layout estável entre frames.

const hash01 = (n) => {
  const x = Math.sin(n * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
};

// Interpolação de cor hex → hex (pra água limpa → turva conforme a qualidade)
function hexToRgb(h) {
  if (h.startsWith("rgb")) return h.match(/[\d.]+/g).map(Number); // mixHex encadeado devolve "rgb(...)"
  const n = parseInt(h.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
function mixHex(a, b, t) {
  const ca = hexToRgb(a), cb = hexToRgb(b);
  const r = Math.round(ca[0] + (cb[0] - ca[0]) * t);
  const g = Math.round(ca[1] + (cb[1] - ca[1]) * t);
  const bl = Math.round(ca[2] + (cb[2] - ca[2]) * t);
  return `rgb(${r}, ${g}, ${bl})`;
}
// Fator de sujeira 0 (água limpa) → 1 (água podre). A água só COMEÇA a ficar feia
// abaixo de MURK_CLEAN_ABOVE (acima disso está visualmente limpa); daí desce
// linearmente até podre em 0.
const MURK_CLEAN_ABOVE = 80;
const murkOf = (quality) =>
  Math.min(1, Math.max(0, (MURK_CLEAN_ABOVE - quality) / MURK_CLEAN_ABOVE));

// Clumps de plantas (posições fixas). back = atrás dos peixes, front = na frente.
const PLANTS_BACK = [
  { x: 0.08, h: 210, blades: 6, hue: 168 },
  { x: 0.30, h: 150, blades: 5, hue: 150 },
  { x: 0.62, h: 240, blades: 7, hue: 172 },
  { x: 0.86, h: 180, blades: 6, hue: 158 },
];
const PLANTS_FRONT = [
  { x: 0.19, h: 130, blades: 5, hue: 150 },
  { x: 0.74, h: 160, blades: 6, hue: 164 },
];

// Decoração extra por faixa de capacidade (decorTier 0/1/2 — CLAUDE.md §8.15/16): tanque
// maior "parece" maior por causa da decoração mais rica, não por mudar a área de nado.
// Tier 0 (Aquário) não desenha nada daqui — visual idêntico ao original.
const PLANTS_BACK_TIER1 = [{ x: 0.46, h: 260, blades: 7, hue: 176 }];
const PLANTS_FRONT_TIER1 = [{ x: 0.48, h: 150, blades: 5, hue: 168 }];
const ROCKS_TIER1 = [
  { x: 0.14, w: 46, h: 30 }, { x: 0.53, w: 60, h: 38 }, { x: 0.80, w: 40, h: 26 },
];

function drawRockCluster(ctx, W, H) {
  for (const r of ROCKS_TIER1) {
    const cx = r.x * W;
    const cy = H - 34;
    ctx.fillStyle = "rgba(40, 52, 50, 0.85)";
    ctx.beginPath();
    ctx.ellipse(cx, cy, r.w, r.h, 0, Math.PI, 0);
    ctx.fill();
    ctx.fillStyle = "rgba(70, 88, 84, 0.5)";
    ctx.beginPath();
    ctx.ellipse(cx - r.w * 0.2, cy - r.h * 0.35, r.w * 0.4, r.h * 0.3, 0, Math.PI, 0);
    ctx.fill();
  }
}

/**
 * Baú do tesouro (tier 2, Aquário Master) — decoração ambiente, não interativa.
 * Fora do centro (não compete com a área de clique dos peixes) e com um brilho
 * pequeno e lento (não pulsa como os botões reais do jogo, ex: recompensa diária —
 * um halo grande pulsando lia como call-to-action clicável, o que confundia).
 */
function drawTreasureChest(ctx, W, H, time) {
  const cx = W * 0.66, baseY = H - 34, w = 64, h = 34;
  ctx.save();
  // corpo do baú
  ctx.fillStyle = "#5a3a1e";
  ctx.fillRect(cx - w / 2, baseY - h, w, h);
  ctx.fillStyle = "#7a5028";
  ctx.beginPath();
  ctx.ellipse(cx, baseY - h, w / 2, 12, 0, Math.PI, 0);
  ctx.fill();
  // fivela dourada
  ctx.fillStyle = "#d9b24c";
  ctx.fillRect(cx - 5, baseY - h - 3, 10, 15);
  // dois glints pequenos e lentos, sem halo grande — leitura de "reflexo", não "botão"
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const glints = [
    { dx: -w * 0.22, dy: -h * 0.85, r: 5, speed: 2600, phase: 0 },
    { dx: w * 0.18, dy: -h * 0.55, r: 3.5, speed: 3100, phase: 1.7 },
  ];
  for (const g of glints) {
    const tw = 0.18 + 0.2 * Math.max(0, Math.sin(time / g.speed + g.phase));
    const gx = cx + g.dx, gy = baseY - h + g.dy;
    const glow = ctx.createRadialGradient(gx, gy, 0, gx, gy, g.r * 3);
    glow.addColorStop(0, `rgba(255, 224, 140, ${tw})`);
    glow.addColorStop(1, "rgba(255, 224, 140, 0)");
    ctx.fillStyle = glow;
    ctx.beginPath();
    ctx.arc(gx, gy, g.r * 3, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
  ctx.restore();
}

function drawPlantClump(ctx, baseX, baseY, clump, time, alpha, sat, light) {
  const { h, blades, hue } = clump;
  for (let b = 0; b < blades; b++) {
    const t = blades > 1 ? b / (blades - 1) : 0.5;
    const bh = h * (0.55 + 0.5 * hash01(baseX * 0.7 + b * 3.1));
    const lean = (t - 0.5) * bh * 0.5;
    const sway = Math.sin(time / 1300 + baseX * 0.02 + b * 0.8) * (10 + bh * 0.06);
    const cx = baseX + lean;
    ctx.beginPath();
    ctx.moveTo(baseX + lean * 0.2, baseY);
    ctx.quadraticCurveTo(cx + sway * 0.5, baseY - bh * 0.55, cx * 1 + lean * 0.4 + sway, baseY - bh);
    ctx.lineWidth = 6 * (1 - t * 0.4);
    ctx.lineCap = "round";
    ctx.strokeStyle = `hsla(${hue + b * 4}, ${sat}%, ${light}%, ${alpha})`;
    ctx.stroke();
  }
}

/** Tinge levemente de rosa/quente pro tema "breeding" (ninho) — sutil, sem exagero. */
const romanticTint = (hex, theme) => (theme === "breeding" ? mixHex(hex, "#ff6e93", 0.14) : hex);

/** Fundo: gradiente de água, raios de luz, plantas de trás, substrato. */
export function drawTankBackground(ctx, W, H, time, quality = 100, theme = "default", decorTier = 0) {
  const murk = murkOf(quality);

  // 1. Profundidade da água — limpa (teal escuro) → turva (verde-podre) conforme a sujeira
  const water = ctx.createLinearGradient(0, 0, 0, H);
  water.addColorStop(0, romanticTint(mixHex("#0e4d5b", "#2f4420", murk), theme));
  water.addColorStop(0.35, romanticTint(mixHex("#0a3543", "#243714", murk), theme));
  water.addColorStop(0.72, romanticTint(mixHex("#072530", "#182611", murk), theme));
  water.addColorStop(1, romanticTint(mixHex("#03151c", "#0d160a", murk), theme));
  ctx.fillStyle = water;
  ctx.fillRect(0, 0, W, H);

  // Névoa verde (aumenta com a sujeira)
  if (murk > 0.02) {
    ctx.fillStyle = `rgba(90, 130, 45, ${0.28 * murk})`;
    ctx.fillRect(0, 0, W, H);
  }

  // 2. Brilho da superfície + linha d'água ondulando
  const surf = ctx.createLinearGradient(0, 0, 0, 70);
  surf.addColorStop(0, "rgba(150, 240, 235, 0.22)");
  surf.addColorStop(1, "rgba(150, 240, 235, 0)");
  ctx.fillStyle = surf;
  ctx.fillRect(0, 0, W, 70);

  // 3. Raios de luz cáustica (diagonais, oscilando) — água suja bloqueia a luz.
  // Faixas maiores (decorTier ≥ 1) têm luz um pouco mais viva — tanque "mais nobre".
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const clarity = (1 - murk * 0.8) * (decorTier >= 1 ? 1.25 : 1);
  const beams = 4;
  for (let i = 0; i < beams; i++) {
    const sway = Math.sin(time / 4200 + i * 1.4) * 34;
    const x = (i + 0.5) * (W / beams) + sway;
    const grad = ctx.createLinearGradient(x, 0, x + 70, H * 0.92);
    grad.addColorStop(0, `rgba(130, 235, 224, ${(0.10 + 0.04 * Math.sin(time / 3000 + i)) * clarity})`);
    grad.addColorStop(1, "rgba(130, 235, 224, 0)");
    ctx.fillStyle = grad;
    ctx.beginPath();
    ctx.moveTo(x - 26, 0);
    ctx.lineTo(x + 26, 0);
    ctx.lineTo(x + 130, H * 0.92);
    ctx.lineTo(x + 10, H * 0.92);
    ctx.closePath();
    ctx.fill();
  }
  ctx.restore();

  // 4. Plantas de trás (silhueta escura, atrás dos peixes) — faixas maiores ganham
  // um clump extra e um agrupamento de rochas, reforçando a sensação de mais espaço.
  for (const clump of PLANTS_BACK)
    drawPlantClump(ctx, clump.x * W, H - 30, clump, time, 0.5, 42, 24);
  if (decorTier >= 1) {
    for (const clump of PLANTS_BACK_TIER1)
      drawPlantClump(ctx, clump.x * W, H - 30, clump, time, 0.5, 42, 24);
    drawRockCluster(ctx, W, H);
  }

  // 5. Substrato (areia/cascalho escuro)
  const bandH = 52;
  const sub = ctx.createLinearGradient(0, H - bandH, 0, H);
  sub.addColorStop(0, "rgba(12, 46, 44, 0)");
  sub.addColorStop(0.35, "#0c302e");
  sub.addColorStop(1, "#123a34");
  ctx.fillStyle = sub;
  ctx.fillRect(0, H - bandH, W, bandH);
  for (let i = 0; i < 46; i++) {
    const px = hash01(i * 3.3 + 1) * W;
    const py = H - hash01(i * 3.3 + 2) * bandH * 0.7;
    const r = 1.5 + hash01(i * 3.3 + 3) * 4;
    const l = 26 + hash01(i * 3.3 + 4) * 22;
    ctx.fillStyle = `hsl(${160 + hash01(i) * 20}, 24%, ${l}%)`;
    ctx.beginPath();
    ctx.ellipse(px, py, r, r * 0.72, 0, 0, Math.PI * 2);
    ctx.fill();
  }
}

function drawHeart(ctx, x, y, size, alpha) {
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.fillStyle = "#ff8fab";
  ctx.beginPath();
  ctx.moveTo(x, y + size * 0.3);
  ctx.bezierCurveTo(x, y, x - size, y, x - size, y + size * 0.35);
  ctx.bezierCurveTo(x - size, y + size * 0.7, x, y + size * 0.9, x, y + size * 1.1);
  ctx.bezierCurveTo(x, y + size * 0.9, x + size, y + size * 0.7, x + size, y + size * 0.35);
  ctx.bezierCurveTo(x + size, y, x, y, x, y + size * 0.3);
  ctx.fill();
  ctx.restore();
}

/** Frente: plantas da frente, partículas suspensas, bolhas, sujeira, vinheta de vidro. */
export function drawTankForeground(ctx, W, H, time, quality = 100, theme = "default", decorTier = 0) {
  const murk = murkOf(quality);

  // Plantas da frente (um pouco mais claras) — faixas maiores ganham decoração extra:
  // Grande soma mais um clump; Master soma o baú do tesouro (centerpiece, §8.15/16).
  for (const clump of PLANTS_FRONT)
    drawPlantClump(ctx, clump.x * W, H - 26, clump, time, 0.62, 46, 32);
  if (decorTier >= 1)
    for (const clump of PLANTS_FRONT_TIER1)
      drawPlantClump(ctx, clump.x * W, H - 26, clump, time, 0.62, 46, 32);
  if (decorTier >= 2)
    drawTreasureChest(ctx, W, H, time);

  // Algas/sujeira flutuando (mais e mais verdes conforme a água piora)
  if (murk > 0.05) {
    ctx.save();
    const flakes = Math.round(murk * 60);
    for (let i = 0; i < flakes; i++) {
      const drift = Math.sin(time / 2000 + i * 1.3) * 24;
      const px = (hash01(i * 1.7) * W + drift + W) % W;
      const py = (hash01(i * 2.3) * H + (time / 1000) * (3 + hash01(i) * 6)) % H;
      const r = 1 + hash01(i * 3.1) * 3.5;
      ctx.globalAlpha = 0.18 + hash01(i * 4.4) * 0.34 * murk;
      ctx.fillStyle = `hsl(${80 + hash01(i) * 40}, ${40 + murk * 30}%, ${28 + hash01(i * 2) * 16}%)`;
      ctx.beginPath();
      ctx.arc(px, py, r, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();

    // Película de algas nas bordas do vidro
    const edge = ctx.createLinearGradient(0, 0, 0, H);
    edge.addColorStop(0, `rgba(70, 110, 40, ${0.22 * murk})`);
    edge.addColorStop(0.15, "rgba(70, 110, 40, 0)");
    edge.addColorStop(0.85, "rgba(60, 95, 35, 0)");
    edge.addColorStop(1, `rgba(50, 85, 30, ${0.3 * murk})`);
    ctx.fillStyle = edge;
    ctx.fillRect(0, 0, W, H);
  }

  // Partículas em suspensão (detritos finos subindo devagar)
  ctx.save();
  ctx.fillStyle = "rgba(200, 235, 240, 0.5)";
  for (let i = 0; i < 44; i++) {
    const speed = 5 + hash01(i * 2.1) * 12;
    const px = (hash01(i * 2.7) * W + Math.sin(time / 2600 + i) * 16 + W) % W;
    const py = H - (((time / 1000) * speed + hash01(i * 1.9) * H) % (H + 20));
    ctx.globalAlpha = 0.12 + hash01(i) * 0.22;
    ctx.beginPath();
    ctx.arc(px, py, 0.6 + hash01(i * 5.1) * 1.4, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();

  // Bolhas (colunas subindo, oscilando)
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < 9; i++) {
    const col = hash01(i * 4.2) * W;
    const speed = 34 + hash01(i * 4.6) * 30;
    const bx = col + Math.sin(time / 900 + i * 2.3) * 10;
    const by = H - (((time / 1000) * speed + hash01(i * 3.3) * H) % (H + 40));
    const r = 1.6 + hash01(i * 2.2) * 3;
    ctx.strokeStyle = "rgba(180, 240, 245, 0.35)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(bx, by, r, 0, Math.PI * 2);
    ctx.stroke();
    ctx.fillStyle = "rgba(200, 245, 250, 0.10)";
    ctx.fill();
  }
  ctx.restore();

  // Corações subindo, bem sutis — só no tema "breeding" (ninho)
  if (theme === "breeding") {
    ctx.save();
    ctx.globalCompositeOperation = "lighter";
    for (let i = 0; i < 6; i++) {
      const col = hash01(i * 5.7 + 50) * W;
      const speed = 18 + hash01(i * 3.1 + 50) * 16;
      const bx = col + Math.sin(time / 1400 + i * 2.1) * 14;
      const by = H - (((time / 1000) * speed + hash01(i * 2.9 + 50) * H) % (H + 40));
      const size = 6 + hash01(i * 4.4 + 50) * 5;
      drawHeart(ctx, bx, by, size, 0.14 + hash01(i * 1.3 + 50) * 0.1);
    }
    ctx.restore();
  }

  // Vinheta + escurecimento das bordas (sensação de olhar pra dentro do vidro).
  // Master (decorTier 2) ganha um leve tom dourado na vinheta — tanque "mais nobre".
  const vg = ctx.createRadialGradient(W / 2, H * 0.46, H * 0.28, W / 2, H * 0.5, H * 0.92);
  vg.addColorStop(0, "rgba(0, 0, 0, 0)");
  vg.addColorStop(1, decorTier >= 2 ? "rgba(24, 16, 4, 0.6)" : "rgba(1, 12, 16, 0.6)");
  ctx.fillStyle = vg;
  ctx.fillRect(0, 0, W, H);

  // Reflexo de vidro no topo (leve faixa clara)
  const glass = ctx.createLinearGradient(0, 0, W * 0.4, H * 0.35);
  glass.addColorStop(0, "rgba(200, 245, 250, 0.06)");
  glass.addColorStop(1, "rgba(200, 245, 250, 0)");
  ctx.fillStyle = glass;
  ctx.fillRect(0, 0, W, H * 0.4);
}
