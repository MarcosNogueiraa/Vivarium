// Rótulos e formatação humana (PT-BR) dos traits e do breakdown de raridade.
import { PT } from "./fishRenderer.js";

export const PART_PT = { tail: "Cauda", dorsal: "Nadadeira dorsal", pectoral: "Barbatana peitoral" };

export const RARITY_RANGES = ["menos de 5.4", "5.4 – 7.5", "7.5 – 9.8", "9.8 – 14.0", "14.0 ou mais"];

// Rótulo humano de um fator do breakdown de raridade
export function factorLabel(f) {
  switch (f.key) {
    case "shimmerTier":
      return f.value === "None" ? "Corpo sem brilho" : `Brilho ${PT.tier[f.value].toLowerCase()}`;
    case "partColor":
      return `${PART_PT[f.part]}: ${PT.color[f.value]}`;
    case "patternType":
      return `${PART_PT[f.part]}: ${PT.pattern[f.value].toLowerCase()}`;
    case "patternColor":
      return `Cor do padrão (${PART_PT[f.part].toLowerCase()}): ${PT.color[f.value]}`;
    case "patternSizeExtreme":
      return `Padrão ${f.value} (${PART_PT[f.part].toLowerCase()})`;
    case "patternOpacityExtreme":
      return `Opacidade ${f.value} do padrão (${PART_PT[f.part].toLowerCase()})`;
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
  const mins = Math.max(0, (Date.now() - new Date(createdAt).getTime()) / 60000);
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
