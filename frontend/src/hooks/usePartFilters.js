import { useState } from "react";
import { traitsOf } from "../lib/generator.js";

// 14/08/2026: filtro de aparência por parte (cauda/dorsal/peitoral), extraído do Mercado
// (única tela que já tinha a versão multi-seleção) — Mochila e Ninho usavam uma versão mais
// antiga (single-select, "toda cor" ou UMA cor) e divergiam em comportamento. Agora as 3 telas
// usam este mesmo hook, então terão sempre o mesmo comportamento por construção.
export const PARTS = ["tail", "dorsal", "pectoral"];

const emptyPartFilter = { colors: [], patterns: [] };
const emptyPartFilters = { tail: { ...emptyPartFilter }, dorsal: { ...emptyPartFilter }, pectoral: { ...emptyPartFilter } };

/**
 * Filtro de cor/padrão por parte, multi-seleção (OR dentro do mesmo atributo — "dorsal verde OU
 * vermelha"). Clicar numa cor/padrão já marcado desmarca; sem nada marcado, "Toda cor"/"Todo
 * padrão" volta a ficar ativo sozinho (é só o estado vazio, `.length === 0`, sem lógica extra).
 */
export function usePartFilters() {
  const [partFilters, setPartFilters] = useState(emptyPartFilters);

  function toggleColor(part, color) {
    setPartFilters((prev) => {
      const cur = prev[part].colors;
      const colors = cur.includes(color) ? cur.filter((c) => c !== color) : [...cur, color];
      return { ...prev, [part]: { ...prev[part], colors } };
    });
  }
  function togglePattern(part, pattern) {
    setPartFilters((prev) => {
      const cur = prev[part].patterns;
      const patterns = cur.includes(pattern) ? cur.filter((p) => p !== pattern) : [...cur, pattern];
      return { ...prev, [part]: { ...prev[part], patterns } };
    });
  }
  function clearColors(part) {
    setPartFilters((prev) => ({ ...prev, [part]: { ...prev[part], colors: [] } }));
  }
  function clearPatterns(part) {
    setPartFilters((prev) => ({ ...prev, [part]: { ...prev[part], patterns: [] } }));
  }
  function reset() {
    setPartFilters(emptyPartFilters);
  }

  const activeCount = PARTS.reduce(
    (n, part) => n + partFilters[part].colors.length + partFilters[part].patterns.length,
    0
  );

  /** Predicado local (Mochila/Ninho, que filtram no cliente — o Mercado filtra no servidor). */
  function matches(creature) {
    const t = traitsOf(creature);
    return PARTS.every((part) => {
      const f = partFilters[part];
      return (f.colors.length === 0 || f.colors.includes(t[part].color))
        && (f.patterns.length === 0 || f.patterns.includes(t[part].pattern));
    });
  }

  return { partFilters, toggleColor, togglePattern, clearColors, clearPatterns, reset, activeCount, matches };
}
