// Port fiel de Vivarium.Core.Generation (mesmo código verificado do protótipo:
// 2.000 seeds idênticos ao motor C# via `Vivarium.Simulation dump` + crosscheck).
// O backend é a fonte de verdade (rarity score vem da API); aqui só derivamos
// os traits visuais do seed pra renderizar.

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
  patternTypes: [
    ["None", 76.0], ["Stripe", 8.0], ["Dot", 8.0], ["Scales", 3.0], ["Rays", 1.6],
    ["Chevron", 1.2], ["Net", 0.9], ["Gradient", 0.6], ["Mottled", 0.35],
    ["Ocellus", 0.2], ["Marble", 0.05],
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
  income: { base: 1.5, growth: 0.42, ref: 4.0 },
  synergy: { perMatch: 0.15, maxBonus: 0.80 },
  // Venda ao NPC (vendor, §8.12) — espelha VendorCalculator/TickConfig (manter em sincronia)
  vendor: { hoursEquivalent: 2.0, minPrice: 1 },
  // Degradação da água (§8.2/8.6) — espelha TickConfig (DegradationPerMinute, DegradationPerFishFactor, manter em sincronia)
  degradation: { perMinute: 1 / 20, perFishFactor: 0.30, rarityRefScore: 5 },
  // Breeding — espelha BreedingDefaults (Gameplay/BreedingConfig.cs, manter em sincronia)
  breeding: { mutationChance: 0.08, rarityBias: 0.15, grandparentReachChance: 0.15 },
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
      return { color, pattern, patternColor: null, patternSize: null, patternOpacity: null };

    const patternPalette = CONFIG.partColors.filter(([value]) => value !== color);
    const patternColor = weightedPick(patternPalette, roll01(seed, partSalt + "_pattern_color"));
    const size = normalPick(seed, partSalt + "_pattern_size", CONFIG.sizeMean, CONFIG.sizeStdDev);
    const opacity = CONFIG.opacityMin
      + roll01(seed, partSalt + "_pattern_opacity") * (CONFIG.opacityMax - CONFIG.opacityMin);
    return { color, pattern, patternColor, patternSize: size, patternOpacity: opacity };
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

// ---------- Breeding: port de TraitGenerator.BreedTraits (Generation/TraitGenerator.cs) ----------
// Um filhote NÃO pode ser exibido com generateTraits(seed) — o seed dele é solto,
// sem relação com os pais. Precisa reconstruir via os seeds dos dois pais + as
// mesmas constantes de mutação/viés do servidor (CONFIG.breeding).

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

function inheritOrMutate(seed, salt, mutationChance, valueA, valueB, table, bias = 0) {
  const mutated = roll01(seed, salt + "_source") < mutationChance;
  if (mutated) return { value: weightedPick(table, roll01(seed, salt)), mutated: true, fromA: false };
  const probA = probabilityOf(table, valueA);
  const probB = probabilityOf(table, valueB);
  const threshold = biasedInheritProbability(probA, probB, bias);
  const fromA = roll01(seed, salt + "_inherit") < threshold;
  return { value: fromA ? valueA : valueB, mutated: false, fromA };
}

function inheritContinuousNormal(seed, salt, mutationChance, valueA, valueB, mean, stdDev) {
  if (roll01(seed, salt + "_source") < mutationChance) return normalPick(seed, salt, mean, stdDev);
  return roll01(seed, salt + "_inherit") < 0.5 ? valueA : valueB;
}
function inheritContinuousUniform(seed, salt, mutationChance, valueA, valueB, min, max) {
  if (roll01(seed, salt + "_source") < mutationChance) return min + roll01(seed, salt) * (max - min);
  return roll01(seed, salt + "_inherit") < 0.5 ? valueA : valueB;
}

function breedPart(seed, partSalt, mutationChance, rarityBias, a, b, colorTable) {
  const colorPick = inheritOrMutate(seed, partSalt + "_color", mutationChance, a.color, b.color, colorTable, rarityBias);
  const color = colorPick.value;

  const patternPick = inheritOrMutate(seed, partSalt + "_pattern", mutationChance, a.pattern, b.pattern, CONFIG.patternTypes, rarityBias);
  const pattern = patternPick.value;
  if (pattern === "None")
    return { color, pattern, patternColor: null, patternSize: null, patternOpacity: null };

  const patternSource = patternPick.fromA ? a : b;
  const subtraitsFromSource = !patternPick.mutated && patternSource.pattern === pattern && patternSource.patternColor !== color;

  let patternColor, patternSize, patternOpacity;
  if (subtraitsFromSource) {
    patternColor = patternSource.patternColor;
    patternSize = patternSource.patternSize;
    patternOpacity = patternSource.patternOpacity;
  } else {
    const patternPalette = CONFIG.partColors.filter(([v]) => v !== color);
    patternColor = weightedPick(patternPalette, roll01(seed, partSalt + "_pattern_color"));
    patternSize = normalPick(seed, partSalt + "_pattern_size", CONFIG.sizeMean, CONFIG.sizeStdDev);
    patternOpacity = CONFIG.opacityMin + roll01(seed, partSalt + "_pattern_opacity") * (CONFIG.opacityMax - CONFIG.opacityMin);
  }
  return { color, pattern, patternColor, patternSize, patternOpacity };
}

/** Traits reais de um pai — generateTraits direto se fresco, ou recomputa 1 nível (sem reach-back) se ele é filhote. */
export function resolveOwnTraits(seed, grandparentASeed, grandparentBSeed, mutationChance, rarityBias) {
  if (grandparentASeed != null && grandparentBSeed != null)
    return breedTraits(seed, grandparentASeed, grandparentBSeed, mutationChance, rarityBias);
  return generateTraits(seed);
}

/**
 * Com `reachChance` de chance (só se os avós existirem), troca o candidato desse lado pelo
 * de um dos avós (50/50 entre eles) em vez dos traits reais do pai direto. Retorna o objeto de
 * traits INTEIRO (não um trait solto) pra manter subtraits coerentes com a mesma fonte.
 */
export function effectiveParentTraits(childSeed, salt, own, grandparent1, grandparent2, reachChance) {
  if (grandparent1 == null || grandparent2 == null) return own;
  if (roll01(childSeed, salt + "_reach") >= reachChance) return own;
  return roll01(childSeed, salt + "_reach_which") < 0.5 ? grandparent1 : grandparent2;
}

/**
 * Traits do filhote — chame SEMPRE que `creature.isBred` for true, usando os
 * seeds dos pais (`ParentASeed`/`ParentBSeed` da API), nunca generateTraits(seed).
 * `parentXGrandparentYSeed` (opcionais, vêm do `CreatureDto` quando o pai correspondente é
 * ele mesmo um filhote): habilitam a chance de herdar um traço de um avô em vez do pai direto
 * (31/07/2026) — ver `TraitGenerator.BreedTraits`/CLAUDE.md 8.8 pro mecanismo espelhado aqui.
 */
export function breedTraits(childSeed, parentASeed, parentBSeed,
  mutationChance = CONFIG.breeding.mutationChance, rarityBias = CONFIG.breeding.rarityBias,
  parentAGrandparentASeed = null, parentAGrandparentBSeed = null,
  parentBGrandparentASeed = null, parentBGrandparentBSeed = null,
  grandparentReachChance = CONFIG.breeding.grandparentReachChance) {
  const ownA = resolveOwnTraits(parentASeed, parentAGrandparentASeed, parentAGrandparentBSeed, mutationChance, rarityBias);
  const ownB = resolveOwnTraits(parentBSeed, parentBGrandparentASeed, parentBGrandparentBSeed, mutationChance, rarityBias);
  const gpA1 = parentAGrandparentASeed != null ? generateTraits(parentAGrandparentASeed) : null;
  const gpA2 = parentAGrandparentBSeed != null ? generateTraits(parentAGrandparentBSeed) : null;
  const gpB1 = parentBGrandparentASeed != null ? generateTraits(parentBGrandparentASeed) : null;
  const gpB2 = parentBGrandparentBSeed != null ? generateTraits(parentBGrandparentBSeed) : null;

  const effA = (slotSalt) => effectiveParentTraits(childSeed, slotSalt + "_a", ownA, gpA1, gpA2, grandparentReachChance);
  const effB = (slotSalt) => effectiveParentTraits(childSeed, slotSalt + "_b", ownB, gpB1, gpB2, grandparentReachChance);

  const shimmerA = effA("body_shimmer"), shimmerB = effB("body_shimmer");
  const tierPick = inheritOrMutate(childSeed, "body_shimmer", mutationChance, shimmerA.shimmerTier, shimmerB.shimmerTier, CONFIG.shimmerTiers, rarityBias);
  const tier = tierPick.value;

  let shimmerColor = null, shimmerOpacity = 0;
  if (tier !== "None") {
    const tierSource = tierPick.fromA ? shimmerA : shimmerB;
    if (!tierPick.mutated && tierSource.shimmerTier === tier) {
      shimmerColor = tierSource.shimmerColor;
      shimmerOpacity = tierSource.shimmerOpacity;
    } else {
      const palette = CONFIG.shimmerColorsByTier[tier];
      shimmerColor = palette[Math.floor(roll01(childSeed, "body_shimmer_color") * palette.length)];
      const [min, max] = CONFIG.shimmerOpacityByTier[tier];
      shimmerOpacity = min + roll01(childSeed, "body_shimmer_opacity") * (max - min);
    }
  }

  const boosted = (tier === "Vibrant" || tier === "Rare" || tier === "Legendary")
    ? CONFIG.closestPartColor[shimmerColor]
    : null;
  const colorTable = applyCorrelation(CONFIG.partColors, boosted);

  const tailA = effA("tail"), tailB = effB("tail");
  const tail = breedPart(childSeed, "tail", mutationChance, rarityBias, tailA.tail, tailB.tail, colorTable);
  const dorsalA = effA("dorsal"), dorsalB = effB("dorsal");
  const dorsal = breedPart(childSeed, "dorsal", mutationChance, rarityBias, dorsalA.dorsal, dorsalB.dorsal, colorTable);
  const pectoralA = effA("pectoral"), pectoralB = effB("pectoral");
  const pectoral = breedPart(childSeed, "pectoral", mutationChance, rarityBias, pectoralA.pectoral, pectoralB.pectoral, colorTable);

  const mv = CONFIG.movement;
  const movement = {
    tailSpeed: inheritContinuousNormal(childSeed, "tail_speed", mutationChance, ownA.movement.tailSpeed, ownB.movement.tailSpeed, mv.speedMean, mv.speedStdDev),
    tailAmplitude: inheritContinuousUniform(childSeed, "tail_wag_amplitude", mutationChance, ownA.movement.tailAmplitude, ownB.movement.tailAmplitude, mv.tailAmpMin, mv.tailAmpMax),
    finSpeed: inheritContinuousNormal(childSeed, "fin_speed", mutationChance, ownA.movement.finSpeed, ownB.movement.finSpeed, mv.speedMean, mv.speedStdDev),
    finAmplitude: inheritContinuousUniform(childSeed, "fin_wag_amplitude", mutationChance, ownA.movement.finAmplitude, ownB.movement.finAmplitude, mv.finAmpMin, mv.finAmpMax),
  };

  return { shimmerTier: tier, shimmerColor, shimmerOpacity, tail, dorsal, pectoral, movement };
}

/**
 * Distribuição de probabilidade fechada (sem RNG) do valor herdado/mutado de
 * UM trait categórico, dado os valores dos dois pais — mesma matemática de
 * `ChildTierDistribution` (C#), generalizada pra qualquer tabela categórica
 * (cor de parte, tipo de padrão). Usada na prévia do ninho.
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
 * Prévia completa do cruzamento: traits reais dos dois pais (já resolve
 * filhotes-de-filhote via `traitsOf`) + distribuição de chances do filho por
 * atributo (tier de brilho, cor e padrão de cada parte). Não cobre os
 * sub-traits contínuos (opacidade/tamanho do padrão, movimento) — esses são
 * herdados junto do padrão/tier escolhido ou resorteados só na mutação.
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

const bigOrNull = (v) => (v != null ? BigInt(v) : null);

/** Traits de qualquer creature (pai normal ou filhote) — escolhe o motor certo. */
export function traitsOf(creature) {
  const seed = BigInt(creature.seed);
  if (creature.isBred && creature.parentASeed && creature.parentBSeed)
    return breedTraits(seed, BigInt(creature.parentASeed), BigInt(creature.parentBSeed),
      CONFIG.breeding.mutationChance, CONFIG.breeding.rarityBias,
      bigOrNull(creature.parentAGrandparentASeed), bigOrNull(creature.parentAGrandparentBSeed),
      bigOrNull(creature.parentBGrandparentASeed), bigOrNull(creature.parentBGrandparentBSeed));
  return generateTraits(seed);
}

/** Breakdown "por que é raro" de qualquer creature — escolhe o motor certo. */
export function rarityBreakdownOf(creature) {
  const seed = BigInt(creature.seed);
  if (creature.isBred && creature.parentASeed && creature.parentBSeed)
    return bredRarityBreakdown(seed, BigInt(creature.parentASeed), BigInt(creature.parentBSeed),
      CONFIG.breeding.mutationChance, CONFIG.breeding.rarityBias,
      bigOrNull(creature.parentAGrandparentASeed), bigOrNull(creature.parentAGrandparentBSeed),
      bigOrNull(creature.parentBGrandparentASeed), bigOrNull(creature.parentBGrandparentBSeed));
  return rarityBreakdown(seed);
}

// ---------- Produção e breakdown de raridade (só display; motor é a fonte) ----------

/** Moedas/hora que o peixe rende a água cheia (espelha IncomeCalculator.CoinsPerHour). */
export function coinsPerHourOf(rarityScore, synergyMult = 1) {
  const i = CONFIG.income;
  return i.base * Math.exp(i.growth * (rarityScore - i.ref)) * synergyMult;
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
function pickProb(table, roll) {
  let total = 0;
  for (const [, w] of table) total += w;
  const target = roll * total;
  let cum = 0;
  for (const [value, w] of table) {
    cum += w;
    if (target < cum) return [value, w / total];
  }
  const last = table[table.length - 1];
  return [last[0], last[1] / total];
}

/**
 * Decompõe o rarity score em fatores (o que faz o peixe ser raro), espelhando
 * TraitGenerator: score = Σ −log10(P). Cada fator: { key, part, value, probPct, points }.
 * O total ≈ rarityScore da API (mesma fórmula).
 */
export function rarityBreakdown(seed) {
  const factors = [];
  const selfInfo = (p) => -Math.log10(p);
  const push = (key, part, value, prob) =>
    factors.push({ key, part, value, probPct: prob * 100, points: selfInfo(prob) });

  // Corpo é destaque: contribuição do tier multiplicada por shimmerScoreWeight.
  const [tier, tierP] = pickProb(CONFIG.shimmerTiers, roll01(seed, "body_shimmer"));
  factors.push({ key: "shimmerTier", part: null, value: tier, probPct: tierP * 100, points: CONFIG.shimmerScoreWeight * selfInfo(tierP) });

  let shimmerColor = null;
  if (tier !== "None") {
    const palette = CONFIG.shimmerColorsByTier[tier];
    shimmerColor = palette[Math.floor(roll01(seed, "body_shimmer_color") * palette.length)];
  }
  const boosted = (tier === "Vibrant" || tier === "Rare" || tier === "Legendary")
    ? CONFIG.closestPartColor[shimmerColor]
    : null;
  const colorTable = applyCorrelation(CONFIG.partColors, boosted);

  const partColors = [], partPatterns = [];
  for (const part of ["tail", "dorsal", "pectoral"]) {
    const [color, colorP] = pickProb(colorTable, roll01(seed, part + "_color"));
    push("partColor", part, color, colorP);
    partColors.push(color);
    const [pattern, patternP] = pickProb(CONFIG.patternTypes, roll01(seed, part + "_pattern"));
    push("patternType", part, pattern, patternP);
    partPatterns.push(pattern);
    if (pattern === "None") continue;

    const patternPalette = CONFIG.partColors.filter(([v]) => v !== color);
    const [pc, pcP] = pickProb(patternPalette, roll01(seed, part + "_pattern_color"));
    push("patternColor", part, pc, pcP);

    const size = normalPick(seed, part + "_pattern_size", CONFIG.sizeMean, CONFIG.sizeStdDev);
    if (size < CONFIG.sizeExtremeLow)
      push("patternSizeExtreme", part, "pequeno", normalCdf(CONFIG.sizeExtremeLow, CONFIG.sizeMean, CONFIG.sizeStdDev));
    else if (size > CONFIG.sizeExtremeHigh)
      push("patternSizeExtreme", part, "grande", 1 - normalCdf(CONFIG.sizeExtremeHigh, CONFIG.sizeMean, CONFIG.sizeStdDev));

    const opacity = CONFIG.opacityMin + roll01(seed, part + "_pattern_opacity") * (CONFIG.opacityMax - CONFIG.opacityMin);
    const range = CONFIG.opacityMax - CONFIG.opacityMin;
    if (opacity < CONFIG.opacityExtremeLow)
      push("patternOpacityExtreme", part, "baixa", (CONFIG.opacityExtremeLow - CONFIG.opacityMin) / range);
    else if (opacity > CONFIG.opacityExtremeHigh)
      push("patternOpacityExtreme", part, "alta", (CONFIG.opacityMax - CONFIG.opacityExtremeHigh) / range);
  }

  const mv = CONFIG.movement;
  for (const [salt, which] of [["tail_speed", "tail"], ["fin_speed", "fin"]]) {
    const speed = normalPick(seed, salt, mv.speedMean, mv.speedStdDev);
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

/**
 * Breakdown de raridade de um FILHOTE — espelha rarityBreakdown, mas cada trait
 * vem de inheritOrMutate (herdado de um dos pais ou mutado) em vez de sorteio
 * livre. Mesma regra de "por que é raro": soma de −log10(P) de cada valor real.
 */
export function bredRarityBreakdown(childSeed, parentASeed, parentBSeed,
  mutationChance = CONFIG.breeding.mutationChance, rarityBias = CONFIG.breeding.rarityBias,
  parentAGrandparentASeed = null, parentAGrandparentBSeed = null,
  parentBGrandparentASeed = null, parentBGrandparentBSeed = null,
  grandparentReachChance = CONFIG.breeding.grandparentReachChance) {
  const ownA = resolveOwnTraits(parentASeed, parentAGrandparentASeed, parentAGrandparentBSeed, mutationChance, rarityBias);
  const ownB = resolveOwnTraits(parentBSeed, parentBGrandparentASeed, parentBGrandparentBSeed, mutationChance, rarityBias);
  const gpA1 = parentAGrandparentASeed != null ? generateTraits(parentAGrandparentASeed) : null;
  const gpA2 = parentAGrandparentBSeed != null ? generateTraits(parentAGrandparentBSeed) : null;
  const gpB1 = parentBGrandparentASeed != null ? generateTraits(parentBGrandparentASeed) : null;
  const gpB2 = parentBGrandparentBSeed != null ? generateTraits(parentBGrandparentBSeed) : null;
  const effA = (slotSalt) => effectiveParentTraits(childSeed, slotSalt + "_a", ownA, gpA1, gpA2, grandparentReachChance);
  const effB = (slotSalt) => effectiveParentTraits(childSeed, slotSalt + "_b", ownB, gpB1, gpB2, grandparentReachChance);

  const factors = [];
  const selfInfo = (p) => -Math.log10(p);
  const push = (key, part, value, prob) =>
    factors.push({ key, part, value, probPct: prob * 100, points: selfInfo(prob) });

  const shimmerA = effA("body_shimmer"), shimmerB = effB("body_shimmer");
  const tierPick = inheritOrMutate(childSeed, "body_shimmer", mutationChance, shimmerA.shimmerTier, shimmerB.shimmerTier, CONFIG.shimmerTiers, rarityBias);
  const tier = tierPick.value;
  const tierProb = probabilityOf(CONFIG.shimmerTiers, tier);
  factors.push({ key: "shimmerTier", part: null, value: tier, probPct: tierProb * 100, points: CONFIG.shimmerScoreWeight * selfInfo(tierProb) });

  let shimmerColor = null;
  if (tier !== "None") {
    const tierSource = tierPick.fromA ? shimmerA : shimmerB;
    shimmerColor = (!tierPick.mutated && tierSource.shimmerTier === tier)
      ? tierSource.shimmerColor
      : CONFIG.shimmerColorsByTier[tier][Math.floor(roll01(childSeed, "body_shimmer_color") * CONFIG.shimmerColorsByTier[tier].length)];
  }
  const boosted = (tier === "Vibrant" || tier === "Rare" || tier === "Legendary")
    ? CONFIG.closestPartColor[shimmerColor]
    : null;
  const colorTable = applyCorrelation(CONFIG.partColors, boosted);

  const partColors = [], partPatterns = [];
  const parts = { tail: [effA("tail").tail, effB("tail").tail], dorsal: [effA("dorsal").dorsal, effB("dorsal").dorsal], pectoral: [effA("pectoral").pectoral, effB("pectoral").pectoral] };
  for (const part of ["tail", "dorsal", "pectoral"]) {
    const [pa, pb] = parts[part];
    const colorPick = inheritOrMutate(childSeed, part + "_color", mutationChance, pa.color, pb.color, colorTable, rarityBias);
    push("partColor", part, colorPick.value, probabilityOf(colorTable, colorPick.value));
    partColors.push(colorPick.value);

    const patternPick = inheritOrMutate(childSeed, part + "_pattern", mutationChance, pa.pattern, pb.pattern, CONFIG.patternTypes, rarityBias);
    push("patternType", part, patternPick.value, probabilityOf(CONFIG.patternTypes, patternPick.value));
    partPatterns.push(patternPick.value);
    if (patternPick.value === "None") continue;

    const patternSource = patternPick.fromA ? pa : pb;
    const subtraitsFromSource = !patternPick.mutated && patternSource.pattern === patternPick.value
      && patternSource.patternColor !== colorPick.value;

    let patternColor, size, opacity;
    if (subtraitsFromSource) {
      patternColor = patternSource.patternColor;
      size = patternSource.patternSize;
      opacity = patternSource.patternOpacity;
    } else {
      const patternPalette = CONFIG.partColors.filter(([v]) => v !== colorPick.value);
      patternColor = weightedPick(patternPalette, roll01(childSeed, part + "_pattern_color"));
      size = normalPick(childSeed, part + "_pattern_size", CONFIG.sizeMean, CONFIG.sizeStdDev);
      opacity = CONFIG.opacityMin + roll01(childSeed, part + "_pattern_opacity") * (CONFIG.opacityMax - CONFIG.opacityMin);
    }
    const scoringPalette = CONFIG.partColors.filter(([v]) => v !== colorPick.value);
    push("patternColor", part, patternColor, probabilityOf(scoringPalette, patternColor));

    if (size < CONFIG.sizeExtremeLow)
      push("patternSizeExtreme", part, "pequeno", normalCdf(CONFIG.sizeExtremeLow, CONFIG.sizeMean, CONFIG.sizeStdDev));
    else if (size > CONFIG.sizeExtremeHigh)
      push("patternSizeExtreme", part, "grande", 1 - normalCdf(CONFIG.sizeExtremeHigh, CONFIG.sizeMean, CONFIG.sizeStdDev));

    const range = CONFIG.opacityMax - CONFIG.opacityMin;
    if (opacity < CONFIG.opacityExtremeLow)
      push("patternOpacityExtreme", part, "baixa", (CONFIG.opacityExtremeLow - CONFIG.opacityMin) / range);
    else if (opacity > CONFIG.opacityExtremeHigh)
      push("patternOpacityExtreme", part, "alta", (CONFIG.opacityMax - CONFIG.opacityExtremeHigh) / range);
  }

  const mv = CONFIG.movement;
  for (const [salt, which, pa, pb] of [
    ["tail_speed", "tail", ownA.movement.tailSpeed, ownB.movement.tailSpeed],
    ["fin_speed", "fin", ownA.movement.finSpeed, ownB.movement.finSpeed],
  ]) {
    const speed = inheritContinuousNormal(childSeed, salt, mutationChance, pa, pb, mv.speedMean, mv.speedStdDev);
    let prob = null, value = null;
    if (speed < mv.speedExtremeLow) { prob = normalCdf(mv.speedExtremeLow, mv.speedMean, mv.speedStdDev); value = "lenta"; }
    else if (speed > mv.speedExtremeHigh) { prob = 1 - normalCdf(mv.speedExtremeHigh, mv.speedMean, mv.speedStdDev); value = "rápida"; }
    if (prob !== null)
      factors.push({ key: "speedExtreme", part: which, value, probPct: prob * 100, points: mv.scoreWeight * selfInfo(prob) });
  }

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
