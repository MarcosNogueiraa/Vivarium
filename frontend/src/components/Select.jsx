import { useEffect, useRef, useState } from "react";

/**
 * Dropdown customizado — substitui `<select>` nativo. O popup nativo do
 * navegador ignora boa parte do nosso CSS (cada engine estiliza diferente,
 * várias simplesmente usam sempre o tema claro do SO pro texto das opções),
 * então mesmo com `color-scheme`/`option{}` o texto ficava ilegível em
 * alguns navegadores (feedback do usuário). Construir a lista nós mesmos
 * garante o mesmo visual escuro em qualquer lugar.
 */
export function Select({ value, onChange, options }) {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    function onDocClick(e) { if (ref.current && !ref.current.contains(e.target)) setOpen(false); }
    function onKey(e) { if (e.key === "Escape") setOpen(false); }
    document.addEventListener("mousedown", onDocClick);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDocClick); document.removeEventListener("keydown", onKey); };
  }, []);

  const current = options.find((o) => o.value === value);

  return (
    <div className="custom-select" ref={ref}>
      <button type="button" className="custom-select-btn" onClick={() => setOpen((v) => !v)} aria-haspopup="listbox" aria-expanded={open}>
        <span>{current?.label ?? ""}</span>
        <span className="custom-select-caret">{open ? "▴" : "▾"}</span>
      </button>
      {open && (
        <ul className="custom-select-list" role="listbox">
          {options.map((o) => (
            <li
              key={o.value}
              role="option"
              aria-selected={o.value === value}
              className={`custom-select-option${o.value === value ? " selected" : ""}`}
              onClick={() => { onChange(o.value); setOpen(false); }}
            >
              {o.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
