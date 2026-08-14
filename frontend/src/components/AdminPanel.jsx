import { useState } from "react";
import { Modal } from "./Modal.jsx";
import { api } from "../lib/api.js";

/**
 * Menu único de ferramentas administrativas (13/08/2026) — antes eram 2 botões soltos
 * no topbar (dar peixe a todos / premium a todos); consolidado aqui a pedido do usuário,
 * junto com um ajuste de carteira mirado em um jogador específico. Só pra uso do admin,
 * sem preocupação extra de UX (feedback inline via texto simples, sem toast).
 */
export function AdminPanel({ onClose, notify }) {
  const [busyAll, setBusyAll] = useState(false);

  const [username, setUsername] = useState("");
  const [currencyCode, setCurrencyCode] = useState("SOFT");
  const [mode, setMode] = useState("add");
  const [amount, setAmount] = useState("");
  const [busyWallet, setBusyWallet] = useState(false);
  const [walletError, setWalletError] = useState(null);

  const [msgAudience, setMsgAudience] = useState("All");
  const [msgUsernamesRaw, setMsgUsernamesRaw] = useState("");
  const [msgTitle, setMsgTitle] = useState("");
  const [msgBody, setMsgBody] = useState("");
  const [msgRewardCode, setMsgRewardCode] = useState("");
  const [msgRewardAmount, setMsgRewardAmount] = useState("");
  const [busyMsg, setBusyMsg] = useState(false);
  const [msgError, setMsgError] = useState(null);

  async function giveFishToAll() {
    setBusyAll(true);
    try {
      const { habitatsAffected } = await api.adminGiveStarterFishAll();
      notify(`+1 peixe pronto pra ${habitatsAffected} jogador(es)`);
    } catch (err) { notify(err.message); }
    finally { setBusyAll(false); }
  }

  async function grantPremiumToAll() {
    setBusyAll(true);
    try {
      const { usersAffected, amount } = await api.adminGrantPremiumAll(1000);
      notify(`+${amount} premium pra ${usersAffected} jogador(es)`);
    } catch (err) { notify(err.message); }
    finally { setBusyAll(false); }
  }

  async function adjustWallet(e) {
    e.preventDefault();
    setWalletError(null);
    setBusyWallet(true);
    try {
      const { balance } = await api.adminAdjustWallet(username.trim(), currencyCode, mode, Number(amount));
      notify(`${username.trim()}: saldo de ${currencyCode} agora é ${balance}`);
      setAmount("");
    } catch (err) {
      setWalletError(err.message);
    } finally {
      setBusyWallet(false);
    }
  }

  async function sendInboxMessage(e) {
    e.preventDefault();
    setMsgError(null);
    setBusyMsg(true);
    try {
      const usernames = msgAudience === "Selected"
        ? msgUsernamesRaw.split(",").map((u) => u.trim()).filter(Boolean)
        : null;
      const { recipientCount, notFoundUsernames } = await api.adminSendInboxMessage(
        msgTitle.trim(), msgBody.trim(), msgAudience, usernames,
        msgRewardCode || null, msgRewardCode ? Number(msgRewardAmount) : null,
      );
      notify(
        notFoundUsernames?.length
          ? `Enviado pra ${recipientCount} jogador(es). Não encontrados: ${notFoundUsernames.join(", ")}`
          : `Enviado pra ${recipientCount} jogador(es).`,
      );
      setMsgTitle(""); setMsgBody(""); setMsgUsernamesRaw(""); setMsgRewardCode(""); setMsgRewardAmount("");
    } catch (err) {
      setMsgError(err.message);
    } finally {
      setBusyMsg(false);
    }
  }

  return (
    <Modal onClose={onClose}>
      <div className="eyebrow">🛠️ Ferramentas de admin</div>

      <h4 style={{ marginTop: 16 }}>Ações globais</h4>
      <div className="prompt-actions" style={{ justifyContent: "flex-start", flexWrap: "wrap" }}>
        <button type="button" className="btn-primary" disabled={busyAll} onClick={giveFishToAll}>
          🎣 Dar peixe a todos
        </button>
        <button type="button" className="btn-primary" disabled={busyAll} onClick={grantPremiumToAll}>
          💎 1000 premium a todos
        </button>
      </div>

      <h4 style={{ marginTop: 24 }}>Ajustar carteira de um jogador</h4>
      <form className="prompt-form" onSubmit={adjustWallet}>
        <label className="prompt-label">Username</label>
        <input
          type="text" value={username} placeholder="ex: marcospdn" autoFocus
          onChange={(e) => setUsername(e.target.value)}
        />

        <label className="prompt-label">Moeda</label>
        <select value={currencyCode} onChange={(e) => setCurrencyCode(e.target.value)}>
          <option value="SOFT">Soft</option>
          <option value="PREMIUM">Premium (💎)</option>
        </select>

        <label className="prompt-label">Modo</label>
        <select value={mode} onChange={(e) => setMode(e.target.value)}>
          <option value="add">Adicionar (soma; use negativo pra remover)</option>
          <option value="set">Definir saldo (substitui o valor atual)</option>
        </select>

        <label className="prompt-label">Quantia</label>
        <input
          type="number" value={amount} placeholder="ex: 1000"
          onChange={(e) => setAmount(e.target.value)}
        />

        {walletError && <div className="error">{walletError}</div>}
        <div className="prompt-actions">
          <button type="button" onClick={onClose}>Fechar</button>
          <button type="submit" className="btn-primary" disabled={busyWallet || !username.trim() || amount === ""}>
            {busyWallet ? "…" : "Aplicar"}
          </button>
        </div>
      </form>

      <h4 style={{ marginTop: 24 }}>Mandar mensagem pra Caixa de Entrada</h4>
      <form className="prompt-form" onSubmit={sendInboxMessage}>
        <label className="prompt-label">Público</label>
        <select value={msgAudience} onChange={(e) => setMsgAudience(e.target.value)}>
          <option value="All">Todos os jogadores</option>
          <option value="Selected">Lista de usernames</option>
        </select>

        {msgAudience === "Selected" && (
          <>
            <label className="prompt-label">Usernames (separados por vírgula)</label>
            <input
              type="text" value={msgUsernamesRaw} placeholder="ex: fulano, beltrano"
              onChange={(e) => setMsgUsernamesRaw(e.target.value)}
            />
          </>
        )}

        <label className="prompt-label">Título</label>
        <input type="text" value={msgTitle} onChange={(e) => setMsgTitle(e.target.value)} />

        <label className="prompt-label">Mensagem</label>
        <textarea value={msgBody} onChange={(e) => setMsgBody(e.target.value)} />

        <label className="prompt-label">Recompensa (opcional)</label>
        <select value={msgRewardCode} onChange={(e) => setMsgRewardCode(e.target.value)}>
          <option value="">Sem recompensa</option>
          <option value="SOFT">Soft</option>
          <option value="PREMIUM">Premium (💎)</option>
        </select>
        {msgRewardCode && (
          <input
            type="number" value={msgRewardAmount} placeholder="quantia"
            onChange={(e) => setMsgRewardAmount(e.target.value)}
          />
        )}

        {msgError && <div className="error">{msgError}</div>}
        <div className="prompt-actions">
          <button type="button" onClick={onClose}>Fechar</button>
          <button
            type="submit" className="btn-primary"
            disabled={
              busyMsg || !msgTitle.trim() || !msgBody.trim()
              || (msgAudience === "Selected" && !msgUsernamesRaw.trim())
              || (msgRewardCode && !msgRewardAmount)
            }
          >
            {busyMsg ? "…" : "Enviar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
