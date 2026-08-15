// Rótulos e formatação humana (PT-BR) dos traits e do breakdown de raridade.
import { PT } from "./fishRenderer.js";

export const PART_PT = { tail: "Cauda", dorsal: "Dorsal", pectoral: "Peitoral" };

// Recalibrado 14-15/08/2026 junto com BANDS (fishRenderer.js) — pirâmide "Íngreme", ajustada
// em 15/08 pra Raro~1,00%/Épico~0,30% (12.24→12.04, 14.85→13.78), manter em sincronia.
export const RARITY_RANGES = ["menos de 5.45", "5.45 – 12.04", "12.04 – 13.78", "13.78 – 16.60", "16.60 ou mais"];

// Rótulo humano de um fator do breakdown de raridade (atributo primeiro, parte depois)
export function factorLabel(f) {
  switch (f.key) {
    case "shimmerTier":
      return f.value === "None" ? "Corpo sem brilho" : `Brilho ${PT.tier[f.value].toLowerCase()}`;
    case "shimmerColor":
      return `Cor do brilho: ${PT.shimmer[f.value]}`;
    case "partColor":
      return `Cor ${PART_PT[f.part]}: ${PT.color[f.value]}`;
    case "patternType":
      return `Padrão ${PART_PT[f.part]}: ${PT.pattern[f.value].toLowerCase()}`;
    case "patternColor":
      return `Cor padrão ${PART_PT[f.part]}: ${PT.color[f.value]}`;
    case "patternSizeExtreme":
      return `Padrão ${f.value} (${PART_PT[f.part]})`;
    case "patternOpacityExtreme":
      return `Opacidade ${f.value} (${PART_PT[f.part]})`;
    case "speedExtreme":
      return `${f.part === "tail" ? "Cauda" : "Nadadeira"} ${f.value}`;
    case "samePattern":
      return `Conjunto: mesmo padrão em ${f.value} partes`;
    case "sameColor":
      return `Conjunto: mesma cor em ${f.value} partes`;
    default:
      return f.key;
  }
}

export const speedWord = (s) =>
  s < 10 ? "muito lenta" : s < 35 ? "lenta" : s > 90 ? "muito rápida" : s > 65 ? "rápida" : "normal";

export function ageOf(createdAt) {
  // ListingDto (Mercado) não expõe createdAt do dono original — sem essa guarda,
  // new Date(undefined) vira Invalid Date e o cálculo inteiro propaga NaN, mostrando
  // literalmente "NaN d" no modal de detalhe (achado real ao revisar o Mercado).
  const time = createdAt == null ? NaN : new Date(createdAt).getTime();
  if (Number.isNaN(time)) return null;
  const mins = Math.max(0, (Date.now() - time) / 60000);
  if (mins < 60) return `${Math.floor(mins)} min`;
  const h = mins / 60;
  if (h < 24) return `${Math.floor(h)} h`;
  return `${Math.floor(h / 24)} d`;
}

export function partSummary(part) {
  const c = PT.color[part.color];
  if (part.pattern === "None") return `${c} · sem padrão`;
  return `${c} · ${PT.pattern[part.pattern].toLowerCase()} ${PT.color[part.patternColor]} `
    + `(tam ${part.patternSize.toFixed(0)}, op ${part.patternOpacity.toFixed(0)}%)`;
}
