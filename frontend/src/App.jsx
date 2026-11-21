import { useState } from "react";
import Search from "./Search.jsx";
import Requests from "./Requests.jsx";
import Me from "./Me.jsx";
import Stats from "./Stats.jsx";

const TABS = [
  ["search", "Find blood"],
  ["requests", "Requests"],
  ["me", "Account"],
  ["stats", "Impact"],
];

export default function App() {
  const [tab, setTab] = useState("search");
  return (
    <main>
      <h1>🩸 Blood Donor Finder</h1>
      <nav>
        {TABS.map(([key, label]) => (
          <button key={key} className={tab === key ? "active" : ""} onClick={() => setTab(key)}>
            {label}
          </button>
        ))}
      </nav>
      {tab === "search" && <Search />}
      {tab === "requests" && <Requests goToProfile={() => setTab("me")} />}
      {tab === "me" && <Me />}
      {tab === "stats" && <Stats />}
    </main>
  );
}
