import { describe, expect, it } from "vitest";
import { decorTierOf, shadowParamsFor } from "./fishRenderer.js";

describe("decorTierOf", () => {
  it("mapeia os nomes de faixa do backend pro tier de decoração", () => {
    expect(decorTierOf("Aquário")).toBe(0);
    expect(decorTierOf("Aquário Grande")).toBe(1);
    expect(decorTierOf("Aquário Master")).toBe(2);
  });

  it("cai pro tier 0 (neutro) em nome desconhecido/ausente", () => {
    expect(decorTierOf("")).toBe(0);
    expect(decorTierOf(undefined)).toBe(0);
    expect(decorTierOf("Aquário Desconhecido")).toBe(0);
  });
});

describe("shadowParamsFor", () => {
  it("peixe encostado no chão tem sombra maior e mais escura que um raso", () => {
    const floorY = 400;
    const fundo = shadowParamsFor(390, floorY);
    const raso = shadowParamsFor(100, floorY);
    expect(fundo.radiusX).toBeGreaterThan(raso.radiusX);
    expect(fundo.alpha).toBeGreaterThan(raso.alpha);
  });

  it("nunca fica com alpha/raio negativo ou fora da faixa esperada", () => {
    const floorY = 400;
    for (const y of [-50, 0, 200, 400, 1000]) {
      const { radiusX, alpha } = shadowParamsFor(y, floorY);
      expect(radiusX).toBeGreaterThan(0);
      expect(alpha).toBeGreaterThanOrEqual(0);
      expect(alpha).toBeLessThanOrEqual(1);
    }
  });
});
