import { useEffect, useState } from "react";
import { getStats } from "./api";

export default function Stats() {
  const [s, setS] = useState(null);
  const [error, setError] = useState("");
  useEffect(() => { getStats().then(setS).catch((e) => setError(e.message)); }, []);

  if (error) return <p className="error">{error}</p>;
  if (!s) return <p className="muted">Loading…</p>;

  const max = Math.max(1, ...s.byBloodGroup.map((g) => g.donors));
  const shortages = s.byBloodGroup.filter((g) => g.shortage);

  return (
    <section>
      <div className="tiles">
        <div className="tile"><b>{s.totalDonors}</b><span>Donors</span></div>
        <div className="tile"><b>{s.eligibleNow}</b><span>Eligible now</span></div>
        <div className="tile"><b>{s.totalDonations}</b><span>Donations</span></div>
        <div className="tile"><b>{s.openRequests}</b><span>Open requests</span></div>
      </div>

      {shortages.map((g) => (
        <p className="alert" key={g.bloodGroup}>
          ⚠ Shortage: {g.bloodGroup} needs {g.openDemandUnits} unit(s), only {g.compatibleSupply} compatible donor(s) available.
        </p>
      ))}

      <h3>Donors by blood group</h3>
      {s.byBloodGroup.map((g) => (
        <div className="bar-row" key={g.bloodGroup}>
          <span className="bar-label">{g.bloodGroup}</span>
          <div className="bar"><div className="bar-fill" style={{ width: `${(g.donors / max) * 100}%` }} /></div>
          <span className="muted">{g.eligible}/{g.donors} eligible</span>
        </div>
      ))}
    </section>
  );
}
