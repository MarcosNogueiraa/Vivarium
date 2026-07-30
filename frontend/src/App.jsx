import { useState } from "react";
import { getToken } from "./lib/api.js";
import { AuthView } from "./views/AuthView.jsx";
import { GameView } from "./views/GameView.jsx";

export default function App() {
  const [authed, setAuthed] = useState(() => Boolean(getToken()));
  return authed
    ? <GameView onLogout={() => setAuthed(false)} />
    : <AuthView onAuthed={() => setAuthed(true)} />;
}
