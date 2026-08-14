import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";
import { ProfileModal } from "./ProfileModal.jsx";

/** Botão de conta (ícone de pessoa) — dropdown com username/email, editar perfil + sair. */
export function AccountMenu({ onLogout, notify }) {
  const [open, setOpen] = useState(false);
  const [me, setMe] = useState(null);
  const [showProfile, setShowProfile] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    if (open && !me) api.me().then(setMe).catch(() => {});
  }, [open, me]);

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
      <button className="account-btn" onClick={() => setOpen((o) => !o)} aria-label="Conta" title="Conta">👤</button>
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
