import { useState } from "react";

/**
 * Seção recolhível (nasce fechada) — reaproveita o visual do .eyebrow, só some o corpo até o clique.
 *
 * variant="prominent" (12/08/2026): visual tipo cartão (fundo, borda, ícone, badge de contagem
 * destacado) — usado quando a seção precisa se destacar solta direto na página (ex: "Filtros
 * avançados" na Mochila/Ninho), diferente do visual sutil default (usado dentro de um modal já
 * hierarquizado, ex: FishDetail). Sem `variant`, o comportamento/visual continua EXATAMENTE
 * igual ao original — não regride nenhum uso existente.
 */
export function CollapsibleSection({ title, children, variant, hint, icon = "🎛️" }) {
  const [open, setOpen] = useState(false);
  const prominent = variant === "prominent";
  return (
    <div className={prominent ? "detail-section detail-section--prominent" : "detail-section"}>
      <button
        className={prominent ? "detail-section-head detail-section-head--prominent" : "detail-section-head"}
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
      >
        <span className="detail-section-head-title">
          {prominent && <span className="detail-section-icon" aria-hidden="true">{icon}</span>}
          <span className="eyebrow">{title}</span>
        </span>
        <span className={`chevron${open ? " open" : ""}`}>▾</span>
      </button>
      {prominent && hint && !open && <p className="detail-section-hint">{hint}</p>}
      {open && children}
    </div>
  );
}
