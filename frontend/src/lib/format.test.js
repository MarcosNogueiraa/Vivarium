import { describe, expect, it } from "vitest";
import { ageOf, factorLabel, partSummary, speedWord } from "./format.js";

describe("speedWord", () => {
  it.each([
    [5, "muito lenta"],
    [20, "lenta"],
    [50, "normal"],
    [70, "rápida"],
    [95, "muito rápida"],
  ])("classifica %i como %s", (speed, expected) => {
    expect(speedWord(speed)).toBe(expected);
  });
});

describe("ageOf", () => {
  it("menos de 1h mostra minutos", () => {
    const createdAt = new Date(Date.now() - 5 * 60_000).toISOString();
    expect(ageOf(createdAt)).toBe("5 min");
  });

  it("entre 1h e 24h mostra horas", () => {
    const createdAt = new Date(Date.now() - 3 * 3_600_000).toISOString();
    expect(ageOf(createdAt)).toBe("3 h");
  });

  it("24h ou mais mostra dias", () => {
    const createdAt = new Date(Date.now() - 2 * 86_400_000).toISOString();
    expect(ageOf(createdAt)).toBe("2 d");
  });
});

describe("factorLabel", () => {
  it("corpo sem brilho tem rótulo dedicado", () => {
    expect(factorLabel({ key: "shimmerTier", value: "None" })).toBe("Corpo sem brilho");
  });

  it("chave desconhecida cai de volta pra própria key (nunca quebra a UI)", () => {
    expect(factorLabel({ key: "trait-novo-desconhecido" })).toBe("trait-novo-desconhecido");
  });
});

describe("partSummary", () => {
  it("parte sem padrão só mostra a cor", () => {
    const part = { color: "Blue", pattern: "None" };
    expect(partSummary(part)).toMatch(/sem padrão/);
  });

  it("parte com padrão inclui tamanho e opacidade formatados", () => {
    const part = { color: "Red", pattern: "Dot", patternColor: "Yellow", patternSize: 42.4, patternOpacity: 67.8 };
    const summary = partSummary(part);
    expect(summary).toContain("tam 42");
    expect(summary).toContain("op 68%");
  });
});
