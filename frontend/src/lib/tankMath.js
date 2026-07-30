// Cálculos derivados do estado do tanque (só display; o servidor é a fonte da renda).
import { coinsPerHourOf, generateTraits, synergyMultiplier } from "./generator.js";
import { PT } from "./fishRenderer.js";

/** Contagem de peixes por cor de cauda no tanque. */
export function tailColorCounts(creatures) {
  const counts = {};
  for (const c of creatures) {
    const color = generateTraits(BigInt(c.seed)).tail.color;
    counts[color] = (counts[color] || 0) + 1;
  }
  return counts;
}

/** Sinergia de cor de cauda: grupos de 2+ com o bônus resultante, ordenados. */
export function tankSynergy(creatures) {
  const counts = tailColorCounts(creatures);
  return Object.entries(counts)
    .filter(([, n]) => n >= 2)
    .map(([color, n]) => ({ color, n, bonus: synergyMultiplier(n) - 1 }))
    .sort((a, b) => b.n - a.n);
}

/**
 * Peixes do tanque prontos pra listagem, com traits + produção (raridade × sinergia),
 * ordenados por produção desc. Reaproveitado pela lista do tanque.
 */
export function tankFishSorted(creatures) {
  const counts = tailColorCounts(creatures);
  return creatures
    .map((c) => {
      const traits = generateTraits(BigInt(c.seed));
      const col = traits.tail.color;
      const mult = synergyMultiplier(counts[col] ?? 1);
      return { c, traits, col, colorLabel: PT.color[col], prod: coinsPerHourOf(Number(c.rarityScore), mult) };
    })
    .sort((a, b) => b.prod - a.prod || Number(b.c.rarityScore) - Number(a.c.rarityScore));
}

/** Produção total do tanque a água cheia (com sinergia) — o "potencial". */
export function tankPotential(creatures) {
  const counts = tailColorCounts(creatures);
  return creatures.reduce((s, c) => {
    const col = generateTraits(BigInt(c.seed)).tail.color;
    return s + coinsPerHourOf(Number(c.rarityScore), synergyMultiplier(counts[col]));
  }, 0);
}

/** Estimativa do próximo peixe na fila a partir do progresso de geração. */
export function nextFishEta(tank) {
  if (tank.queue.length >= tank.queueCap) return { full: true };
  const interval = tank.generationIntervalMinutes || 15;
  const progress = Number(tank.generationProgressMinutes ?? 0);
  const waterFactor = Number(tank.maintenanceLevel) < 40 ? 0.5 : 1;
  const rate = (tank.online ? 1.0 : 0.45) * waterFactor; // min efetivos por min real
  const mins = rate > 0 ? Math.max(0, interval - progress) / rate : Infinity;
  return { full: false, mins, fraction: Math.min(1, progress / interval) };
}
