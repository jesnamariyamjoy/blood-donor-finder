import { useEffect, useState } from "react";
import { getRequests, createRequest, pledge, fulfill } from "./api";
import { GROUPS, URGENCIES, getToken, isMasked } from "./shared";

const empty = { patient: "", bloodGroup: "O+", units: 1, hospital: "", city: "", contact: "", urgency: "Urgent" };

function whatsappLink(r) {
  const text = `🩸 ${r.urgency.toUpperCase()}: ${r.units} unit(s) of ${r.bloodGroup} blood needed at ${r.hospital}, ${r.city} for ${r.patient}. Contact: ${r.contact}`;
  return `https://wa.me/?text=${encodeURIComponent(text)}`;
}

export default function Requests({ goToProfile }) {
  const [list, setList] = useState([]);
  const [form, setForm] = useState(empty);
  const [showForm, setShowForm] = useState(false);
  const [msg, setMsg] = useState(null);
  const loggedIn = !!getToken();
  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value });

  const load = () => getRequests().then(setList).catch((e) => setMsg({ ok: false, text: e.message }));
  useEffect(() => { load(); }, []);

  async function onCreate(e) {
    e.preventDefault(); setMsg(null);
    try {
      await createRequest({ ...form, units: Number(form.units) });
      setForm(empty); setShowForm(false);
      setMsg({ ok: true, text: "Request posted. Nearby eligible donors are being alerted by SMS." });
      load();
    } catch (err) { setMsg({ ok: false, text: err.message }); }
  }

  async function onPledge(id) {
    if (!loggedIn) return goToProfile();
    setMsg(null);
    try {
      await pledge(id);
      setMsg({ ok: true, text: "Thank you! Your pledge is recorded and the family has been notified." });
      load();
    } catch (err) { setMsg({ ok: false, text: err.message }); }
  }

  async function onFulfill(id) {
    try { await fulfill(id); load(); } catch (err) { setMsg({ ok: false, text: err.message }); }
  }

  return (
    <section>
      <button className="ghost full" onClick={() => (loggedIn ? setShowForm(!showForm) : goToProfile())}>
        {!loggedIn ? "Log in to post a request" : showForm ? "Cancel" : "+ Post an emergency request"}
      </button>

      {showForm && (
        <form onSubmit={onCreate} className="col box">
          <input placeholder="Patient name" value={form.patient} onChange={set("patient")} required />
          <div className="row">
            <select value={form.bloodGroup} onChange={set("bloodGroup")}>{GROUPS.map((g) => <option key={g}>{g}</option>)}</select>
            <input type="number" min="1" max="10" value={form.units} onChange={set("units")} title="Units" />
            <select value={form.urgency} onChange={set("urgency")}>{URGENCIES.map((u) => <option key={u}>{u}</option>)}</select>
          </div>
          <input placeholder="Hospital" value={form.hospital} onChange={set("hospital")} required />
          <input placeholder="City" value={form.city} onChange={set("city")} required />
          <input placeholder="Contact number" value={form.contact} onChange={set("contact")} required />
          <button>Post request</button>
        </form>
      )}

      {msg && <p className={msg.ok ? "ok" : "error"}>{msg.text}</p>}
      {list.length === 0 && <p className="muted">No open requests right now.</p>}

      {list.map((r) => (
        <div className={"card col urg-" + r.urgency} key={r.id}>
          <div>
            <strong>{r.units} unit(s) of {r.bloodGroup}</strong>
            <span className={"badge urg-badge-" + r.urgency}>{r.urgency}</span>
            <div className="muted">{r.patient} · {r.hospital}, {r.city}</div>
            <div className="muted">
              {r.pledges.length > 0 ? `Pledged: ${r.pledges.join(", ")}` : "No pledges yet"}
            </div>
          </div>
          <div className="row">
            {!r.mine && <button onClick={() => onPledge(r.id)}>{loggedIn ? "I can donate" : "Log in to help"}</button>}
            {!isMasked(r.contact) && <a className="call" href={`tel:${r.contact}`}>Call</a>}
            {loggedIn && <a className="call wa" href={whatsappLink(r)} target="_blank" rel="noreferrer">Share</a>}
            {r.mine && <button className="ghost" onClick={() => onFulfill(r.id)}>Mark fulfilled</button>}
          </div>
        </div>
      ))}
    </section>
  );
}
