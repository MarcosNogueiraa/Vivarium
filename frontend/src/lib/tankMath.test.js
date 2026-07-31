import { describe, expect, it } from "vitest";
import { traitsOf } from "./generator.js";
import { nextFishEta, tankFishSorted, tankPotential, tankSynergy } from "./tankMath.js";

function fish(id, seed, rarityScore) {
  return { id, seed: String(seed), rarityScore, isBred: false };
}

describe("tankSynergy", () => {
  it("agrupa peixes com o mesmo seed (logo, mesma cor de cauda) e ignora grupos de 1", () => {
    const a = fish(1, 111, 5);
    const b = fish(2, 111, 5); // seed idêntico → traits idênticos → mesma cor de cauda
    const c = fish(3, 222, 5); // provavelmente cor diferente, sozinho no grupo dele
    const col = traitsOf(a).tail.color;

    const synergy = tankSynergy([a, b, c]);
    const group = synergy.find((g) => g.color === col);

    expect(group).toBeDefined();
    expect(group.n).toBeGreaterThanOrEqual(2);
    expect(group.bonus).toBeGreaterThan(0);
    for (const g of synergy) expect(g.n).toBeGreaterThanOrEqual(2); // grupos de 1 não aparecem
  });

  it("tanque vazio não tem sinergia", () => {
    expect(tankSynergy([])).toEqual([]);
  });
});

describe("tankFishSorted", () => {
  it("ordena por produção (renda) decrescente", () => {
    const creatures = [fish(1, 111, 3), fish(2, 222, 15), fish(3, 333, 8)];
    const sorted = tankFishSorted(creatures);

    expect(sorted).toHaveLength(3);
    for (let i = 1; i < sorted.length; i++) expect(sorted[i - 1].prod).toBeGreaterThanOrEqual(sorted[i].prod);
  });
});

describe("tankPotential", () => {
  it("tanque vazio rende zero", () => {
    expect(tankPotential([])).toBe(0);
  });

  it("cresce com mais peixes", () => {
    const one = tankPotential([fish(1, 111, 5)]);
    const two = tankPotential([fish(1, 111, 5), fish(2, 222, 5)]);
    expect(two).toBeGreaterThan(one);
  });
});

describe("nextFishEta", () => {
  it("fila cheia retorna full: true", () => {
    const tank = { queue: [1, 2, 3], queueCap: 3 };
    expect(nextFishEta(tank)).toEqual({ full: true });
  });

  it("progresso zerado precisa do intervalo inteiro (fraction 0)", () => {
    const tank = {
      queue: [], queueCap: 5, generationIntervalMinutes: 25,
      generationProgressMinutes: 0, maintenanceLevel: 100, online: true,
    };
    const eta = nextFishEta(tank);
    expect(eta.full).toBe(false);
    expect(eta.fraction).toBe(0);
    expect(eta.mins).toBeCloseTo(25, 10);
  });

  it("offline demora mais que online (mesmo progresso)", () => {
    const base = { queue: [], queueCap: 5, generationIntervalMinutes: 25, generationProgressMinutes: 10, maintenanceLevel: 100 };
    const online = nextFishEta({ ...base, online: true });
    const offline = nextFishEta({ ...base, online: false });
    expect(offline.mins).toBeGreaterThan(online.mins);
  });

  it("água baixa (<40) demora mais que água cheia", () => {
    const base = { queue: [], queueCap: 5, generationIntervalMinutes: 25, generationProgressMinutes: 10, online: true };
    const fullWater = nextFishEta({ ...base, maintenanceLevel: 100 });
    const lowWater = nextFishEta({ ...base, maintenanceLevel: 20 });
    expect(lowWater.mins).toBeGreaterThan(fullWater.mins);
  });
});
