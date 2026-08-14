import { useState } from "react";
import { api } from "../lib/api.js";

/**
 * Opt-out da coleta automática/Limpeza Automática de VIP — salva na hora, sem debounce (um
 * clique por vez). Extraído (13/08/2026) pra ser reaproveitado tanto pelos checkboxes
 * explicativos da Loja quanto pelos ícones rápidos do Tanque — mesma lógica, duas
 * representações visuais diferentes.
 */
export function useAutoToggles(tank, notify, onSaved) {
  const [busy, setBusy] = useState(false);

  async function set(field, checked) {
    setBusy(true);
    try {
      const autoCollectEnabled = field === "collect" ? checked : (tank?.autoCollectEnabled ?? true);
      const autoCleanEnabled = field === "clean" ? checked : (tank?.autoCleanEnabled ?? true);
      await api.setToggles(autoCollectEnabled, autoCleanEnabled);
      await onSaved();
    } catch (err) {
      notify(err.message);
    } finally {
      setBusy(false);
    }
  }

  return {
    busy,
    toggleCollect: () => set("collect", !(tank?.autoCollectEnabled ?? true)),
    toggleClean: () => set("clean", !(tank?.autoCleanEnabled ?? true)),
    setCollect: (checked) => set("collect", checked),
    setClean: (checked) => set("clean", checked),
  };
}
