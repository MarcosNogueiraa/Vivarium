import { describe, expect, it } from "vitest";
import { decorTierOf } from "./fishRenderer.js";

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
