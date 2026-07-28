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
  pattern: { None: "Sem padrão", Stripe: "Estria", Dot: "Bolinha", Gradient: "Degradê", Mottled: "Manchado" },
};

// Faixas calibradas via simulação (CLAUDE.md seção 5)
export const BANDS = [
  { max: 5.0, name: "Comum", color: "#93a7b0" },
  { max: 6.7, name: "Incomum", color: "#57b876" },
  { max: 8.4, name: "Raro", color: "#4d8fe0" },
  { max: 11.2, name: "Épico", color: "#a86ce4" },
  { max: Infinity, name: "Lendário", color: "#f0b93b" },
];
export const bandOf = (score) => BANDS.find((b) => score < b.max);

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
  tailWave: { ampBase: 34, waveNumber: 3.2, exp: 1.4, strips: 28, jointX: 380, len: 78 },
  dorsalWave: { ampBase: 10, waveNumber: 2.2, exp: 1.1, strips: 20, jointX: 224, len: 110 },
  pectoralWave: { ampBase: 14, waveNumber: 2.0, exp: 1.2, strips: 18, jointX: 220, len: 72 },
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
  if (time === 0 || ampTip === 0) {
    ctx.drawImage(canvas, ox, oy);
    return;
  }
  const basePhase = time / period + phaseArg;
  const stripW = canvas.width / cfg.strips;
  for (let i = 0; i < cfg.strips; i++) {
    const sx = i * stripW;
    const u = Math.min(1, Math.max(0, (ox + sx + stripW / 2 - cfg.jointX) / cfg.len));
    const amp = ampTip * Math.pow(u, cfg.exp);
    const offset = amp * Math.sin(basePhase - cfg.waveNumber * u);
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
