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
    ["None", 65.0], ["Stripe", 15.0], ["Dot", 15.0], ["Gradient", 4.0], ["Mottled", 1.0],
  ],
  correlationBoostPoints: 15.0,
  sizeMean: 50.0, sizeStdDev: 20.0,
  opacityMin: 20.0, opacityMax: 90.0,
  movement: {
    speedMean: 50.0, speedStdDev: 20.0,
    tailAmpMin: 0.20, tailAmpMax: 0.75,
    finAmpMin: 0.15, finAmpMax: 0.75,
  },
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
