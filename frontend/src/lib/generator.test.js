import { describe, expect, it } from "vitest";
import {
  biasedInheritProbability, breedTraits, CONFIG, coinsPerHourOf, effectiveParentTraits, generateTraits,
  newDuplicationStreak, probabilityOf, rarityBreakdown, resolveOwnTraits, restrictTable, roll01,
  synergyMultiplier, traitsOf, vendorPriceOf, waterDegradationPerFishPerHour, weightOf,
} from "./generator.js";

function findSeedWithTailColor(color, searchLimit = 5000) {
  for (let s = 1n; s <= BigInt(searchLimit); s++) {
    if (generateTraits(s).tail.color === color) return s;
  }
  throw new Error(`nenhum seed com cauda ${color} nos primeiros ${searchLimit}`);
}

describe("roll01", () => {
  it("é determinístico pro mesmo seed+salt", () => {
    expect(roll01(123n, "body_shimmer")).toBe(roll01(123n, "body_shimmer"));
  });

  it("varia com o salt (evita correlação entre traits)", () => {
    expect(roll01(123n, "a")).not.toBe(roll01(123n, "b"));
  });

  it("sempre cai em [0, 1)", () => {
    for (const seed of [0n, 1n, -1n, 999999999999n, -999999999999n]) {
      const r = roll01(seed, "qualquer");
      expect(r).toBeGreaterThanOrEqual(0);
      expect(r).toBeLessThan(1);
    }
  });
});

describe("generateTraits", () => {
  it("é determinístico pro mesmo seed", () => {
    const a = generateTraits(42n);
    const b = generateTraits(42n);
    expect(a).toEqual(b);
  });

  it("seeds diferentes tendem a gerar traits diferentes", () => {
    const a = generateTraits(1n);
    const b = generateTraits(2n);
    expect(a).not.toEqual(b);
  });

  it("sem brilho (tier None) não sorteia cor/opacidade de shimmer", () => {
    // Busca um seed que caia em "None" (78% de chance, achar um é rápido)
    let seed = 0n;
    let traits;
    do { traits = generateTraits(seed); seed += 1n; } while (traits.shimmerTier !== "None" && seed < 1000n);
    expect(traits.shimmerTier).toBe("None");
    expect(traits.shimmerColor).toBeNull();
    expect(traits.shimmerOpacity).toBe(0);
  });

  it("padrão 'None' não sorteia cor/tamanho/opacidade de padrão", () => {
    const traits = generateTraits(42n);
    for (const part of [traits.tail, traits.dorsal, traits.pectoral]) {
      if (part.pattern === "None") {
        expect(part.patternColor).toBeNull();
        expect(part.patternSize).toBeNull();
        expect(part.patternOpacity).toBeNull();
      }
    }
  });

  // Trava de regressão: se o algoritmo mudar sem querer (reordenar hashes, trocar
  // fórmula), este snapshot falha. Atualizar com `vitest run -u` só quando a mudança
  // no motor de traits for intencional (e o TraitConfigVersion do backend também subir).
  it("seeds fixos batem com o snapshot salvo (regressão do motor determinístico)", () => {
    expect(generateTraits(1n)).toMatchSnapshot();
    expect(generateTraits(1234567890123n)).toMatchSnapshot();
  });
});

describe("traitsOf", () => {
  it("peixe normal usa generateTraits(seed)", () => {
    const creature = { seed: "42", isBred: false };
    expect(traitsOf(creature)).toEqual(generateTraits(42n));
  });

  it("filhote usa breedTraits com os seeds dos pais", () => {
    const creature = { seed: "999", isBred: true, parentASeed: "1", parentBSeed: "2" };
    expect(traitsOf(creature)).toEqual(breedTraits(999n, 1n, 2n));
  });
});

describe("biasedInheritProbability", () => {
  it("bias 0 é sempre 50/50, mesmo com probabilidades bem diferentes", () => {
    expect(biasedInheritProbability(0.002, 0.78, 0)).toBe(0.5);
  });

  it("probabilidades iguais ficam 50/50 mesmo com bias > 0", () => {
    expect(biasedInheritProbability(0.1, 0.1, 0.5)).toBeCloseTo(0.5, 10);
  });

  it("pesa a favor do valor mais raro (probabilidade menor) quando bias > 0", () => {
    const p = biasedInheritProbability(0.002, 0.78, 0.15); // A é raro (lendário), B é comum
    expect(p).toBeGreaterThan(0.5);
  });
});

describe("probabilityOf", () => {
  it("soma das probabilidades da tabela é 1", () => {
    const table = [["a", 70], ["b", 20], ["c", 10]];
    const total = table.reduce((s, [v]) => s + probabilityOf(table, v), 0);
    expect(total).toBeCloseTo(1, 10);
  });
});

describe("coinsPerHourOf", () => {
  it("cresce com a raridade", () => {
    const comum = coinsPerHourOf(4);
    const raro = coinsPerHourOf(7.5);
    const lendario = coinsPerHourOf(14);
    expect(comum).toBeLessThan(raro);
    expect(raro).toBeLessThan(lendario);
  });

  it("sinergia multiplica linearmente a renda", () => {
    const base = coinsPerHourOf(5, 1);
    const comSinergia = coinsPerHourOf(5, 1.5);
    expect(comSinergia).toBeCloseTo(base * 1.5, 10);
  });

  it("taper do Lendário comprime a variação acima do piso (12/08/2026)", () => {
    const piso = coinsPerHourOf(14);
    const topoObservado = coinsPerHourOf(21);
    expect(piso).toBeGreaterThan(95);
    expect(piso).toBeLessThan(105);
    expect(topoObservado / piso).toBeLessThan(2.5);
    // contínuo no corte, sem salto
    expect(Math.abs(coinsPerHourOf(14.001) - coinsPerHourOf(13.999))).toBeLessThan(0.1);
  });
});

describe("vendorPriceOf", () => {
  it("cresce com a raridade e nunca fica abaixo do mínimo", () => {
    expect(vendorPriceOf(0)).toBe(CONFIG.vendor.minPrice);
    expect(vendorPriceOf(5)).toBeLessThan(vendorPriceOf(9));
    expect(vendorPriceOf(9)).toBeLessThan(vendorPriceOf(15));
  });

  it("fica bem abaixo do que um filtro básico custa (20 soft) pra um peixe comum", () => {
    expect(vendorPriceOf(5)).toBeLessThan(20);
  });
});

describe("synergyMultiplier", () => {
  it("1 peixe (ou 0) não tem bônus", () => {
    expect(synergyMultiplier(1)).toBe(1);
    expect(synergyMultiplier(0)).toBe(1);
  });

  it("cresce com mais peixes da mesma cor, até o teto", () => {
    expect(synergyMultiplier(2)).toBeGreaterThan(1);
    expect(synergyMultiplier(3)).toBeGreaterThan(synergyMultiplier(2));
    expect(synergyMultiplier(1000)).toBeLessThanOrEqual(1.8); // maxBonus 0.80
  });
});

describe("breedTraits — viés de raridade em cor de parte (31/07/2026)", () => {
  // Antes só o shimmerTier usava rarityBias; cor/padrão de parte herdavam 50/50 puro,
  // deixando o filhote livre pra regredir perto do piso da população mesmo vindo de
  // pais decentes. Espelha os testes equivalentes em BreedTraitsTests.cs (C#).
  it("inclina a herança de cor pro valor mais raro entre os pais", () => {
    const whiteParent = findSeedWithTailColor("PureWhite");
    const orangeParent = findSeedWithTailColor("Orange", 50);
    const rarityBias = 0.6;
    const n = 4000;

    // Anti-duplicação desligada (antiDuplicationDecay/MaxPenalty=0, 13/08/2026) — este teste
    // isola só o efeito do rarityBias na cor da cauda; sem desligar, a sequência (que também
    // reage ao tier, sorteado ANTES da cor, com o MESMO rarityBias) desloca a taxa observada
    // pra fora da faixa estreita esperada aqui.
    let whiteCount = 0;
    for (let childSeed = 1n; childSeed <= BigInt(n); childSeed++) {
      const color = breedTraits(childSeed, whiteParent, orangeParent, 0, rarityBias,
        null, null, null, null, CONFIG.breeding.grandparentReachChance, CONFIG.breeding.mutationRarityBiasStrength, 0, 0).tail.color;
      if (color === "PureWhite") whiteCount++;
    }

    const expected = biasedInheritProbability(0.01, 0.22, rarityBias);
    expect(expected).toBeGreaterThan(0.5);
    expect(whiteCount / n).toBeGreaterThan(expected - 0.05);
    expect(whiteCount / n).toBeLessThan(expected + 0.05);
  });

  it("bias 0 (comportamento antigo) continua 50/50 puro — não quebra o default", () => {
    const whiteParent = findSeedWithTailColor("PureWhite");
    const orangeParent = findSeedWithTailColor("Orange", 50);
    const n = 4000;

    let whiteCount = 0;
    for (let childSeed = 1n; childSeed <= BigInt(n); childSeed++) {
      const color = breedTraits(childSeed, whiteParent, orangeParent, 0, 0).tail.color;
      if (color === "PureWhite") whiteCount++;
    }
    expect(whiteCount / n).toBeGreaterThan(0.45);
    expect(whiteCount / n).toBeLessThan(0.55);
  });

  it("com os parâmetros reais de produção, não vira lavagem garantida", () => {
    const whiteParent = findSeedWithTailColor("PureWhite");
    const orangeParent = findSeedWithTailColor("Orange", 50);
    const { mutationChance, rarityBias } = CONFIG.breeding;
    const n = 3000;

    let whiteCount = 0;
    for (let childSeed = 1n; childSeed <= BigInt(n); childSeed++) {
      const color = breedTraits(childSeed + 10_000_000n, whiteParent, orangeParent, mutationChance, rarityBias).tail.color;
      if (color === "PureWhite") whiteCount++;
    }
    expect(whiteCount / n).toBeLessThan(0.70);
  });
});

describe("anti-duplicação e piso de mutação (13/08/2026)", () => {
  // Espelha os testes equivalentes em BreedTraitsTests.cs (C#).

  it.each([1, 2, 3, 10])("applyPenalty nunca inverte o lado já favorecido pela raridade (streak=%i)", (streakCount) => {
    // Bug real corrigido 13/08/2026 (relatado pelo usuário, conta EoNeng — filhotes saindo com
    // score bem abaixo dos dois pais). Ver comentário equivalente em BreedTraitsTests.cs (C#).
    const streak = newDuplicationStreak(CONFIG.breeding.antiDuplicationDecay, CONFIG.breeding.antiDuplicationMaxPenalty);
    streak.sideA = true;
    streak.count = streakCount;
    const result = streak.applyPenalty(0.71);
    expect(result).toBeGreaterThanOrEqual(0.5);

    const streakB = newDuplicationStreak(CONFIG.breeding.antiDuplicationDecay, CONFIG.breeding.antiDuplicationMaxPenalty);
    streakB.sideA = false;
    streakB.count = streakCount;
    const resultB = streakB.applyPenalty(0.29);
    expect(resultB).toBeLessThanOrEqual(0.5);
  });

  it("applyPenalty continua livre pra empurrar quando o lado que ganha já era o menos favorecido", () => {
    const streak = newDuplicationStreak(CONFIG.breeding.antiDuplicationDecay, CONFIG.breeding.antiDuplicationMaxPenalty);
    streak.sideA = true;
    streak.count = 5;
    const result = streak.applyPenalty(0.3);
    expect(result).toBeLessThan(0.3);
  });

  it("reduz a fração de filhotes que clonam um dos pais em 4 slots", () => {
    const parentASeed = findSeedWithTier("Subtle", 5000);
    const ta = generateTraits(parentASeed);
    let parentBSeed = null;
    for (let s = 1n; s <= 5000n; s++) {
      const tb = generateTraits(s);
      if (tb.shimmerTier !== ta.shimmerTier && tb.tail.color !== ta.tail.color
        && tb.dorsal.color !== ta.dorsal.color && tb.pectoral.color !== ta.pectoral.color) {
        parentBSeed = s;
        break;
      }
    }
    if (parentBSeed === null) throw new Error("não achou par distinto");

    const n = 6000;
    function countCloneOfA(decay, maxPenalty) {
      let clones = 0;
      for (let childSeed = 1n; childSeed <= BigInt(n); childSeed++) {
        const child = breedTraits(childSeed, parentASeed, parentBSeed, 0, 0,
          null, null, null, null, 0, -1, decay, maxPenalty);
        if (child.shimmerTier === ta.shimmerTier && child.tail.color === ta.tail.color
          && child.dorsal.color === ta.dorsal.color && child.pectoral.color === ta.pectoral.color)
          clones++;
      }
      return clones / n;
    }

    const pctDisabled = countCloneOfA(0, 0);
    const pctEnabled = countCloneOfA(CONFIG.breeding.antiDuplicationDecay, CONFIG.breeding.antiDuplicationMaxPenalty);

    expect(pctDisabled).toBeGreaterThan(0.03);
    expect(pctDisabled).toBeLessThan(0.11);
    expect(pctEnabled).toBeLessThan(pctDisabled * 0.9);
  }, 20000);

  it.each([
    ["Black", "Black"], // ambos 2ª cor mais rara (3%) — só Preto ou Branco
    ["Blue", "Red"], // Azul(20%)/Vermelho(18%): piso = Azul, só exclui Laranja
    ["Blue", "Black"], // Azul(20%)/Preto(3%): piso AINDA é Azul, o mais fraco — não o mais raro
  ])("piso de mutação: nunca sai mais comum que o pai mais fraco (%s + %s)", (colorA, colorB) => {
    const parentASeed = findSeedWithTailColor(colorA);
    const parentBSeed = findSeedWithTailColor(colorB);
    const floorWeight = Math.max(weightOf(CONFIG.partColors, colorA), weightOf(CONFIG.partColors, colorB));

    for (let childSeed = 1n; childSeed <= 3000n; childSeed++) {
      const child = breedTraits(childSeed, parentASeed, parentBSeed, 1.0, 0,
        null, null, null, null, 0, CONFIG.breeding.mutationRarityBiasStrength, 0, 0);
      // Pula os poucos casos em que o tier também mutou pra Vibrante+ (correlação shimmer→cor
      // reescala a tabela de cor inteira — mesma ressalva do teste C# equivalente).
      if (["Vibrant", "Rare", "Legendary"].includes(child.shimmerTier)) continue;
      const resultWeight = weightOf(CONFIG.partColors, child.tail.color);
      expect(resultWeight).toBeLessThanOrEqual(floorWeight);
    }
  });

  it("restrictTable: piso no valor mais comum da tabela mantém a tabela inteira", () => {
    const maxWeight = Math.max(...CONFIG.shimmerTiers.map(([, w]) => w));
    expect(restrictTable(CONFIG.shimmerTiers, maxWeight).length).toBe(CONFIG.shimmerTiers.length);
  });

  it("restrictTable: piso no 2º valor mais raro exclui todos os mais comuns", () => {
    const blackWeight = weightOf(CONFIG.partColors, "Black");
    const restricted = restrictTable(CONFIG.partColors, blackWeight).map(([v]) => v);
    expect(new Set(restricted)).toEqual(new Set(["Black", "PureWhite"]));
  });
});

function findSeedWithTier(tier, searchLimit = 5000) {
  for (let s = 1n; s <= BigInt(searchLimit); s++) {
    if (generateTraits(s).shimmerTier === tier) return s;
  }
  throw new Error(`nenhum seed com tier ${tier} nos primeiros ${searchLimit}`);
}

describe("chance de herdar traço de um avô (31/07/2026)", () => {
  // Espelha os testes equivalentes em BreedTraitsTests.cs (C#) — testa
  // effectiveParentTraits/resolveOwnTraits diretamente em vez de tentar isolar o sinal na
  // saída ponta-a-ponta (o valor "próprio" de um pai bred já reflete os mesmos avós que o
  // reach-back alcançaria, então observar só a cor final do filhote é ambíguo).
  it("sem avós, sempre retorna o próprio", () => {
    const own = generateTraits(1n);
    for (let childSeed = 1n; childSeed <= 500n; childSeed++) {
      const result = effectiveParentTraits(childSeed, "salt", own, null, null, 1);
      expect(result.traits).toEqual(own);
      expect(result.source).toBe("own");
    }
  });

  it("com avós, a fração que alcança um avô aproxima reachChance", () => {
    const own = generateTraits(1n);
    const grandparent1 = generateTraits(2n);
    const grandparent2 = generateTraits(3n);
    const reachChance = 0.3;
    const n = 15000;

    let reached = 0;
    for (let childSeed = 1n; childSeed <= BigInt(n); childSeed++) {
      const result = effectiveParentTraits(childSeed, "salt", own, grandparent1, grandparent2, reachChance);
      if (result.traits !== own) reached++;
    }
    expect(reached / n).toBeGreaterThan(reachChance - 0.03);
    expect(reached / n).toBeLessThan(reachChance + 0.03);
  });

  it("reachChance 0 nunca alcança o avô mesmo com avós conhecidos", () => {
    const own = generateTraits(1n);
    const grandparent1 = generateTraits(2n);
    const grandparent2 = generateTraits(3n);
    for (let childSeed = 1n; childSeed <= 2000n; childSeed++) {
      const result = effectiveParentTraits(childSeed, "salt", own, grandparent1, grandparent2, 0);
      expect(result.traits).toEqual(own);
      expect(result.source).toBe("own");
    }
  });

  it("resolveOwnTraits: pai fresco usa generateTraits direto", () => {
    expect(resolveOwnTraits(42n, null, null, 0.08, 0.15)).toEqual(generateTraits(42n));
  });

  it("resolveOwnTraits: pai filhote recomputa via os avós, nunca generateTraits(seed) direto (bug corrigido)", () => {
    const resolved = resolveOwnTraits(999n, 1n, 2n, 0.08, 0.15);
    expect(resolved).toEqual(breedTraits(999n, 1n, 2n, 0.08, 0.15));
    expect(resolved).not.toEqual(generateTraits(999n));
  });

  it("breedTraits sem ancestralidade de avós é idêntico ao comportamento antigo (regressão)", () => {
    const semAvos = breedTraits(42n, 1n, 2n, 0.08, 0.15);
    const comAvosNulos = breedTraits(42n, 1n, 2n, 0.08, 0.15, null, null, null, null, 0.15);
    expect(comAvosNulos).toEqual(semAvos);
  });
});

describe("rarityBreakdown", () => {
  it("soma dos fatores bate com o total retornado", () => {
    const { total, factors } = rarityBreakdown(42n);
    const sum = factors.reduce((s, f) => s + f.points, 0);
    expect(sum).toBeCloseTo(total, 10);
  });

  it("sempre inclui o fator de shimmerTier do corpo", () => {
    const { factors } = rarityBreakdown(42n);
    expect(factors.some((f) => f.key === "shimmerTier")).toBe(true);
  });
});

describe("Degradê — mix de cores (12/08/2026)", () => {
  function manySeeds(count) {
    const seeds = [];
    for (let i = 1; i <= count; i++) seeds.push(BigInt(i) * 7919n);
    return seeds;
  }

  function findSeedWithGradientMix(mix, searchLimit) {
    for (let s = 1n; s <= BigInt(searchLimit); s++) {
      const t = generateTraits(s);
      for (const key of ["tail", "dorsal", "pectoral"]) {
        if (t[key].pattern === "Gradient" && t[key].mix === mix) return { seed: s, part: key };
      }
    }
    throw new Error(`nenhum seed com Degradê ${mix} nos primeiros ${searchLimit}`);
  }

  it("mix só é não-nulo quando o padrão é Gradient", () => {
    for (const seed of manySeeds(3000)) {
      const t = generateTraits(seed);
      for (const part of [t.tail, t.dorsal, t.pectoral]) {
        if (part.pattern === "Gradient") expect(part.mix).not.toBeNull();
        else expect(part.mix).toBeNull();
      }
    }
  });

  it("distribuição do mix bate com os pesos (Even bem mais raro que os assimétricos)", () => {
    const counts = { BaseDominant: 0, Even: 0, PatternDominant: 0 };
    let total = 0;
    for (const seed of manySeeds(20000)) {
      const t = generateTraits(seed);
      for (const part of [t.tail, t.dorsal, t.pectoral]) {
        if (part.mix) { counts[part.mix]++; total++; }
      }
    }
    expect(total).toBeGreaterThan(30);
    expect(counts.BaseDominant / total).toBeGreaterThan(0.25);
    expect(counts.BaseDominant / total).toBeLessThan(0.65);
    expect(counts.Even / total).toBeGreaterThan(0.01);
    expect(counts.Even / total).toBeLessThan(0.25);
    expect(counts.PatternDominant / total).toBeGreaterThan(0.25);
    expect(counts.PatternDominant / total).toBeLessThan(0.65);
  }, 20000);

  it("Even soma as duas cores (base e padrão) no breakdown de raridade", () => {
    const { seed, part } = findSeedWithGradientMix("Even", 20000);
    const { factors } = rarityBreakdown(seed);
    const colorFactor = factors.find((f) => f.key === "partColor" && f.part === part);
    const patternColorFactor = factors.find((f) => f.key === "patternColor" && f.part === part);
    expect(colorFactor.points).toBeGreaterThan(0);
    expect(patternColorFactor.points).toBeGreaterThan(0);
    expect(colorFactor.note).toBeUndefined();
    expect(patternColorFactor.note).toBeUndefined();
  });

  it("split assimétrico só conta a cor dominante — a minoritária zera e ganha uma nota", () => {
    for (const mix of ["BaseDominant", "PatternDominant"]) {
      const { seed, part } = findSeedWithGradientMix(mix, 5000);
      const { factors } = rarityBreakdown(seed);
      const colorFactor = factors.find((f) => f.key === "partColor" && f.part === part);
      const patternColorFactor = factors.find((f) => f.key === "patternColor" && f.part === part);
      if (mix === "BaseDominant") {
        expect(colorFactor.points).toBeGreaterThan(0);
        expect(patternColorFactor.points).toBe(0);
        expect(patternColorFactor.note).toBeDefined();
      } else {
        expect(patternColorFactor.points).toBeGreaterThan(0);
        expect(colorFactor.points).toBe(0);
        expect(colorFactor.note).toBeDefined();
      }
    }
  });

  it("mix herdado em bloco no breeding quando o subtrait vem do mesmo pai", () => {
    let parentA = null;
    for (let s = 1n; s <= 5000n; s++) {
      if (generateTraits(s).tail.pattern === "Gradient") { parentA = s; break; }
    }
    let parentB = null;
    for (let s = 1n; s <= 5000n; s++) {
      if (generateTraits(s).tail.pattern === "None") { parentB = s; break; }
    }
    const traitsA = generateTraits(parentA);

    let foundInherited = false;
    for (let childSeed = 1n; childSeed <= 5000n; childSeed++) {
      const child = breedTraits(childSeed, parentA, parentB, 0, 0);
      if (child.tail.pattern !== "Gradient") continue;
      if (child.tail.patternColor !== traitsA.tail.patternColor) continue;
      if (child.tail.patternSize !== traitsA.tail.patternSize) continue;
      if (child.tail.patternOpacity !== traitsA.tail.patternOpacity) continue;
      foundInherited = true;
      expect(child.tail.mix).toBe(traitsA.tail.mix);
    }
    expect(foundInherited).toBe(true);
  }, 20000);
});

describe("waterDegradationPerFishPerHour", () => {
  it("cresce com a raridade (peixe mais raro suja mais)", () => {
    const comum = waterDegradationPerFishPerHour(5);
    const epico = waterDegradationPerFishPerHour(15);
    expect(epico).toBeGreaterThan(comum);
    expect(epico).toBeCloseTo(comum * 3, 10); // score 15 / ref 5 = 3x o peso de score 5
  });
});
