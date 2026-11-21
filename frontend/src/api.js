import { getToken, setToken } from "./shared";

const BASE = import.meta.env.VITE_API_URL ?? "http://localhost:5000";

async function request(path, options = {}) {
  const token = getToken();
  const res = await fetch(BASE + path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
  if (!res.ok) {
    if (res.status === 429) throw new Error("Too many attempts. Please wait a minute.");
    if (res.status === 401 && token) {
      setToken(null);
      throw new Error("Session expired. Please log in again.");
    }
    const body = await res.json().catch(() => null);
    const msg =
      typeof body === "string" ? body
      : body?.errors ? Object.values(body.errors).flat().join(" ")
      : res.statusText;
    throw new Error(msg);
  }
  return res.status === 204 ? null : res.json();
}

const qs = (o) =>
  new URLSearchParams(
    Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== "")
  ).toString();
const send = (method, body) => ({ method, body: body ? JSON.stringify(body) : undefined });

// auth + profile
export const register = (d) => request("/auth/register", send("POST", d));
export const login = (d) => request("/auth/login", send("POST", d));
export const getMe = () => request("/me");
export const recordDonation = () => request("/me/donation", send("PATCH"));
export const setAvailability = (a) => request(`/me/availability?available=${a}`, send("PATCH"));
export const setSms = (e) => request(`/me/sms?enabled=${e}`, send("PATCH"));
export const deleteMe = () => request("/me", send("DELETE"));

// search, requests, stats
export const searchDonors = (p) => request(`/search?${qs(p)}`);
export const getRequests = () => request("/requests");
export const createRequest = (r) => request("/requests", send("POST", r));
export const pledge = (id) => request(`/requests/${id}/pledge`, send("POST"));
export const fulfill = (id) => request(`/requests/${id}/fulfill`, send("PATCH"));
export const getStats = () => request("/stats");

export function getLocation() {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) return reject(new Error("Location is not supported in this browser."));
    navigator.geolocation.getCurrentPosition(
      (p) => resolve({ lat: p.coords.latitude, lng: p.coords.longitude }),
      () => reject(new Error("Location permission denied.")),
      { timeout: 8000 }
    );
  });
}
