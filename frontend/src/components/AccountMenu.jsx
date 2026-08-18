import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";
import { ProfileModal } from "./ProfileModal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";

/** Botão de conta (avatar do jogador ou ícone padrão) — dropdown com username/email, editar
 * perfil + sair. Avatar/nível carregados no mount (18/08/2026, BACKLOG.md #7) — precisam
 * aparecer no botão fechado, não só depois de abrir o dropdown. */
export function AccountMenu({ onLogout, notify }) {
  const [open, setOpen] = useState(false);
  const [me, setMe] = useState(null);
  const [showProfile, setShowProfile] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    api.me().then(setMe).catch(() => {});
  }, []);

  useEffect(() => {
    if (!open) return;
    function onOutside(e) { if (ref.current && !ref.current.contains(e.target)) setOpen(false); }
    function onEsc(e) { if (e.key === "Escape") setOpen(false); }
    document.addEventListener("mousedown", onOutside);
    document.addEventListener("keydown", onEsc);
    return () => {
      document.removeEventListener("mousedown", onOutside);
      document.removeEventListener("keydown", onEsc);
    };
  }, [open]);

  return (
    <div className="account-menu" ref={ref}>
      <button className="account-btn" onClick={() => setOpen((o) => !o)} aria-label="Conta" title="Conta">
        {me?.avatar
          ? <span className="avatar-fish-canvas"><FishCanvas creature={me.avatar} width={38} /></span>
          : "👤"}
      </button>
      {me != null && (
        <div className="level-chip" title={`Nível ${me.level} — ${me.currentLevelXp}/${me.xpForNextLevel} XP`}>
          <span className="level-chip-label">Nv. {me.level}</span>
          <div className="level-chip-bar"><div className="level-chip-bar-fill" style={{ width: `${Math.round(me.progress01 * 100)}%` }} /></div>
        </div>
      )}
      {open && (
        <div className="account-dropdown">
          <div className="account-info">
            <div className="account-username">{me?.username ?? "…"}</div>
            <div className="account-email faint">{me?.email ?? ""}</div>
          </div>
          <button className="account-edit" onClick={() => { setShowProfile(true); setOpen(false); }}>
            ✏️ Editar perfil
          </button>
          <button className="account-logout" onClick={onLogout}>Sair</button>
        </div>
      )}
      {showProfile && (
        <ProfileModal
          me={me}
          notify={notify}
          onUpdated={(updated) => setMe((prev) => ({ ...prev, ...updated }))}
          onClose={() => setShowProfile(false)}
        />
      )}
    </div>
  );
}
