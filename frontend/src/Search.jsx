import { useState } from "react";
import { searchDonors, getLocation } from "./api";
import { GROUPS, isMasked } from "./shared";

export default function Search() {
  const [bloodGroup, setBloodGroup] = useState("O+");
  const [city, setCity] = useState("");
  const [coords, setCoords] = useState(null);
  const [radius, setRadius] = useState(25);
  const [result, setResult] = useState(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function locate() {
    try { setCoords(await getLocation()); setError(""); }
    catch (e) { setError(e.message); }
  }

  async function onSubmit(e) {
    e.preventDefault();
    setLoading(true); setError(""); setResult(null);
    try {
      const params = coords
        ? { bloodGroup, lat: coords.lat, lng: coords.lng, radiusKm: radius }
        : { bloodGroup, city: city.trim() };
      setResult(await searchDonors(params));
    } catch (err) { setError(err.message); }
    finally { setLoading(false); }
  }

  return (
    <section>
      <form onSubmit={onSubmit} className="col">
        <div className="row">
          <select value={bloodGroup} onChange={(e) => setBloodGroup(e.target.value)}>
            {GROUPS.map((g) => <option key={g}>{g}</option>)}
          </select>
          {coords ? (
            <select value={radius} onChange={(e) => setRadius(Number(e.target.value))}>
              {[5, 10, 25, 50, 100].map((r) => <option key={r} value={r}>Within {r} km</option>)}
            </select>
          ) : (
            <input placeholder="City" value={city} onChange={(e) => setCity(e.target.value)} required />
          )}
        </div>
        <div className="row">
          <button type="button" className="ghost" onClick={coords ? () => setCoords(null) : locate}>
            {coords ? "✓ Using my location (tap to use city)" : "📍 Use my location"}
          </button>
          <button disabled={loading}>{loading ? "Searching…" : "Find donors"}</button>
        </div>
      </form>

      {error && <p className="error">{error}</p>}
      {result && (
        <>
          <p className="muted">
            {result.count} eligible donor(s) for {result.needed} — nearest and exact matches first
          </p>
          {result.matches.map(({ donor, distanceKm, exactMatch }) => (
            <div className="card" key={donor.id}>
              <div>
                <strong>{donor.name}</strong>
                <span className={"badge" + (exactMatch ? " exact" : "")}>{donor.bloodGroup}</span>
                <span className={"badge lvl-" + donor.level}>{donor.level}</span>
                <div className="muted">
                  {donor.city}{distanceKm != null && ` · ${distanceKm} km away`} · {donor.donationCount} donation(s)
                </div>
              </div>
              {isMasked(donor.phone)
                ? <span className="muted">Log in to call</span>
                : <a className="call" href={`tel:${donor.phone}`}>Call</a>}
            </div>
          ))}
        </>
      )}
    </section>
  );
}
