// Desde 13/08/2026 (traits congelados no nascimento), o backend calcula os traits UMA VEZ,
// no nascimento (fresco ou cruzado), e devolve eles prontos em `creature.traits` — o cliente
// não deriva mais nada do seed pra EXIBIR um peixe (fim da era de portar o motor C# pra JS
// bit-a-bit). O que sobra aqui:
//   - `generateTraits(seed)` — motor determinístico completo, só pra prévia visual de um seed
//     avulso (nenhum peixe real ainda existe pra ele).
//   - `traitDistribution`/`breedingPreview` — cálculo FECHADO (sem RNG) de "chance de cada
//     valor sair no filhote", usado na prévia do Ninho ANTES de confirmar o cruzamento (o
//     filhote ainda não existe, não tem seed nem traits reais).
//   - `traitsOf`/`rarityBreakdownOf` — agora leem os valores já resolvidos da criatura (vindos
//     da API), só recalculando probabilidade/pontos em cima deles — sem RNG, sem seed, sem
//     limite de profundidade de ancestralidade (esse problema inteiro deixou de existir).
//   - Fórmulas de produção/degradação (`coinsPerHourOf` etc.) — só display, motor é o servidor.

const SHA256 = (() => {
  const K = new Uint32Array([
    0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
    0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
    0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
    0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
    0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
    0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2]);
  const rotr = (x, n) => ((x >>> n) | (x << (32 - n))) >>> 0;

  function hash(bytes) {
    const len = bytes.length;
    const padded = new Uint8Array((((len + 8) >> 6) + 1) << 6);
    padded.set(bytes);
    padded[len] = 0x80;
    const dv = new DataView(padded.buffer);
    dv.setUint32(padded.length - 4, len * 8);

    const H = new Uint32Array([0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19]);
    const w = new Uint32Array(64);
    for (let off = 0; off < padded.length; off += 64) {
      for (let i = 0; i < 16; i++) w[i] = dv.getUint32(off + i * 4);
      for (let i = 16; i < 64; i++) {
        const s0 = rotr(w[i-15], 7) ^ rotr(w[i-15], 18) ^ (w[i-15] >>> 3);
        const s1 = rotr(w[i-2], 17) ^ rotr(w[i-2], 19) ^ (w[i-2] >>> 10);
        w[i] = (w[i-16] + s0 + w[i-7] + s1) >>> 0;
      }
      let [a,b,c,d,e,f,g,h] = H;
      for (let i = 0; i < 64; i++) {
        const S1 = rotr(e,6) ^ rotr(e,11) ^ rotr(e,25);
        const ch = (e & f) ^ (~e & g);
        const t1 = (h + S1 + ch + K[i] + w[i]) >>> 0;
        const S0 = rotr(a,2) ^ rotr(a,13) ^ rotr(a,22);
        const maj = (a & b) ^ (a & c) ^ (b & c);
        const t2 = (S0 + maj) >>> 0;
        h = g; g = f; f = e; e = (d + t1) >>> 0;
        d = c; c = b; b = a; a = (t1 + t2) >>> 0;
      }
      H[0]=(H[0]+a)>>>0; H[1]=(H[1]+b)>>>0; H[2]=(H[2]+c)>>>0; H[3]=(H[3]+d)>>>0;
      H[4]=(H[4]+e)>>>0; H[5]=(H[5]+f)>>>0; H[6]=(H[6]+g)>>>0; H[7]=(H[7]+h)>>>0;
    }
    const out = new Uint8Array(32);
    const odv = new DataView(out.buffer);
    for (let i = 0; i < 8; i++) odv.setUint32(i * 4, H[i]);
    return out;
  }
  return { hash };
})();

const textEncoder = new TextEncoder();

function hashU64(seed, salt) {
  const saltBytes = textEncoder.encode(salt);
  const input = new Uint8Array(8 + saltBytes.length);
  new DataView(input.buffer).setBigInt64(0, BigInt.asIntN(64, seed), true);
  input.set(saltBytes, 8);
  const digest = SHA256.hash(input);
  return new DataView(digest.buffer).getBigUint64(0, true);
}

export function roll01(seed, salt) {
  return Number(hashU64(seed, salt) >> 11n) * Math.pow(2, -53);
}

export const CONFIG = {
  shimmerTiers: [
    ["None", 78.0], ["Subtle", 15.0], ["Vibrant", 5.5], ["Rare", 1.3], ["Legendary", 0.2],
  ],
  shimmerColorsByTier: {
    Subtle: ["Gold", "Silver", "Bluish"],
    Vibrant: ["Emerald", "Purple", "Pink"],
    Rare: ["Rainbow", "AbsoluteBlack"],
    Legendary: ["Iridescent"],
  },
  shimmerOpacityByTier: { Subtle: [10, 25], Vibrant: [30, 50], Rare: [55, 75], Legendary: [80, 100] },
  partColors: [
    ["Orange", 22.0], ["Blue", 20.0], ["Red", 18.0], ["Yellow", 16.0],
    ["Green", 14.0], ["Purple", 6.0], ["Black", 3.0], ["PureWhite", 1.0],
  ],
  // Gradient: 0.6 -> 0.4 (12/08/2026, delta somado em None) — espelha TraitConfigV1 (manter em sincronia).
  patternTypes: [
    ["None", 76.2], ["Stripe", 8.0], ["Dot", 8.0], ["Scales", 3.0], ["Rays", 1.6],
    ["Chevron", 1.2], ["Net", 0.9], ["Gradient", 0.4], ["Mottled", 0.35],
    ["Ocellus", 0.2], ["Marble", 0.05],
  ],
  // Mistura de cores do Degradê (12/08/2026) — espelha TraitConfigV1.GradientMixRatios.
  // Even (50/50) é o mais raro; nos assimétricos só a cor dominante conta no score.
  gradientMixRatios: [
    ["BaseDominant", 45.0], ["Even", 10.0], ["PatternDominant", 45.0],
  ],
  shimmerScoreWeight: 2.5,
  setBonus: { samePattern2: 1.0, samePattern3: 2.5, sameColor2: 0.8, sameColor3: 2.0 },
  correlationBoostPoints: 15.0,
  sizeMean: 50.0, sizeStdDev: 20.0, sizeExtremeLow: 10.0, sizeExtremeHigh: 90.0,
  opacityMin: 20.0, opacityMax: 90.0, opacityExtremeLow: 30.0, opacityExtremeHigh: 80.0,
  movement: {
    speedMean: 50.0, speedStdDev: 20.0,
    speedExtremeLow: 10.0, speedExtremeHigh: 90.0, scoreWeight: 0.5,
    tailAmpMin: 0.20, tailAmpMax: 0.75,
    finAmpMin: 0.15, finAmpMax: 0.75,
  },
  // Renda por peixe — espelha IncomeCalculator/TickConfig (manter em sincronia)
  // taperScore/taperGrowth (12/08/2026): acima do piso Lendário, crescimento reduzido e
  // contínuo — espelha TickConfig.IncomeLegendaryTaperScore/Growth (manter em sincronia)
  income: { base: 1.5, growth: 0.42, ref: 4.0, taperScore: 14.0, taperGrowth: 0.10 },
  synergy: { perMatch: 0.15, maxBonus: 0.80 },
  // Venda ao NPC (vendor, §8.12) — espelha VendorCalculator/TickConfig (manter em sincronia)
  vendor: { hoursEquivalent: 2.0, minPrice: 1 },
  // Degradação da água (§8.2/8.6) — espelha TickConfig (DegradationPerMinute, DegradationPerFishFactor, manter em sincronia)
  degradation: { perMinute: 1 / 20, perFishFactor: 0.30, rarityRefScore: 5 },
  // Breeding — espelha BreedingDefaults (Gameplay/BreedingConfig.cs, manter em sincronia). Usado
  // só pela PRÉVIA (traitDistribution/breedingPreview, cálculo fechado antes de confirmar) — o
  // resultado real do cruzamento vem congelado da API, não é recalculado aqui.
  breeding: { mutationChance: 0.04, rarityBias: 0.15 },
  closestPartColor: {
    Gold: "Yellow", Silver: "PureWhite", Bluish: "Blue", Emerald: "Green",
    Purple: "Purple", Pink: "Red", Rainbow: "PureWhite", AbsoluteBlack: "Black",
    Iridescent: "PureWhite",
  },
};

function weightedPick(table, roll) {
  let total = 0;
  for (const [, w] of table) total += w;
  const target = roll * total;
  let cumulative = 0;
  for (const [value, w] of table) {
    cumulative += w;
    if (target < cumulative) return value;
  }
  return table[table.length - 1][0];
}

function normalPick(seed, salt, mean, stdDev) {
  const u1 = 1.0 - roll01(seed, salt);
  const u2 = roll01(seed, salt + "_phase");
  const z = Math.sqrt(-2.0 * Math.log(u1)) * Math.cos(2.0 * Math.PI * u2);
  return Math.min(100, Math.max(0, mean + stdDev * z));
}

function applyCorrelation(table, boosted) {
  if (boosted === null) return table;
  let boostedBase = 0;
  for (const [value, w] of table) if (value === boosted) boostedBase = w;
  const boostedNew = boostedBase + CONFIG.correlationBoostPoints;
  const othersScale = (100.0 - boostedNew) / (100.0 - boostedBase);
  return table.map(([value, w]) => [value, value === boosted ? boostedNew : w * othersScale]);
}

/** Motor determinístico completo (seed → traits) — só pra prévia visual de um seed avulso. */
export function generateTraits(seed) {
  const tier = weightedPick(CONFIG.shimmerTiers, roll01(seed, "body_shimmer"));

  let shimmerColor = null;
  let shimmerOpacity = 0;
  if (tier !== "None") {
    const palette = CONFIG.shimmerColorsByTier[tier];
    shimmerColor = palette[Math.floor(roll01(seed, "body_shimmer_color") * palette.length)];
    const [min, max] = CONFIG.shimmerOpacityByTier[tier];
    shimmerOpacity = min + roll01(seed, "body_shimmer_opacity") * (max - min);
  }

  const boosted = (tier === "Vibrant" || tier === "Rare" || tier === "Legendary")
    ? CONFIG.closestPartColor[shimmerColor]
    : null;
  const colorTable = applyCorrelation(CONFIG.partColors, boosted);

  function generatePart(partSalt) {
    const color = weightedPick(colorTable, roll01(seed, partSalt + "_color"));
    const pattern = weightedPick(CONFIG.patternTypes, roll01(seed, partSalt + "_pattern"));
    if (pattern === "None")
      return { color, pattern, patternColor: null, patternSize: null, patternOpacity: null, mix: null };

    const patternPalette = CONFIG.partColors.filter(([value]) => value !== color);
    const patternColor = weightedPick(patternPalette, roll01(seed, partSalt + "_pattern_color"));
    const size = normalPick(seed, partSalt + "_pattern_size", CONFIG.sizeMean, CONFIG.sizeStdDev);
    const opacity = CONFIG.opacityMin
      + roll01(seed, partSalt + "_pattern_opacity") * (CONFIG.opacityMax - CONFIG.opacityMin);
    const mix = pattern === "Gradient"
      ? weightedPick(CONFIG.gradientMixRatios, roll01(seed, partSalt + "_pattern_mix"))
      : null;
    return { color, pattern, patternColor, patternSize: size, patternOpacity: opacity, mix };
  }

  const mv = CONFIG.movement;
  const movement = {
    tailSpeed: normalPick(seed, "tail_speed", mv.speedMean, mv.speedStdDev),
    tailAmplitude: mv.tailAmpMin + roll01(seed, "tail_wag_amplitude") * (mv.tailAmpMax - mv.tailAmpMin),
    finSpeed: normalPick(seed, "fin_speed", mv.speedMean, mv.speedStdDev),
    finAmplitude: mv.finAmpMin + roll01(seed, "fin_wag_amplitude") * (mv.finAmpMax - mv.finAmpMin),
  };

  return {
    shimmerTier: tier,
    shimmerColor,
    shimmerOpacity,
    tail: generatePart("tail"),
    dorsal: generatePart("dorsal"),
    pectoral: generatePart("pectoral"),
    movement,
  };
}

/** Probabilidade de um valor já conhecido na tabela (peso/total), sem sortear. */
export function probabilityOf(table, value) {
  let total = 0, match = 0;
  for (const [v, w] of table) { total += w; if (v === value) match = w; }
  return match / total;
}

/** bias=0 é 50/50 puro; bias=1 pesa pelo inverso exato da probabilidade. */
export function biasedInheritProbability(probA, probB, bias) {
  if (bias <= 0) return 0.5;
  const wA = Math.pow(probA, -bias);
  const wB = Math.pow(probB, -bias);
  return wA / (wA + wB);
}

/**
 * Distribuição de probabilidade fechada (sem RNG) do valor herdado/mutado de
 * UM trait categórico, dado os valores dos dois pais — mesma matemática de
 * `ChildTierDistribution` (C#), generalizada pra qualquer tabela categórica
 * (cor de parte, tipo de padrão). Usada na prévia do ninho, ANTES de confirmar
 * o cruzamento (o filhote ainda não existe).
 */
export function traitDistribution(table, valueA, valueB, mutationChance, bias = 0) {
  const total = table.reduce((sum, [, w]) => sum + w, 0);
  const probA = probabilityOf(table, valueA);
  const probB = probabilityOf(table, valueB);
  const inheritAProb = (1 - mutationChance) * biasedInheritProbability(probA, probB, bias);
  const inheritBProb = (1 - mutationChance) - inheritAProb;

  const dist = new Map();
  for (const [value, w] of table) dist.set(value, mutationChance * (w / total));
  dist.set(valueA, (dist.get(valueA) ?? 0) + inheritAProb);
  dist.set(valueB, (dist.get(valueB) ?? 0) + inheritBProb);

  return [...dist.entries()]
    .map(([value, prob]) => ({ value, prob }))
    .sort((x, y) => y.prob - x.prob);
}

/**
 * Prévia completa do cruzamento: traits REAIS e já congelados dos dois pais
 * (`traitsOf`, direto de `creature.traits` — nenhuma reconstrução) + distribuição
 * de chances do filho por atributo (tier de brilho, cor e padrão de cada parte).
 * Não cobre os sub-traits contínuos (opacidade/tamanho do padrão, movimento) —
 * esses são herdados junto do padrão/tier escolhido ou resorteados só na mutação.
 */
export function breedingPreview(parentACreature, parentBCreature,
  mutationChance = CONFIG.breeding.mutationChance, rarityBias = CONFIG.breeding.rarityBias) {
  const a = traitsOf(parentACreature);
  const b = traitsOf(parentBCreature);

  const shimmerTier = traitDistribution(CONFIG.shimmerTiers, a.shimmerTier, b.shimmerTier, mutationChance, rarityBias);

  function partPreview(key) {
    const pa = a[key], pb = b[key];
    return {
      parentA: pa,
      parentB: pb,
      color: traitDistribution(CONFIG.partColors, pa.color, pb.color, mutationChance, rarityBias),
      pattern: traitDistribution(CONFIG.patternTypes, pa.pattern, pb.pattern, mutationChance, rarityBias),
    };
  }

  return {
    parentA: a, parentB: b, shimmerTier,
    tail: partPreview("tail"), dorsal: partPreview("dorsal"), pectoral: partPreview("pectoral"),
  };
}

/** Traits de uma criatura — já vêm congelados/resolvidos da API, nenhum cálculo aqui. */
export function traitsOf(creature) {
  return creature.traits;
}

// Backend serializa `TraitSourceEntry.Source` como "ParentA"/"ParentB"/"Mutation" (nome do
// enum C#, PascalCase) — a UI (CollectCelebration) espera "parentA"/"parentB"/"mutation".
const SOURCE_LABEL_MAP = { ParentA: "parentA", ParentB: "parentB", Mutation: "mutation" };

/** Índice `chave|parte -> source` a partir de `creature.breedingSource` (null pra peixe não-filhote ou pré-refatoração). */
function sourceIndex(breedingSource) {
  if (!breedingSource) return null;
  const map = new Map();
  for (const entry of breedingSource) map.set(entry.part ? `${entry.key}|${entry.part}` : entry.key, SOURCE_LABEL_MAP[entry.source] ?? null);
  return map;
}

/**
 * Decompõe o rarity score em fatores (o que faz o peixe ser raro), espelhando
 * TraitGenerator: score = Σ −log10(P). Cada fator: { key, part, value, probPct, points, source }.
 * O total ≈ rarityScore da API (mesma fórmula). Diferente de antes (13/08/2026): não sorteia
 * nada — os valores já vêm resolvidos em `creature.traits`, só recalcula a PROBABILIDADE de
 * cada valor já conhecido nas tabelas de peso (sem RNG, sem seed, sem limite de ancestralidade).
 * `source` (de onde veio cada atributo — pai A/B ou mutação) vem de `creature.breedingSource`
 * quando existir (null pra peixe fresco ou pra filhote nascido antes desta migração).
 */
export function rarityBreakdownOf(creature) {
  const traits = traitsOf(creature);
  const bySlot = sourceIndex(creature.breedingSource);
  const sourceFor = (key, part) => bySlot?.get(part ? `${key}|${part}` : key) ?? null;

  const factors = [];
  const selfInfo = (p) => -Math.log10(p);

  const tierProb = probabilityOf(CONFIG.shimmerTiers, traits.shimmerTier);
  factors.push({
    key: "shimmerTier", part: null, value: traits.shimmerTier, probPct: tierProb * 100,
    points: CONFIG.shimmerScoreWeight * selfInfo(tierProb), source: sourceFor("shimmerTier", null),
  });

  const boosted = (traits.shimmerTier === "Vibrant" || traits.shimmerTier === "Rare" || traits.shimmerTier === "Legendary")
    ? CONFIG.closestPartColor[traits.shimmerColor]
    : null;
  const colorTable = applyCorrelation(CONFIG.partColors, boosted);

  const partColors = [], partPatterns = [];
  for (const part of ["tail", "dorsal", "pectoral"]) {
    const p = traits[part];
    const colorProb = probabilityOf(colorTable, p.color);
    const colorFactor = {
      key: "partColor", part, value: p.color, probPct: colorProb * 100, points: selfInfo(colorProb),
      source: sourceFor("color", part),
    };
    factors.push(colorFactor);
    partColors.push(p.color);

    const patternProb = probabilityOf(CONFIG.patternTypes, p.pattern);
    factors.push({
      key: "patternType", part, value: p.pattern, probPct: patternProb * 100, points: selfInfo(patternProb),
      source: sourceFor("pattern", part),
    });
    partPatterns.push(p.pattern);
    if (p.pattern === "None") continue;

    // Cor do padrão nunca ganha um slot de origem próprio no motor (herda junto do padrão,
    // mesma fonte) — mesma convenção de rótulo que "pattern" pra essa parte.
    const scoringPalette = CONFIG.partColors.filter(([v]) => v !== p.color);
    const patternColorProb = probabilityOf(scoringPalette, p.patternColor);
    const patternColorFactor = {
      key: "patternColor", part, value: p.patternColor, probPct: patternColorProb * 100, points: selfInfo(patternColorProb),
      source: sourceFor("pattern", part),
    };
    factors.push(patternColorFactor);

    if (p.patternSize < CONFIG.sizeExtremeLow)
      factors.push({ key: "patternSizeExtreme", part, value: "pequeno", probPct: normalCdf(CONFIG.sizeExtremeLow, CONFIG.sizeMean, CONFIG.sizeStdDev) * 100, points: selfInfo(normalCdf(CONFIG.sizeExtremeLow, CONFIG.sizeMean, CONFIG.sizeStdDev)) });
    else if (p.patternSize > CONFIG.sizeExtremeHigh)
      factors.push({ key: "patternSizeExtreme", part, value: "grande", probPct: (1 - normalCdf(CONFIG.sizeExtremeHigh, CONFIG.sizeMean, CONFIG.sizeStdDev)) * 100, points: selfInfo(1 - normalCdf(CONFIG.sizeExtremeHigh, CONFIG.sizeMean, CONFIG.sizeStdDev)) });

    const range = CONFIG.opacityMax - CONFIG.opacityMin;
    if (p.patternOpacity < CONFIG.opacityExtremeLow) {
      const prob = (CONFIG.opacityExtremeLow - CONFIG.opacityMin) / range;
      factors.push({ key: "patternOpacityExtreme", part, value: "baixa", probPct: prob * 100, points: selfInfo(prob) });
    } else if (p.patternOpacity > CONFIG.opacityExtremeHigh) {
      const prob = (CONFIG.opacityMax - CONFIG.opacityExtremeHigh) / range;
      factors.push({ key: "patternOpacityExtreme", part, value: "alta", probPct: prob * 100, points: selfInfo(prob) });
    }

    if (p.pattern === "Gradient" && p.mix != null) {
      const mixProb = probabilityOf(CONFIG.gradientMixRatios, p.mix);
      factors.push({ key: "gradientMix", part, value: p.mix, probPct: mixProb * 100, points: selfInfo(mixProb) });
      // Só a cor DOMINANTE conta — zera o fator da minoritária (sem tirar da lista,
      // pra UI mostrar "por que não conta" em vez de simplesmente sumir).
      if (p.mix === "PatternDominant") {
        colorFactor.points = 0;
        colorFactor.note = "não contabilizado (degradê assimétrico)";
      } else if (p.mix === "BaseDominant") {
        patternColorFactor.points = 0;
        patternColorFactor.note = "não contabilizado (degradê assimétrico)";
      }
    }
  }

  const mv = CONFIG.movement;
  for (const [which, speed] of [["tail", traits.movement.tailSpeed], ["fin", traits.movement.finSpeed]]) {
    let prob = null, value = null;
    if (speed < mv.speedExtremeLow) { prob = normalCdf(mv.speedExtremeLow, mv.speedMean, mv.speedStdDev); value = "lenta"; }
    else if (speed > mv.speedExtremeHigh) { prob = 1 - normalCdf(mv.speedExtremeHigh, mv.speedMean, mv.speedStdDev); value = "rápida"; }
    if (prob !== null)
      factors.push({ key: "speedExtreme", part: which, value, probPct: prob * 100, points: mv.scoreWeight * selfInfo(prob) });
  }

  // Bônus de conjunto coeso (mesmo padrão / mesma cor entre partes)
  const sb = CONFIG.setBonus;
  const maxGroup = (arr) => {
    const counts = {};
    let max = 0;
    for (const v of arr) { counts[v] = (counts[v] || 0) + 1; max = Math.max(max, counts[v]); }
    return max;
  };
  const nonNone = partPatterns.filter((p) => p !== "None");
  const patMatch = nonNone.length ? maxGroup(nonNone) : 0;
  if (patMatch === 3) factors.push({ key: "samePattern", part: null, value: "3", probPct: null, points: sb.samePattern3 });
  else if (patMatch === 2) factors.push({ key: "samePattern", part: null, value: "2", probPct: null, points: sb.samePattern2 });
  const colMatch = maxGroup(partColors);
  if (colMatch === 3) factors.push({ key: "sameColor", part: null, value: "3", probPct: null, points: sb.sameColor3 });
  else if (colMatch === 2) factors.push({ key: "sameColor", part: null, value: "2", probPct: null, points: sb.sameColor2 });

  const total = factors.reduce((s, f) => s + f.points, 0);
  factors.sort((a, b) => b.points - a.points);
  return { total, factors };
}

// ---------- Produção e breakdown de raridade (só display; motor é o servidor) ----------

/** Moedas/hora que o peixe rende a água cheia (espelha IncomeCalculator.CoinsPerHour). */
export function coinsPerHourOf(rarityScore, synergyMult = 1) {
  const i = CONFIG.income;
  if (rarityScore <= i.taperScore)
    return i.base * Math.exp(i.growth * (rarityScore - i.ref)) * synergyMult;
  const floorAtTaper = i.base * Math.exp(i.growth * (i.taperScore - i.ref));
  return floorAtTaper * Math.exp(i.taperGrowth * (rarityScore - i.taperScore)) * synergyMult;
}

/** Multiplicador de sinergia pra N peixes da mesma cor de cauda (espelha o servidor). */
export function synergyMultiplier(sameColorCount) {
  const s = CONFIG.synergy;
  return 1 + Math.min(s.maxBonus, s.perMatch * Math.max(0, sameColorCount - 1));
}

/** Preço de venda ao NPC — deliberadamente baixo, espelha VendorCalculator (só display). */
export function vendorPriceOf(rarityScore) {
  const v = CONFIG.vendor;
  const price = Math.round(coinsPerHourOf(rarityScore) * v.hoursEquivalent);
  return Math.max(v.minPrice, price);
}

/**
 * Quanto UM peixe no tanque acelera a degradação da água, em pontos de qualidade/hora.
 * A fórmula do servidor é base·(1 + fator·pesoTotal)·fatorDaFaixa — cada peixe soma
 * `score/rarityRefScore` ao peso total (08/08/2026: era 1 fixo por peixe, agora quem
 * rende mais suja mais). `bandFactor` vem do backend (`tank.capacityBandDegradationFactor`,
 * CapacityBands) — não duplicamos as faixas aqui, só recebemos o fator já resolvido.
 */
export function waterDegradationPerFishPerHour(rarityScore, bandFactor = 1) {
  const d = CONFIG.degradation;
  const weight = rarityScore / d.rarityRefScore;
  return d.perMinute * d.perFishFactor * weight * bandFactor * 60;
}

function erf(x) {
  const sign = Math.sign(x);
  x = Math.abs(x);
  const t = 1.0 / (1.0 + 0.3275911 * x);
  const y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
            * t * Math.exp(-x * x);
  return sign * y;
}
function normalCdf(x, mean, stdDev) {
  return 0.5 * (1.0 + erf((x - mean) / (stdDev * Math.SQRT2)));
}
