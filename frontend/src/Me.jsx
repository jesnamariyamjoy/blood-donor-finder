import { useEffect, useState } from "react";
import { register, login, getMe, recordDonation, setAvailability, setSms, deleteMe, getLocation } from "./api";
import { GROUPS, getToken, setToken } from "./shared";

const empty = { name: "", bloodGroup: "O+", city: "", phone: "", password: "", lastDonation: "" };

export default function Me() {
  const [donor, setDonor] = useState(null);
  const [mode, setMode] = useState("login");
  const [form, setForm] = useState(empty);
  const [coords, setCoords] = useState(null);
  const [msg, setMsg] = useState(null);
  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value });

  useEffect(() => {
    if (getToken()) getMe().then(setDonor).catch(() => setToken(null));
  }, []);

  async function locate() {
    try { setCoords(await getLocation()); } catch (e) { setMsg({ ok: false, text: e.message }); }
  }

  async function onAuth(e) {
    e.preventDefault(); setMsg(null);
    try {
      const res = mode === "login"
        ? await login({ phone: form.phone, password: form.password })
        : await register({ ...form, lastDonation: form.lastDonation || null, lat: coords?.lat, lng: coords?.lng });
      setToken(res.token); setDonor(res.profile); setForm(empty);
    } catch (err) { setMsg({ ok: false, text: err.message }); }
  }

  async function act(fn) {
    setMsg(null);
    try { setDonor(await fn()); } catch (err) { setMsg({ ok: false, text: err.message }); }
  }

  function logout() { setToken(null); setDonor(null); }

  async function onDelete() {
    if (!confirm("Delete your account permanently?")) return;
    try { await deleteMe(); logout(); } catch (err) { setMsg({ ok: false, text: err.message }); }
  }

  if (donor) {
    const eligible = donor.available && (!donor.nextEligible || new Date(donor.nextEligible) <= new Date());
    return (
      <section>
        <div className="card col">
          <div>
            <strong>{donor.name}</strong>
            <span className="badge exact">{donor.bloodGroup}</span>
            <span className={"badge lvl-" + donor.level}>{donor.level}</span>
          </div>
          <div className="muted">{donor.city} · {donor.donationCount} donation(s)</div>
          <div className={eligible ? "ok" : "muted"}>
            {!donor.available ? "Marked unavailable"
              : eligible ? "✓ You're eligible to donate now"
              : `Next eligible on ${donor.nextEligible}`}
          </div>
          <div className="row">
            <button onClick={() => act(recordDonation)}>I donated today</button>
            <button className="ghost" onClick={() => act(() => setAvailability(!donor.available))}>
              {donor.available ? "Pause availability" : "Resume availability"}
            </button>
          </div>
          <button className="ghost" onClick={() => act(() => setSms(!donor.smsAlerts))}>
            {donor.smsAlerts ? "🔔 SMS alerts on (tap to turn off)" : "🔕 SMS alerts off (tap to turn on)"}
          </button>
          <div className="row">
            <button className="ghost" onClick={logout}>Log out</button>
            <button className="ghost" onClick={onDelete}>Delete account</button>
          </div>
        </div>
        {msg && <p className={msg.ok ? "ok" : "error"}>{msg.text}</p>}
      </section>
    );
  }

  return (
    <section>
      <div className="row">
        <button className={mode === "login" ? "" : "ghost"} onClick={() => setMode("login")}>Log in</button>
        <button className={mode === "register" ? "" : "ghost"} onClick={() => setMode("register")}>Create account</button>
      </div>
      <form onSubmit={onAuth} className="col box">
        {mode === "register" && <input placeholder="Full name" value={form.name} onChange={set("name")} required />}
        {mode === "register" && (
          <select value={form.bloodGroup} onChange={set("bloodGroup")}>{GROUPS.map((g) => <option key={g}>{g}</option>)}</select>
        )}
        {mode === "register" && <input placeholder="City" value={form.city} onChange={set("city")} required />}
        <input placeholder="Phone (10+ digits)" value={form.phone} onChange={set("phone")} required />
        <input type="password" placeholder="Password (min 8 characters)" value={form.password} onChange={set("password")} required />
        {mode === "register" && (
          <>
            <label className="muted">Last donation date (optional)
              <input type="date" value={form.lastDonation} onChange={set("lastDonation")} />
            </label>
            <button type="button" className="ghost" onClick={locate}>
              {coords ? "✓ Location saved for distance matching" : "📍 Add my location (for nearby matching)"}
            </button>
          </>
        )}
        <button>{mode === "login" ? "Log in" : "Create account"}</button>
      </form>
      {msg && <p className={msg.ok ? "ok" : "error"}>{msg.text}</p>}
    </section>
  );
}
