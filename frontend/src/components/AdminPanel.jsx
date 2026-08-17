import { useState } from "react";
import { Modal } from "./Modal.jsx";
import { CollapsibleSection } from "./CollapsibleSection.jsx";
import { api } from "../lib/api.js";
import { EGG_NAMES } from "../lib/eggs.js";

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
  // §7.21 (17/08/2026): admin também pode dar ovo, não só moeda. Array com repetição = quantidade
  // (ex: ["egg_common","egg_common","egg_legendary"]) — dá pra misturar tiers na mesma mensagem
  // (pedido do usuário no mesmo dia: mais de um ovo, de mais de uma raridade, na mesma mensagem).
  const [msgRewardEggs, setMsgRewardEggs] = useState([]);
  const [msgEggPick, setMsgEggPick] = useState("");
  const [busyMsg, setBusyMsg] = useState(false);
  const [msgError, setMsgError] = useState(null);

  function addEggToCart() {
    if (!msgEggPick) return;
    setMsgRewardEggs((prev) => [...prev, msgEggPick]);
  }
  function removeOneEggFromCart(key) {
    setMsgRewardEggs((prev) => {
      const idx = prev.indexOf(key);
      if (idx === -1) return prev;
      return [...prev.slice(0, idx), ...prev.slice(idx + 1)];
    });
  }
  // { egg_common: 2, egg_legendary: 1 } — pra mostrar "Ovo Comum ×2" em vez de 2 chips iguais.
  const eggCartCounts = msgRewardEggs.reduce((acc, key) => ({ ...acc, [key]: (acc[key] ?? 0) + 1 }), {});

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

  // 15/08/2026, pedido do usuário: "Dar moedas" (antes um formulário só de mensagem com
  // recompensa opcional) vira a tela principal de dar moedas — pública (todos/lista, já
  // existia) + recompensa em destaque + mensagem agora OPCIONAL. Sem mudança de backend: título/
  // corpo continuam obrigatórios lá (schema não-anulável), então quando o admin não escreve nada
  // e só quer mandar moedas, o front preenche um texto padrão antes de enviar.
  const hasReward = Boolean((msgRewardCode && msgRewardAmount) || msgRewardEggs.length > 0);
  const hasCustomMessage = Boolean(msgTitle.trim() && msgBody.trim());

  async function sendInboxMessage(e) {
    e.preventDefault();
    setMsgError(null);
    setBusyMsg(true);
    try {
      const usernames = msgAudience === "Selected"
        ? msgUsernamesRaw.split(",").map((u) => u.trim()).filter(Boolean)
        : null;
      const title = msgTitle.trim() || (msgRewardEggs.length > 0 ? "🥚 Presente do admin" : "🎁 Presente do admin");
      const body = msgBody.trim() || "Você recebeu uma recompensa da equipe do jogo!";
      const { recipientCount, notFoundUsernames } = await api.adminSendInboxMessage(
        title, body, msgAudience, usernames,
        msgRewardCode || null, msgRewardCode ? Number(msgRewardAmount) : null,
        msgRewardEggs.length > 0 ? msgRewardEggs : null,
      );
      notify(
        notFoundUsernames?.length
          ? `Enviado pra ${recipientCount} jogador(es). Não encontrados: ${notFoundUsernames.join(", ")}`
          : `Enviado pra ${recipientCount} jogador(es).`,
      );
      setMsgTitle(""); setMsgBody(""); setMsgUsernamesRaw(""); setMsgRewardCode(""); setMsgRewardAmount("");
      setMsgRewardEggs([]); setMsgEggPick("");
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

      <CollapsibleSection title="🔧 Corrigir carteira de um jogador">
        <p className="hint" style={{ padding: 0, marginBottom: 8 }}>
          Crédito direto e silencioso (não passa pela Caixa de Entrada) — pra corrigir saldo, não
          pra presentear. Pra dar moedas de verdade, use "🎁 Dar moedas / ovo" abaixo.
        </p>
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
      </CollapsibleSection>

      <CollapsibleSection title="🎁 Dar moedas / ovo">
        <p className="hint" style={{ padding: 0, marginBottom: 8 }}>
          Vira uma entrega na Caixa de Entrada — o jogador precisa resgatar. Mensagem é opcional
          (some com "🎁 Presente do admin" se você deixar em branco).
        </p>
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

          <label className="prompt-label">Moeda</label>
          <select value={msgRewardCode} onChange={(e) => setMsgRewardCode(e.target.value)}>
            <option value="">Sem moeda</option>
            <option value="SOFT">Soft</option>
            <option value="PREMIUM">Premium (💎)</option>
          </select>
          {msgRewardCode && (
            <input
              type="number" value={msgRewardAmount} placeholder="quantia"
              onChange={(e) => setMsgRewardAmount(e.target.value)}
            />
          )}

          <label className="prompt-label">Ovos (opcional — dá pra adicionar vários, de tiers diferentes)</label>
          <div className="card-row">
            <select value={msgEggPick} onChange={(e) => setMsgEggPick(e.target.value)}>
              <option value="">Escolher tier…</option>
              {Object.entries(EGG_NAMES).map(([key, name]) => (
                <option key={key} value={key}>{name}</option>
              ))}
            </select>
            <button type="button" disabled={!msgEggPick} onClick={addEggToCart}>+ Adicionar</button>
          </div>
          {Object.keys(eggCartCounts).length > 0 && (
            <div className="card-row" style={{ flexWrap: "wrap", gap: 6 }}>
              {Object.entries(eggCartCounts).map(([key, count]) => (
                <span key={key} className="badge" style={{ "--tier": "var(--muted)" }}>
                  {EGG_NAMES[key]} ×{count}
                  <button
                    type="button" className="link-btn" style={{ marginLeft: 6 }}
                    onClick={() => removeOneEggFromCart(key)}
                  >
                    ✕
                  </button>
                </span>
              ))}
            </div>
          )}
          <p className="hint" style={{ padding: 0, marginTop: -6 }}>
            Cada peixe é gerado só quando o jogador resgatar o ovo correspondente (mesma sorte de comprar na Loja).
          </p>

          <label className="prompt-label">Título (opcional)</label>
          <input
            type="text" value={msgTitle} placeholder="🎁 Presente do admin"
            onChange={(e) => setMsgTitle(e.target.value)}
          />

          <label className="prompt-label">Mensagem (opcional)</label>
          <textarea
            value={msgBody} placeholder="Você recebeu uma recompensa da equipe do jogo!"
            onChange={(e) => setMsgBody(e.target.value)}
          />

          {msgError && <div className="error">{msgError}</div>}
          <div className="prompt-actions">
            <button type="button" onClick={onClose}>Fechar</button>
            <button
              type="submit" className="btn-primary"
              disabled={
                busyMsg || (!hasReward && !hasCustomMessage)
                || (msgAudience === "Selected" && !msgUsernamesRaw.trim())
                || (msgRewardCode && !msgRewardAmount)
              }
            >
              {busyMsg ? "…" : "Enviar"}
            </button>
          </div>
        </form>
      </CollapsibleSection>
    </Modal>
  );
}
