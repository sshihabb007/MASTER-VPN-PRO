// Shihab Shield VPN — 113-Country Metadata, Regional Groups & Local UTC Clocks
// Engineered by Mehedi Hasan Shihab (sshihabb007)

const SHIHAB_COUNTRY_TABLE = {
  // Americas
  US: { name: "United States", city: "Ashburn / New York", region: "Americas", lat: 38.90, lon: -77.04, utc: -5.0 },
  CA: { name: "Canada", city: "Toronto", region: "Americas", lat: 43.65, lon: -79.38, utc: -5.0 },
  MX: { name: "Mexico", city: "Mexico City", region: "Americas", lat: 19.43, lon: -99.13, utc: -6.0 },
  BR: { name: "Brazil", city: "Sao Paulo", region: "Americas", lat: -23.55, lon: -46.63, utc: -3.0 },
  AR: { name: "Argentina", city: "Buenos Aires", region: "Americas", lat: -34.60, lon: -58.38, utc: -3.0 },
  CL: { name: "Chile", city: "Santiago", region: "Americas", lat: -33.45, lon: -70.66, utc: -3.0 },
  CO: { name: "Colombia", city: "Bogota", region: "Americas", lat: 4.71, lon: -74.07, utc: -5.0 },
  PE: { name: "Peru", city: "Lima", region: "Americas", lat: -12.05, lon: -77.04, utc: -5.0 },
  EC: { name: "Ecuador", city: "Quito", region: "Americas", lat: -0.18, lon: -78.47, utc: -5.0 },
  UY: { name: "Uruguay", city: "Montevideo", region: "Americas", lat: -34.90, lon: -56.16, utc: -3.0 },
  PA: { name: "Panama", city: "Panama City", region: "Americas", lat: 8.98, lon: -79.52, utc: -5.0 },
  CR: { name: "Costa Rica", city: "San Jose", region: "Americas", lat: 9.93, lon: -84.09, utc: -6.0 },
  PR: { name: "Puerto Rico", city: "San Juan", region: "Americas", lat: 18.47, lon: -66.11, utc: -4.0 },
  VE: { name: "Venezuela", city: "Caracas", region: "Americas", lat: 10.48, lon: -66.90, utc: -4.0 },

  // EMEA
  GB: { name: "United Kingdom", city: "London", region: "EMEA", lat: 51.51, lon: -0.13, utc: 0.0 },
  DE: { name: "Germany", city: "Frankfurt", region: "EMEA", lat: 50.11, lon: 8.68, utc: 1.0 },
  FR: { name: "France", city: "Paris", region: "EMEA", lat: 48.86, lon: 2.35, utc: 1.0 },
  NL: { name: "Netherlands", city: "Amsterdam", region: "EMEA", lat: 52.37, lon: 4.90, utc: 1.0 },
  CH: { name: "Switzerland", city: "Zurich", region: "EMEA", lat: 47.37, lon: 8.54, utc: 1.0 },
  SE: { name: "Sweden", city: "Stockholm", region: "EMEA", lat: 59.33, lon: 18.07, utc: 1.0 },
  NO: { name: "Norway", city: "Oslo", region: "EMEA", lat: 59.91, lon: 10.75, utc: 1.0 },
  DK: { name: "Denmark", city: "Copenhagen", region: "EMEA", lat: 55.68, lon: 12.57, utc: 1.0 },
  FI: { name: "Finland", city: "Helsinki", region: "EMEA", lat: 60.17, lon: 24.94, utc: 2.0 },
  IS: { name: "Iceland", city: "Reykjavik", region: "EMEA", lat: 64.15, lon: -21.94, utc: 0.0 },
  IE: { name: "Ireland", city: "Dublin", region: "EMEA", lat: 53.35, lon: -6.26, utc: 0.0 },
  ES: { name: "Spain", city: "Madrid", region: "EMEA", lat: 40.42, lon: -3.70, utc: 1.0 },
  PT: { name: "Portugal", city: "Lisbon", region: "EMEA", lat: 38.72, lon: -9.14, utc: 0.0 },
  IT: { name: "Italy", city: "Milan", region: "EMEA", lat: 45.46, lon: 9.19, utc: 1.0 },
  AT: { name: "Austria", city: "Vienna", region: "EMEA", lat: 48.21, lon: 16.37, utc: 1.0 },
  BE: { name: "Belgium", city: "Brussels", region: "EMEA", lat: 50.85, lon: 4.35, utc: 1.0 },
  LU: { name: "Luxembourg", city: "Luxembourg", region: "EMEA", lat: 49.61, lon: 6.13, utc: 1.0 },
  PL: { name: "Poland", city: "Warsaw", region: "EMEA", lat: 52.23, lon: 21.01, utc: 1.0 },
  CZ: { name: "Czech Republic", city: "Prague", region: "EMEA", lat: 50.08, lon: 14.44, utc: 1.0 },
  SK: { name: "Slovakia", city: "Bratislava", region: "EMEA", lat: 48.15, lon: 17.11, utc: 1.0 },
  HU: { name: "Hungary", city: "Budapest", region: "EMEA", lat: 47.50, lon: 19.04, utc: 1.0 },
  RO: { name: "Romania", city: "Bucharest", region: "EMEA", lat: 44.43, lon: 26.10, utc: 2.0 },
  BG: { name: "Bulgaria", city: "Sofia", region: "EMEA", lat: 42.70, lon: 23.32, utc: 2.0 },
  GR: { name: "Greece", city: "Athens", region: "EMEA", lat: 37.98, lon: 23.73, utc: 2.0 },
  UA: { name: "Ukraine", city: "Kyiv", region: "EMEA", lat: 50.45, lon: 30.52, utc: 2.0 },
  EE: { name: "Estonia", city: "Tallinn", region: "EMEA", lat: 59.44, lon: 24.75, utc: 2.0 },
  LV: { name: "Latvia", city: "Riga", region: "EMEA", lat: 56.95, lon: 24.11, utc: 2.0 },
  LT: { name: "Lithuania", city: "Vilnius", region: "EMEA", lat: 54.69, lon: 25.28, utc: 2.0 },
  RS: { name: "Serbia", city: "Belgrade", region: "EMEA", lat: 44.79, lon: 20.45, utc: 1.0 },
  HR: { name: "Croatia", city: "Zagreb", region: "EMEA", lat: 45.81, lon: 15.98, utc: 1.0 },
  SI: { name: "Slovenia", city: "Ljubljana", region: "EMEA", lat: 46.06, lon: 14.51, utc: 1.0 },
  TR: { name: "Turkey", city: "Istanbul", region: "EMEA", lat: 41.01, lon: 28.98, utc: 3.0 },
  RU: { name: "Russia", city: "Moscow", region: "EMEA", lat: 55.76, lon: 37.62, utc: 3.0 },
  IL: { name: "Israel", city: "Tel Aviv", region: "EMEA", lat: 32.09, lon: 34.78, utc: 2.0 },
  AE: { name: "United Arab Emirates", city: "Dubai", region: "EMEA", lat: 25.20, lon: 55.27, utc: 4.0 },
  SA: { name: "Saudi Arabia", city: "Riyadh", region: "EMEA", lat: 24.71, lon: 46.68, utc: 3.0 },
  QA: { name: "Qatar", city: "Doha", region: "EMEA", lat: 25.29, lon: 51.53, utc: 3.0 },
  ZA: { name: "South Africa", city: "Johannesburg", region: "EMEA", lat: -26.20, lon: 28.05, utc: 2.0 },
  EG: { name: "Egypt", city: "Cairo", region: "EMEA", lat: 30.04, lon: 31.24, utc: 2.0 },
  MA: { name: "Morocco", city: "Casablanca", region: "EMEA", lat: 33.57, lon: -7.59, utc: 1.0 },
  NG: { name: "Nigeria", city: "Lagos", region: "EMEA", lat: 6.52, lon: 3.38, utc: 1.0 },
  KE: { name: "Kenya", city: "Nairobi", region: "EMEA", lat: -1.29, lon: 36.82, utc: 3.0 },

  // Asia & Oceania
  JP: { name: "Japan", city: "Tokyo", region: "Asia & Oceania", lat: 35.68, lon: 139.69, utc: 9.0 },
  KR: { name: "South Korea", city: "Seoul", region: "Asia & Oceania", lat: 37.57, lon: 126.98, utc: 9.0 },
  SG: { name: "Singapore", city: "Singapore", region: "Asia & Oceania", lat: 1.35, lon: 103.82, utc: 8.0 },
  HK: { name: "Hong Kong", city: "Hong Kong", region: "Asia & Oceania", lat: 22.32, lon: 114.17, utc: 8.0 },
  TW: { name: "Taiwan", city: "Taipei", region: "Asia & Oceania", lat: 25.03, lon: 121.57, utc: 8.0 },
  IN: { name: "India", city: "Mumbai", region: "Asia & Oceania", lat: 19.08, lon: 72.88, utc: 5.5 },
  BD: { name: "Bangladesh", city: "Dhaka", region: "Asia & Oceania", lat: 23.81, lon: 90.41, utc: 6.0 },
  PK: { name: "Pakistan", city: "Karachi", region: "Asia & Oceania", lat: 24.86, lon: 67.00, utc: 5.0 },
  ID: { name: "Indonesia", city: "Jakarta", region: "Asia & Oceania", lat: -6.21, lon: 106.85, utc: 7.0 },
  MY: { name: "Malaysia", city: "Kuala Lumpur", region: "Asia & Oceania", lat: 3.14, lon: 101.69, utc: 8.0 },
  TH: { name: "Thailand", city: "Bangkok", region: "Asia & Oceania", lat: 13.76, lon: 100.50, utc: 7.0 },
  VN: { name: "Vietnam", city: "Ho Chi Minh City", region: "Asia & Oceania", lat: 10.82, lon: 106.63, utc: 7.0 },
  PH: { name: "Philippines", city: "Manila", region: "Asia & Oceania", lat: 14.60, lon: 120.98, utc: 8.0 },
  KH: { name: "Cambodia", city: "Phnom Penh", region: "Asia & Oceania", lat: 11.56, lon: 104.93, utc: 7.0 },
  KZ: { name: "Kazakhstan", city: "Almaty", region: "Asia & Oceania", lat: 43.22, lon: 76.85, utc: 5.0 },
  AU: { name: "Australia", city: "Sydney", region: "Asia & Oceania", lat: -33.87, lon: 151.21, utc: 10.0 },
  NZ: { name: "New Zealand", city: "Auckland", region: "Asia & Oceania", lat: -36.85, lon: 174.76, utc: 12.0 }
};

function resolveShihabCountryMeta(code) {
  let cc = String(code || "").trim().toUpperCase();
  if (cc === "UK") cc = "GB";
  if (SHIHAB_COUNTRY_TABLE[cc]) {
    return Object.assign({ code: cc }, SHIHAB_COUNTRY_TABLE[cc]);
  }
  return {
    code: cc || "GL",
    name: cc && cc.length === 2 ? ("Country " + cc) : "Global Relay",
    city: "Gateway Hub",
    region: "EMEA",
    lat: 38.90,
    lon: -77.04,
    utc: 0.0
  };
}

function formatShihabCountryClock(code) {
  const meta = resolveShihabCountryMeta(code);
  const nowUtcMs = Date.now();
  const localMs = nowUtcMs + Math.round(meta.utc * 3600 * 1000);
  const d = new Date(localMs);
  const hh = String(d.getUTCHours()).padStart(2, "0");
  const mm = String(d.getUTCMinutes()).padStart(2, "0");
  const sign = meta.utc >= 0 ? "+" : "-";
  const absH = Math.floor(Math.abs(meta.utc));
  const absM = Math.round((Math.abs(meta.utc) - absH) * 60);
  const offTxt = "UTC" + sign + String(absH).padStart(2, "0") + ":" + String(absM).padStart(2, "0");
  return hh + ":" + mm + " (" + offTxt + ")";
}

if (typeof self !== "undefined") {
  self.SHIHAB_COUNTRY_TABLE = SHIHAB_COUNTRY_TABLE;
  self.resolveShihabCountryMeta = resolveShihabCountryMeta;
  self.formatShihabCountryClock = formatShihabCountryClock;
}

