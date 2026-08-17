// Ícone de ova de peixe (mini versão do ovo da celebração) — não o emoji 🥚 de galinha
// (feedback do usuário, §7.21). Cor por tier vem do CSS (.mini-fish-egg--{tier}, styles.css).
export function EggIcon({ tier }) {
  return (
    <span className={`mini-fish-egg mini-fish-egg--${tier}`} aria-hidden="true">
      <span className="mfe mfe-a" /><span className="mfe mfe-b" /><span className="mfe mfe-main" />
    </span>
  );
}
