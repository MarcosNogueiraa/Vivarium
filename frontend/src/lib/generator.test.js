import { describe, expect, it } from "vitest";
import {
  biasedInheritProbability, breedingPreview, CONFIG, coinsPerHourOf, generateTraits,
  probabilityOf, rarityBreakdownOf, roll01, synergyMultiplier, traitDistribution, traitsOf,
  vendorPriceOf, waterDegradationPerFishPerHour,
} from "./generator.js";

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
  // 13/08/2026 — traits congelados no nascimento: o cliente não deriva mais nada, só lê
  // `creature.traits` (já resolvido pelo servidor, seja peixe fresco ou filhote).
  it("devolve creature.traits direto, sem recalcular nada", () => {
    const traits = generateTraits(42n);
    const creature = { seed: "42", traits };
    expect(traitsOf(creature)).toBe(traits);
  });

  it("funciona igual pra um filhote (o servidor já resolveu a herança)", () => {
    const traits = generateTraits(999n);
    const creature = { seed: "999", isBred: true, parentASeed: "1", parentBSeed: "2", traits };
    expect(traitsOf(creature)).toBe(traits);
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

describe("traitDistribution / breedingPreview (prévia do Ninho, antes de confirmar)", () => {
  it("distribuição de um trait categórico soma 1", () => {
    const dist = traitDistribution(CONFIG.partColors, "Orange", "Blue", 0.04, 0.15);
    const total = dist.reduce((s, { prob }) => s + prob, 0);
    expect(total).toBeCloseTo(1, 10);
  });

  it("sem mutação, só os valores dos pais têm probabilidade > 0", () => {
    const dist = traitDistribution(CONFIG.partColors, "Orange", "Blue", 0, 0.15);
    for (const { value, prob } of dist) {
      if (value === "Orange" || value === "Blue") expect(prob).toBeGreaterThan(0);
      else expect(prob).toBe(0);
    }
  });

  it("breedingPreview usa os traits reais (já congelados) dos dois pais", () => {
    const parentA = { seed: "1", traits: generateTraits(1n) };
    const parentB = { seed: "2", traits: generateTraits(2n) };
    const preview = breedingPreview(parentA, parentB);
    expect(preview.parentA).toEqual(parentA.traits);
    expect(preview.parentB).toEqual(parentB.traits);
    expect(preview.shimmerTier.reduce((s, { prob }) => s + prob, 0)).toBeCloseTo(1, 10);
  });
});

describe("coinsPerHourOf", () => {
  it("cresce com a raridade", () => {
    const comum = coinsPerHourOf(4);
    const raro = coinsPerHourOf(7.5);
    const lendario = coinsPerHourOf(CONFIG.income.taperScore);
    expect(comum).toBeLessThan(raro);
    expect(raro).toBeLessThan(lendario);
  });

  it("sinergia multiplica linearmente a renda", () => {
    const base = coinsPerHourOf(5, 1);
    const comSinergia = coinsPerHourOf(5, 1.5);
    expect(comSinergia).toBeCloseTo(base * 1.5, 10);
  });

  it("taper do Lendário comprime a variação acima do piso (12/08/2026)", () => {
    // 14/08/2026: piso subiu de ~137/h pra ~298/h junto com o corte de Lendário (14.75 → 16.60,
    // pirâmide "Íngreme", ShimmerTiers.Legendary 0,2%→0,02%) — mesma curva exponencial, só
    // compõe por mais distância antes do taper entrar. Não é regressão.
    const taperScore = CONFIG.income.taperScore;
    const piso = coinsPerHourOf(taperScore);
    const topoObservado = coinsPerHourOf(taperScore + 6.25);
    expect(piso).toBeGreaterThan(285);
    expect(piso).toBeLessThan(310);
    expect(topoObservado / piso).toBeLessThan(2.5);
    // contínuo no corte, sem salto — tolerância relativa ao piso (a inclinação local escala
    // com o valor da função, não é sinal de descontinuidade real)
    expect(Math.abs(coinsPerHourOf(taperScore + 0.001) - coinsPerHourOf(taperScore - 0.001))).toBeLessThan(piso * 0.005);
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

describe("rarityBreakdownOf", () => {
  // Desde 13/08/2026: não sorteia mais nada — só recalcula probabilidade/pontos em cima dos
  // valores JÁ RESOLVIDOS em `creature.traits` (nenhum RNG, nenhum seed envolvido no cálculo).
  it("soma dos fatores bate com o total retornado", () => {
    const creature = { traits: generateTraits(42n) };
    const { total, factors } = rarityBreakdownOf(creature);
    const sum = factors.reduce((s, f) => s + f.points, 0);
    expect(sum).toBeCloseTo(total, 10);
  });

  it("sempre inclui o fator de shimmerTier do corpo", () => {
    const creature = { traits: generateTraits(42n) };
    const { factors } = rarityBreakdownOf(creature);
    expect(factors.some((f) => f.key === "shimmerTier")).toBe(true);
  });

  it("sem creature.breedingSource, todo fator tem source null", () => {
    const creature = { traits: generateTraits(42n) };
    const { factors } = rarityBreakdownOf(creature);
    for (const f of factors) expect(f.source ?? null).toBeNull();
  });

  it("com creature.breedingSource, mapeia ParentA/ParentB/Mutation pro rótulo esperado pela UI", () => {
    const creature = {
      traits: generateTraits(42n),
      breedingSource: [
        { key: "shimmerTier", part: null, source: "ParentA" },
        { key: "color", part: "tail", source: "ParentB" },
        { key: "pattern", part: "tail", source: "Mutation" },
      ],
    };
    const { factors } = rarityBreakdownOf(creature);
    expect(factors.find((f) => f.key === "shimmerTier").source).toBe("parentA");
    expect(factors.find((f) => f.key === "partColor" && f.part === "tail").source).toBe("parentB");
    expect(factors.find((f) => f.key === "patternType" && f.part === "tail").source).toBe("mutation");
    // patternColor reusa a mesma origem do pick de padrão (motor não rastreia um slot próprio)
    const patternColorFactor = factors.find((f) => f.key === "patternColor" && f.part === "tail");
    if (patternColorFactor) expect(patternColorFactor.source).toBe("mutation");
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
    const { factors } = rarityBreakdownOf({ traits: generateTraits(seed) });
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
      const { factors } = rarityBreakdownOf({ traits: generateTraits(seed) });
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
});

describe("waterDegradationPerFishPerHour", () => {
  it("cresce com a raridade (peixe mais raro suja mais)", () => {
    const comum = waterDegradationPerFishPerHour(5);
    const epico = waterDegradationPerFishPerHour(15);
    expect(epico).toBeGreaterThan(comum);
    expect(epico).toBeCloseTo(comum * 3, 10); // score 15 / ref 5 = 3x o peso de score 5
  });
});

