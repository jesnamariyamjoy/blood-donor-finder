export const GROUPS = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
export const URGENCIES = ["Critical", "Urgent", "Normal"];
export const getToken = () => localStorage.getItem("token");
export const setToken = (t) => (t ? localStorage.setItem("token", t) : localStorage.removeItem("token"));
export const isMasked = (phone) => !phone || phone.includes("*");
