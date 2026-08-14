import { PART_HEX, PT } from "../lib/fishRenderer.js";
import { PART_PT } from "../lib/format.js";
import { CONFIG } from "../lib/generator.js";
import { CollapsibleSection } from "./CollapsibleSection.jsx";
import { PARTS } from "../hooks/usePartFilters.js";

const PATTERN_VALUES = CONFIG.patternTypes.map(([v]) => v);

/** UI dos filtros de cor/padrão por parte — controlada pelo hook `usePartFilters`. */
export function AppearanceFilters({ partFilters, activeCount, onToggleColor, onTogglePattern, onClearColors, onClearPatterns, hint }) {
  return (
    <CollapsibleSection
      variant="prominent"
      hint={hint ?? "Marque quantos valores quiser em cada atributo — dentro do mesmo atributo funciona como OU (ex: dorsal verde OU vermelha)."}
      title={
        <>
          Filtros avançados{" "}
          {activeCount > 0 && <span className="filter-count-badge">({activeCount})</span>}
        </>
      }
    >
      <div className="appearance-filter-group">
        {PARTS.map((part) => (
          <div className="appearance-filter-part" key={part}>
            <strong>{PART_PT[part]}</strong>
            <div className="filter-chips">
              <button
                className={`filter-chip${partFilters[part].colors.length === 0 ? " active" : ""}`}
                onClick={() => onClearColors(part)}
              >
                Toda cor
              </button>
              {Object.keys(PART_HEX).map((color) => (
                <button
                  key={color}
                  className={`filter-chip color-chip${partFilters[part].colors.includes(color) ? " active" : ""}`}
                  style={{ "--tier": PART_HEX[color] }}
                  title={PT.color[color]}
                  onClick={() => onToggleColor(part, color)}
                >
                  <span className="dot-color" style={{ background: PART_HEX[color] }} />
                </button>
              ))}
            </div>
            <div className="filter-chips">
              <button
                className={`filter-chip${partFilters[part].patterns.length === 0 ? " active" : ""}`}
                onClick={() => onClearPatterns(part)}
              >
                Todo padrão
              </button>
              {PATTERN_VALUES.map((pattern) => (
                <button
                  key={pattern}
                  className={`filter-chip${partFilters[part].patterns.includes(pattern) ? " active" : ""}`}
                  onClick={() => onTogglePattern(part, pattern)}
                >
                  {PT.pattern[pattern]}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </CollapsibleSection>
  );
}
