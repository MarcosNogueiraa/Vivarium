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

// Focinho arredondado (não é mais um único vértice em V — as duas curvas
// aproximam de pontos próximos, ligados por um pequeno arco na ponta), mesma
// silhueta única compartilhada por todo peixe da espécie.
const bodyPath = new Path2D(
  "M 134 199 C 158 150 235 125 305 138 C 348 147 376 178 384 210 " +
  "C 376 242 348 273 305 282 C 235 295 158 270 134 221 " +
  "Q 122 210 134 199 Z");
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
const BODY_BBOX = [122, 124, 266, 172];

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

/**
 * Grade de arcos de escama dentro de `bbox` — compartilhada pelo padrão "Scales"
 * (trait sorteado, cor/tamanho variam) e pela textura fixa e sutil do corpo
 * (sempre presente, mesma pra todo peixe da espécie).
 */
function drawScaleTexture(ctx, bbox, r, strokeStyle, lineWidth) {
  const [bx, by, bw, bh] = bbox;
  const stepX = r * 1.8, stepY = r * 1.05;
  ctx.lineWidth = lineWidth;
  ctx.strokeStyle = strokeStyle;
  for (let row = 0, y = by; y < by + bh + stepY; row++, y += stepY) {
    const off = row % 2 ? stepX / 2 : 0;
    for (let x = bx + off; x < bx + bw + stepX; x += stepX) {
      ctx.beginPath();
      ctx.arc(x, y, r, Math.PI * 0.15, Math.PI * 0.85);
      ctx.stroke();
    }
  }
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
    drawScaleTexture(ctx, bbox, r, color, Math.max(1, r * 0.16));
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

// Ponto de encaixe (onde a nadadeira nasce do corpo) e o leque de ângulos dos
// raios, por nadadeira — mesmos pontos de partida/fechamento dos Path2D acima
// (ex: tailPath começa/fecha em ~378,205 — é onde a cauda "nasce"). Ângulos em
// radianos, 0 = +x (canvas). Igual pra TODO peixe da espécie (só cor/padrão
// variam) — não introduz variação de forma entre indivíduos.
const FIN_RAYS = {
  tail: { joint: [378, 206], angleFrom: -0.85, angleTo: 0.85, length: 74, count: 6 },
  dorsal: { joint: [228, 138], angleFrom: -2.15, angleTo: -1.05, length: 62, count: 5 },
  pectoral: { joint: [228, 244], angleFrom: 0.35, angleTo: 1.15, length: 48, count: 4 },
};

/** Linhas finas translúcidas irradiando do encaixe — dá leitura de "raio de nadadeira" ao blob sólido. */
function drawFinRays(ctx, seed, name, path) {
  const spec = FIN_RAYS[name];
  if (!spec) return;
  const { joint, angleFrom, angleTo, length, count } = spec;
  const [jx, jy] = joint;

  ctx.save();
  ctx.clip(path);
  ctx.strokeStyle = "rgba(255, 255, 255, 0.22)";
  ctx.lineWidth = 1.1;
  ctx.lineCap = "round";
  for (let i = 0; i < count; i++) {
    const t = count > 1 ? i / (count - 1) : 0.5;
    const jitter = (roll01(seed, `finray_${name}_${i}`) - 0.5) * 0.12;
    const angle = angleFrom + (angleTo - angleFrom) * t + jitter;
    const len = length * (0.85 + roll01(seed, `finraylen_${name}_${i}`) * 0.25);
    ctx.beginPath();
    ctx.moveTo(jx, jy);
    ctx.lineTo(jx + Math.cos(angle) * len, jy + Math.sin(angle) * len);
    ctx.stroke();
  }
  ctx.restore();
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
  drawFinRays(sctx, seed, name, path);

  const sprite = { canvas, ox: bx - M, oy: by - M };
  spriteCache.set(key, sprite);
  return sprite;
}

// Corpo: gradiente + textura de escama são IDÊNTICOS pra todo peixe (não
// dependem de seed nem trait — só o shimmer varia, e esse continua desenhado
// ao vivo por cima). Cacheado uma única vez (chave fixa), não por peixe —
// antes disso a textura de escama redesenhava ~288 arcos por peixe TODO
// FRAME (sem cache nenhum), o gargalo real por trás do nado "travado" com
// vários peixes no tanque (08/08/2026).
let bodySprite = null;

function getBodySprite() {
  if (bodySprite) return bodySprite;

  const M = MOVEMENT_TUNING.spriteMargin;
  const [bx, by, bw, bh] = BODY_BBOX;
  const canvas = document.createElement("canvas");
  canvas.width = bw + 2 * M;
  canvas.height = bh + 2 * M;
  const sctx = canvas.getContext("2d");
  sctx.translate(-bx + M, -by + M);

  const bodyGrad = sctx.createLinearGradient(0, by, 0, by + bh);
  bodyGrad.addColorStop(0, "#9aa1a9");
  bodyGrad.addColorStop(0.55, "#7d848d");
  bodyGrad.addColorStop(1, "#5f666f");
  sctx.fillStyle = bodyGrad;
  sctx.fill(bodyPath);
  sctx.strokeStyle = "rgba(0,0,0,0.3)";
  sctx.lineWidth = 2;
  sctx.stroke(bodyPath);

  sctx.save();
  sctx.clip(bodyPath);
  sctx.globalAlpha = 0.09;
  drawScaleTexture(sctx, BODY_BBOX, 9, "#e8edf1", 1);
  sctx.restore();

  bodySprite = { canvas, ox: bx - M, oy: by - M };
  return bodySprite;
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

  // Corpo (gradiente + textura de escama) é um sprite cacheado — idêntico pra
  // todo peixe, nunca muda frame a frame (só o shimmer, desenhado ao vivo
  // logo abaixo, varia). Antes disso a textura de escama era redesenhada do
  // zero (centenas de arcos) a cada frame pra cada peixe — gargalo real com
  // vários peixes no tanque.
  const body = getBodySprite();
  ctx.drawImage(body.canvas, body.ox, body.oy);

  drawShimmer(ctx, traits, time);

  ctx.fillStyle = "#f2f5f7";
  ctx.beginPath(); ctx.arc(184, 198, 9, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = "#14181d";
  ctx.beginPath(); ctx.arc(186, 199, 4.5, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = "rgba(255,255,255,0.85)";
  ctx.beginPath(); ctx.arc(184, 196.5, 1.6, 0, Math.PI * 2); ctx.fill();

  // Guelra: crescente fixo atrás da cabeça, sempre presente (mesmo detalhe pra
  // todo peixe da espécie) — leve pulso de opacidade simulando a respiração.
  const gillBreath = 0.18 + 0.08 * Math.sin(time / 1100 + phase);
  ctx.save();
  ctx.clip(bodyPath);
  ctx.strokeStyle = `rgba(30, 38, 44, ${gillBreath})`;
  ctx.lineWidth = 2.5;
  ctx.lineCap = "round";
  ctx.beginPath();
  ctx.arc(222, 205, 20, Math.PI * 0.62, Math.PI * 1.38);
  ctx.stroke();
  ctx.restore();

  // Bolha ocasional da boca — puramente determinística por seed+time (sem
  // estado extra pra guardar): um ciclo de repouso longo, uma janela curta
  // onde a bolha sobe e desaparece.
  if (time > 0) {
    const bubblePeriod = 4200 + roll01(seed, "bubble_period") * 5200;
    const bubblePhase = roll01(seed, "bubble_phase") * bubblePeriod;
    const bubbleT = (time + bubblePhase) % bubblePeriod;
    const bubbleDur = 1100;
    if (bubbleT < bubbleDur) {
      const p = bubbleT / bubbleDur;
      const bx = 170 + Math.sin(p * 6) * 3;
      const by = 197 - p * 32;
      ctx.save();
      ctx.globalAlpha = (1 - p) * 0.5;
      ctx.strokeStyle = "rgba(200,240,245,0.8)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(bx, by, 1.6 + p * 1.4, 0, Math.PI * 2);
      ctx.stroke();
      ctx.restore();
    }
  }

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

/**
 * Manchas de alga sobre um item de decoração — aparecem e crescem conforme a
 * água suja (`murk`, o mesmo fator 0-1 que já esverdeia a água/névoa/bordas do
 * vidro). Abaixo de ~10% de sujeira não desenha nada (água "limpa" não tem
 * mancha visível, mesmo padrão de corte do MURK_CLEAN_ABOVE).
 *
 * TODO (futuro, não implementado): "cascudo" é um PEIXE novo (uma espécie/criatura
 * que nadaria no tanque — diferente do filtro automático, que é item de loja/
 * equipamento; hook do bônus dele já documentado em GameService.cs/CLAUDE.md
 * §8.15/16). O efeito visual do cascudo seria reduzir/limpar essas manchas onde
 * ele passa — reaproveitar `drawAlgaePatches`/`murk` em vez de criar sistema novo.
 */
function drawAlgaePatches(ctx, spots, murk) {
  if (murk <= 0.1) return;
  ctx.save();
  ctx.globalCompositeOperation = "multiply";
  for (const [x, y, r] of spots) {
    ctx.fillStyle = `rgba(70, 120, 40, ${Math.min(0.5, murk * 0.6)})`;
    ctx.beginPath();
    ctx.ellipse(x, y, r * murk, r * murk * 0.55, 0.3, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}

function drawRockCluster(ctx, W, H, murk) {
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
    drawAlgaePatches(ctx, [[cx - r.w * 0.3, cy - r.h * 0.5, r.w * 0.45], [cx + r.w * 0.25, cy - r.h * 0.25, r.w * 0.3]], murk);
  }
}

/**
 * Navio afundado (tier 2, Aquário Master) — desenhado no FOREGROUND (depois dos
 * peixes, CLAUDE.md §8.16), então qualquer peixe que passe por trás da silhueta
 * fica coberto por ela — dá a sensação de profundidade/camadas que só decoração
 * de fundo (sempre atrás de tudo) não consegue.
 */
function drawSunkenShip(ctx, W, H, time, murk) {
  const cx = W * 0.20, baseY = H - 30;
  ctx.save();
  ctx.translate(cx, baseY);
  ctx.rotate(-0.10); // tombado de lado, meio enterrado no substrato

  // Casco
  ctx.fillStyle = "#2e2622";
  ctx.beginPath();
  ctx.moveTo(-95, 0);
  ctx.quadraticCurveTo(-100, -34, -60, -42);
  ctx.lineTo(70, -40);
  ctx.quadraticCurveTo(100, -30, 88, 0);
  ctx.closePath();
  ctx.fill();
  // Convés e casco superior, tom um pouco mais claro
  ctx.fillStyle = "#3d332c";
  ctx.beginPath();
  ctx.moveTo(-55, -40);
  ctx.lineTo(60, -38);
  ctx.lineTo(50, -54);
  ctx.lineTo(-40, -55);
  ctx.closePath();
  ctx.fill();
  // Vigias (portholes)
  ctx.fillStyle = "rgba(20, 60, 60, 0.7)";
  for (const px of [-30, 0, 30]) {
    ctx.beginPath();
    ctx.arc(px, -20, 6, 0, Math.PI * 2);
    ctx.fill();
  }
  // Algas cobrindo o casco — cresce com a água suja (mesmo helper das rochas).
  drawAlgaePatches(ctx, [[-40, -10, 34], [30, -18, 22]], murk);

  // Mastro quebrado + farrapo de vela balançando
  ctx.strokeStyle = "#3d332c";
  ctx.lineWidth = 6;
  ctx.beginPath();
  ctx.moveTo(20, -50);
  ctx.lineTo(30, -120);
  ctx.stroke();
  const sway = Math.sin(time / 1600) * 8;
  ctx.fillStyle = "rgba(150, 160, 150, 0.28)";
  ctx.beginPath();
  ctx.moveTo(30, -118);
  ctx.quadraticCurveTo(46 + sway, -100, 34, -80);
  ctx.quadraticCurveTo(30, -100, 30, -118);
  ctx.fill();

  ctx.restore();
}

/**
 * Baú do tesouro (tier 2, Aquário Master) — decoração ambiente, não interativa.
 * Fora do centro (não compete com a área de clique dos peixes) e com um brilho
 * pequeno e lento (não pulsa como os botões reais do jogo, ex: recompensa diária —
 * um halo grande pulsando lia como call-to-action clicável, o que confundia).
 */
function drawTreasureChest(ctx, W, H, time, murk) {
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
  drawAlgaePatches(ctx, [[cx - w * 0.28, baseY - h * 0.4, w * 0.32]], murk);
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

/**
 * Espinha de rocha alta (tier 2, Aquário Master) — obstáculo vertical no FOREGROUND
 * (fish passam por trás, mesma ideia do navio) pra quebrar o plano horizontal e
 * reforçar a sensação de profundidade/camadas.
 */
function drawObstacleSpire(ctx, x, H, hgt, murk) {
  const baseY = H - 28;
  ctx.save();
  ctx.fillStyle = "rgba(46, 58, 56, 0.92)";
  ctx.beginPath();
  ctx.moveTo(x - 26, baseY);
  ctx.quadraticCurveTo(x - 20, baseY - hgt * 0.55, x - 6, baseY - hgt);
  ctx.quadraticCurveTo(x + 4, baseY - hgt * 0.6, x + 22, baseY);
  ctx.closePath();
  ctx.fill();
  ctx.fillStyle = "rgba(80, 98, 92, 0.45)";
  ctx.beginPath();
  ctx.moveTo(x - 14, baseY);
  ctx.quadraticCurveTo(x - 10, baseY - hgt * 0.5, x - 4, baseY - hgt * 0.9);
  ctx.lineTo(x + 2, baseY - hgt * 0.85);
  ctx.quadraticCurveTo(x - 2, baseY - hgt * 0.4, x - 2, baseY);
  ctx.closePath();
  ctx.fill();
  drawAlgaePatches(ctx, [[x - 8, baseY - hgt * 0.3, 16], [x + 6, baseY - hgt * 0.55, 12]], murk);
  ctx.restore();
}

// Posição fixa do air stone (fração de W) — longe dos elementos do decorTier 2
// (navio ~0.10-0.29, espinha ~0.38-0.46, baú ~0.63-0.69, espinha ~0.88-0.93).
const AIR_STONE_X_FRAC = 0.335;

/** Corpo da pedra de ar (presente em toda faixa de capacidade) — desenhado uma vez, no foreground. */
function drawAirStoneBase(ctx, W, H) {
  const x = AIR_STONE_X_FRAC * W;
  const baseY = H - 30;
  ctx.save();
  ctx.fillStyle = "rgba(60, 66, 68, 0.9)";
  ctx.beginPath();
  ctx.ellipse(x, baseY, 11, 6, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = "rgba(90, 98, 100, 0.6)";
  ctx.beginPath();
  ctx.ellipse(x - 3, baseY - 1.5, 4, 2.2, 0, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}

const AIR_STONE_R_SPLIT = 1.9;

/**
 * Coluna de bolhas contínua nascendo sempre do mesmo ponto (diferente das
 * bolhas ambiente, que nascem espalhadas e aleatórias pela tela) — como um
 * aquário de verdade. `big` filtra qual metade desenhar: as pequenas entram
 * no fundo (`drawTankBackground`, atrás dos peixes) e as grandes na frente
 * (`drawTankForeground`) — dá noção de profundidade (bolha longe = pequena e
 * atrás; bolha perto do vidro = grande e na frente).
 */
function drawAirStoneBubbles(ctx, W, H, time, big) {
  const x = AIR_STONE_X_FRAC * W;
  const baseY = H - 30;
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  const count = 7;
  for (let i = 0; i < count; i++) {
    const r = 1.1 + hash01(i * 2.4 + 92) * 1.6;
    if (big !== (r >= AIR_STONE_R_SPLIT)) continue;
    const speed = 46 + hash01(i * 5.7 + 90) * 20;
    const cycle = H + 30;
    const by = baseY - (((time / 1000) * speed + hash01(i * 3.1 + 91) * cycle) % cycle);
    const sway = Math.sin(time / 500 + i * 1.9) * 3;
    const bx = x + sway;
    ctx.strokeStyle = "rgba(190, 245, 250, 0.4)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(bx, by, r, 0, Math.PI * 2);
    ctx.stroke();
    ctx.fillStyle = "rgba(210, 250, 255, 0.12)";
    ctx.fill();
  }
  ctx.restore();
}

const AMBIENT_BUBBLE_R_SPLIT = 3.1;

/**
 * Bolhas ambiente (nascem espalhadas pela tela, sem fonte fixa — diferente do
 * air stone). Mesmo split por tamanho: pequenas atrás dos peixes, grandes na
 * frente — reforça a sensação de profundidade (pedido do usuário, 08/08/2026).
 */
function drawAmbientBubbles(ctx, W, H, time, big) {
  ctx.save();
  ctx.globalCompositeOperation = "lighter";
  for (let i = 0; i < 9; i++) {
    const r = 1.6 + hash01(i * 2.2) * 3;
    if (big !== (r >= AMBIENT_BUBBLE_R_SPLIT)) continue;
    const col = hash01(i * 4.2) * W;
    const speed = 34 + hash01(i * 4.6) * 30;
    const bx = col + Math.sin(time / 900 + i * 2.3) * 10;
    const by = H - (((time / 1000) * speed + hash01(i * 3.3) * H) % (H + 40));
    ctx.strokeStyle = "rgba(180, 240, 245, 0.35)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(bx, by, r, 0, Math.PI * 2);
    ctx.stroke();
    ctx.fillStyle = "rgba(200, 245, 250, 0.10)";
    ctx.fill();
  }
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

/** Altura da faixa de substrato (areia/cascalho) no fundo do tanque — usada pelo chão da sombra dos peixes também. */
export const SUBSTRATE_BAND_H = 52;

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
    drawRockCluster(ctx, W, H, murk);
  }

  // 5. Substrato (areia/cascalho escuro) — grão com profundidade (luz/sombra
  // própria) e leve ondulação, como se a correnteza do filtro moldasse a areia.
  const bandH = SUBSTRATE_BAND_H;
  const sub = ctx.createLinearGradient(0, H - bandH, 0, H);
  sub.addColorStop(0, "rgba(12, 46, 44, 0)");
  sub.addColorStop(0.35, "#0c302e");
  sub.addColorStop(1, "#123a34");
  ctx.fillStyle = sub;
  ctx.fillRect(0, H - bandH, W, bandH);

  // Ondulações de areia: faixas onduladas bem sutis, fase deslizando devagar
  // (mesmo espírito da correnteza que já balança as plantas).
  ctx.save();
  ctx.globalCompositeOperation = "overlay";
  const ripples = 5;
  for (let i = 0; i < ripples; i++) {
    const ry = H - bandH * 0.15 - i * (bandH * 0.16);
    ctx.strokeStyle = `rgba(180, 210, 200, ${0.05 + 0.02 * (i % 2)})`;
    ctx.lineWidth = 2;
    ctx.beginPath();
    for (let x = 0; x <= W; x += 12) {
      const wobble = Math.sin(x / 46 + time / 5200 + i * 1.7) * 2.2;
      const py = ry + wobble;
      if (x === 0) ctx.moveTo(x, py); else ctx.lineTo(x, py);
    }
    ctx.stroke();
  }
  ctx.restore();

  for (let i = 0; i < 46; i++) {
    const px = hash01(i * 3.3 + 1) * W;
    const py = H - hash01(i * 3.3 + 2) * bandH * 0.7;
    const r = 1.5 + hash01(i * 3.3 + 3) * 4;
    const l = 26 + hash01(i * 3.3 + 4) * 22;
    ctx.fillStyle = `hsl(${160 + hash01(i) * 20}, 24%, ${l}%)`;
    ctx.beginPath();
    ctx.ellipse(px, py, r, r * 0.72, 0, 0, Math.PI * 2);
    ctx.fill();
    // Grãos maiores ganham luz/sombra própria — leitura de bolinha 3D, não um disco chapado.
    if (r > 3) {
      ctx.fillStyle = `hsla(${160 + hash01(i) * 20}, 30%, ${Math.min(78, l + 26)}%, 0.55)`;
      ctx.beginPath();
      ctx.ellipse(px - r * 0.3, py - r * 0.3, r * 0.4, r * 0.28, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = `hsla(${160 + hash01(i) * 20}, 24%, ${Math.max(8, l - 16)}%, 0.5)`;
      ctx.beginPath();
      ctx.ellipse(px + r * 0.3, py + r * 0.22, r * 0.42, r * 0.3, 0, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  // Metade "pequena" das bolhas (ambiente + air stone) nasce aqui, no fundo —
  // fica atrás de todo peixe, dando noção de profundidade (a outra metade,
  // maior, nasce no foreground — ver drawTankForeground).
  drawAmbientBubbles(ctx, W, H, time, false);
  drawAirStoneBubbles(ctx, W, H, time, false);
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
  if (decorTier >= 2) {
    drawSunkenShip(ctx, W, H, time, murk);
    drawObstacleSpire(ctx, W * 0.42, H, 92, murk);
    drawTreasureChest(ctx, W, H, time, murk);
    drawObstacleSpire(ctx, W * 0.90, H, 70, murk);
  }

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

  // Microfauna de fundo: pontinhos de vida ambiente perto do substrato, se
  // mexendo em voltas pequenas e erráticas (não sobem como as partículas de
  // detrito abaixo) — sugere um ecossistema vivo além dos peixes do jogador,
  // sem ser uma "espécie" nova (nenhum trait, nenhuma forma, só textura viva).
  ctx.save();
  ctx.fillStyle = "rgba(210, 235, 225, 0.9)";
  for (let i = 0; i < 16; i++) {
    const baseX = hash01(i * 7.1 + 80) * W;
    const baseY = H - SUBSTRATE_BAND_H * 0.55 - hash01(i * 7.1 + 81) * 34;
    const driftX = Math.sin(time / 1800 + i * 2.1) * 9 + Math.cos(time / 3300 + i) * 5;
    const driftY = Math.cos(time / 2200 + i * 1.4) * 4;
    ctx.globalAlpha = 0.08 + hash01(i * 3.7 + 82) * 0.14;
    ctx.beginPath();
    ctx.arc(baseX + driftX, baseY + driftY, 0.7 + hash01(i * 5.3 + 83) * 0.6, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();

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

  // Bolhas (colunas subindo, oscilando) — metade "grande" nasce aqui, na
  // frente dos peixes; a metade pequena já nasceu no fundo (drawTankBackground).
  drawAmbientBubbles(ctx, W, H, time, true);

  // Air stone: corpo sempre no foreground + metade grande da coluna de bolhas
  // (a pequena já nasceu atrás, no fundo).
  drawAirStoneBase(ctx, W, H);
  drawAirStoneBubbles(ctx, W, H, time, true);

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

/**
 * Sombra projetada no substrato (função pura, testável) — quanto mais fundo o
 * peixe está (perto do chão), maior e mais escura a sombra; perto da superfície,
 * quase some. `maxDist` é a distância (em px) acima da qual a sombra já está no
 * mínimo.
 */
export function shadowParamsFor(y, floorY, maxDist = 220) {
  const dist = Math.max(0, floorY - y);
  const t = Math.min(1, Math.max(0.15, 1 - dist / maxDist));
  return { radiusX: 14 + 30 * t, alpha: 0.08 + 0.26 * t };
}

/** Desenha a sombra elíptica de um peixe no substrato, na posição x do peixe. */
export function drawFishShadow(ctx, x, y, floorY) {
  const { radiusX, alpha } = shadowParamsFor(y, floorY);
  ctx.save();
  ctx.translate(x, floorY - 4);
  ctx.scale(1, 0.32);
  const grad = ctx.createRadialGradient(0, 0, 0, 0, 0, radiusX);
  grad.addColorStop(0, `rgba(2, 10, 8, ${alpha})`);
  grad.addColorStop(1, "rgba(2, 10, 8, 0)");
  ctx.fillStyle = grad;
  ctx.beginPath();
  ctx.arc(0, 0, radiusX, 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}
