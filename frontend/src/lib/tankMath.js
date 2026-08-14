// Cálculos derivados do estado do tanque (só display; o servidor é a fonte da renda).
import { CONFIG, coinsPerHourOf, partSynergyBonus, synergyMultiplier, traitsOf } from "./generator.js";
import { PT } from "./fishRenderer.js";

/** Acima disso, comprar filtro não muda nada (a renda já está no teto) — avisar antes de gastar. */
export const FILTER_WARN_THRESHOLD = 95;

// 14/08/2026: sinergia passou a valer por PARTE (cauda/dorsal/peitoral contam separado, os 3
// bônus somam no peixe) — ver CONFIG.synergy/synergyMultiplier em generator.js.
const PARTS = ["tail", "dorsal", "pectoral"];

/** Contagem de peixes por cor, numa parte específica ("tail" | "dorsal" | "pectoral"). */
export function partColorCounts(creatures, part) {
  const counts = {};
  for (const c of creatures) {
    const color = traitsOf(c)[part].color;
    counts[color] = (counts[color] || 0) + 1;
  }
  return counts;
}

/** Contagens das 3 partes de uma vez — evita recalcular em `tankFishSorted`/`tankPotential`. */
function allPartCounts(creatures) {
  return Object.fromEntries(PARTS.map((part) => [part, partColorCounts(creatures, part)]));
}

/** Multiplicador de sinergia do peixe a partir das contagens já calculadas das 3 partes. */
function synergyMultiplierOf(traits, counts) {
  return synergyMultiplier(
    counts.tail[traits.tail.color] ?? 1,
    counts.dorsal[traits.dorsal.color] ?? 1,
    counts.pectoral[traits.pectoral.color] ?? 1,
  );
}

/** Sinergia de cor por parte: grupos de 2+ em cada parte, com o bônus resultante, ordenados. */
export function tankSynergy(creatures) {
  const groups = [];
  for (const part of PARTS) {
    const counts = partColorCounts(creatures, part);
    for (const [color, n] of Object.entries(counts)) {
      if (n >= 2) groups.push({ part, color, n, bonus: partSynergyBonus(n) });
    }
  }
  return groups.sort((a, b) => b.n - a.n);
}

/**
 * Peixes do tanque prontos pra listagem, com traits + produção (raridade × sinergia),
 * ordenados por produção desc. Reaproveitado pela lista do tanque.
 */
export function tankFishSorted(creatures) {
  const counts = allPartCounts(creatures);
  return creatures
    .map((c) => {
      const traits = traitsOf(c);
      const col = traits.tail.color;
      const mult = synergyMultiplierOf(traits, counts);
      return { c, traits, col, colorLabel: PT.color[col], prod: coinsPerHourOf(Number(c.rarityScore), mult) };
    })
    .sort((a, b) => b.prod - a.prod || Number(b.c.rarityScore) - Number(a.c.rarityScore));
}

/** Produção total do tanque a água cheia (com sinergia) — o "potencial". */
export function tankPotential(creatures) {
  const counts = allPartCounts(creatures);
  return creatures.reduce((s, c) => {
    const traits = traitsOf(c);
    return s + coinsPerHourOf(Number(c.rarityScore), synergyMultiplierOf(traits, counts));
  }, 0);
}

/** "Peso" do tanque pra fins de filtro/degradação — espelha `ActiveFishWeight` do backend (só display). */
export function tankFishWeight(creatures) {
  return creatures.reduce((s, c) => s + Number(c.rarityScore), 0) / CONFIG.degradation.rarityRefScore;
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
