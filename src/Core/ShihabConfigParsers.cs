using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ShihabShieldVpn.Core
{
    public static class ShihabConfigParsers
    {
        private sealed class ShihabCountryMeta
        {
            public string Code;
            public string Name;
            public string City;
            public string Region;
            public double Lat;
            public double Lon;
            public double UtcOffset;
        }

        private static readonly Dictionary<string, ShihabCountryMeta> ShihabCountryTable = BuildShihabCountryMetaTable();

        private static void AddMeta(
            Dictionary<string, ShihabCountryMeta> map,
            string code,
            string name,
            string city,
            string region,
            double lat,
            double lon,
            double utcOffset)
        {
            ShihabCountryMeta m = new ShihabCountryMeta();
            m.Code = code;
            m.Name = name;
            m.City = city;
            m.Region = region;
            m.Lat = lat;
            m.Lon = lon;
            m.UtcOffset = utcOffset;
            map[code] = m;
        }

        private static Dictionary<string, ShihabCountryMeta> BuildShihabCountryMetaTable()
        {
            Dictionary<string, ShihabCountryMeta> m = new Dictionary<string, ShihabCountryMeta>(StringComparer.OrdinalIgnoreCase);

            // Americas (28)
            AddMeta(m, "US", "United States", "Ashburn / New York", "Americas", 38.90, -97.00, -5.0);
            AddMeta(m, "CA", "Canada", "Toronto", "Americas", 56.13, -106.35, -5.0);
            AddMeta(m, "MX", "Mexico", "Mexico City", "Americas", 23.63, -102.55, -6.0);
            AddMeta(m, "BR", "Brazil", "Sao Paulo", "Americas", -14.24, -51.93, -3.0);
            AddMeta(m, "AR", "Argentina", "Buenos Aires", "Americas", -34.60, -58.38, -3.0);
            AddMeta(m, "CL", "Chile", "Santiago", "Americas", -33.45, -70.66, -3.0);
            AddMeta(m, "CO", "Colombia", "Bogota", "Americas", 4.71, -74.07, -5.0);
            AddMeta(m, "PE", "Peru", "Lima", "Americas", -12.05, -77.04, -5.0);
            AddMeta(m, "EC", "Ecuador", "Quito", "Americas", -0.18, -78.47, -5.0);
            AddMeta(m, "VE", "Venezuela", "Caracas", "Americas", 10.48, -66.90, -4.0);
            AddMeta(m, "UY", "Uruguay", "Montevideo", "Americas", -34.90, -56.16, -3.0);
            AddMeta(m, "PY", "Paraguay", "Asuncion", "Americas", -25.26, -57.58, -3.0);
            AddMeta(m, "BO", "Bolivia", "La Paz", "Americas", -16.50, -68.15, -4.0);
            AddMeta(m, "PA", "Panama", "Panama City", "Americas", 8.98, -79.52, -5.0);
            AddMeta(m, "CR", "Costa Rica", "San Jose", "Americas", 9.93, -84.09, -6.0);
            AddMeta(m, "GT", "Guatemala", "Guatemala City", "Americas", 14.63, -90.51, -6.0);
            AddMeta(m, "HN", "Honduras", "Tegucigalpa", "Americas", 14.07, -87.21, -6.0);
            AddMeta(m, "SV", "El Salvador", "San Salvador", "Americas", 13.69, -89.22, -6.0);
            AddMeta(m, "NI", "Nicaragua", "Managua", "Americas", 12.11, -86.24, -6.0);
            AddMeta(m, "DO", "Dominican Republic", "Santo Domingo", "Americas", 18.49, -69.93, -4.0);
            AddMeta(m, "PR", "Puerto Rico", "San Juan", "Americas", 18.47, -66.11, -4.0);
            AddMeta(m, "JM", "Jamaica", "Kingston", "Americas", 18.00, -76.79, -5.0);
            AddMeta(m, "BS", "Bahamas", "Nassau", "Americas", 25.05, -77.35, -5.0);
            AddMeta(m, "BB", "Barbados", "Bridgetown", "Americas", 13.10, -59.62, -4.0);
            AddMeta(m, "TT", "Trinidad and Tobago", "Port of Spain", "Americas", 10.65, -61.51, -4.0);
            AddMeta(m, "GD", "Grenada", "St. George's", "Americas", 12.05, -61.75, -4.0);
            AddMeta(m, "LC", "Saint Lucia", "Castries", "Americas", 13.91, -60.98, -4.0);
            AddMeta(m, "BQ", "Bonaire", "Kralendijk", "Americas", 12.17, -68.28, -4.0);

            // EMEA — Europe, Middle East & Africa (57)
            AddMeta(m, "GB", "United Kingdom", "London", "EMEA", 53.50, -2.20, 0.0);
            AddMeta(m, "UK", "United Kingdom", "London", "EMEA", 53.50, -2.20, 0.0);
            AddMeta(m, "IE", "Ireland", "Dublin", "EMEA", 53.35, -6.26, 0.0);
            AddMeta(m, "DE", "Germany", "Frankfurt", "EMEA", 51.16, 10.45, 1.0);
            AddMeta(m, "FR", "France", "Paris", "EMEA", 46.60, 2.21, 1.0);
            AddMeta(m, "NL", "Netherlands", "Amsterdam", "EMEA", 52.37, 4.90, 1.0);
            AddMeta(m, "BE", "Belgium", "Brussels", "EMEA", 50.85, 4.35, 1.0);
            AddMeta(m, "LU", "Luxembourg", "Luxembourg City", "EMEA", 49.61, 6.13, 1.0);
            AddMeta(m, "AT", "Austria", "Vienna", "EMEA", 47.52, 14.55, 1.0);
            AddMeta(m, "CH", "Switzerland", "Zurich", "EMEA", 46.82, 8.23, 1.0);
            AddMeta(m, "SE", "Sweden", "Stockholm", "EMEA", 60.13, 18.64, 1.0);
            AddMeta(m, "NO", "Norway", "Oslo", "EMEA", 60.47, 8.47, 1.0);
            AddMeta(m, "FI", "Finland", "Helsinki", "EMEA", 61.92, 25.75, 2.0);
            AddMeta(m, "DK", "Denmark", "Copenhagen", "EMEA", 56.26, 9.50, 1.0);
            AddMeta(m, "IS", "Iceland", "Reykjavik", "EMEA", 64.15, -21.94, 0.0);
            AddMeta(m, "ES", "Spain", "Madrid", "EMEA", 40.46, -3.75, 1.0);
            AddMeta(m, "PT", "Portugal", "Lisbon", "EMEA", 39.40, -8.22, 0.0);
            AddMeta(m, "IT", "Italy", "Milan", "EMEA", 41.87, 12.57, 1.0);
            AddMeta(m, "GR", "Greece", "Athens", "EMEA", 39.07, 21.82, 2.0);
            AddMeta(m, "MT", "Malta", "Valletta", "EMEA", 35.90, 14.51, 1.0);
            AddMeta(m, "CY", "Cyprus", "Nicosia", "EMEA", 35.19, 33.38, 2.0);
            AddMeta(m, "PL", "Poland", "Warsaw", "EMEA", 51.92, 19.15, 1.0);
            AddMeta(m, "CZ", "Czech Republic", "Prague", "EMEA", 49.82, 15.47, 1.0);
            AddMeta(m, "SK", "Slovakia", "Bratislava", "EMEA", 48.15, 17.11, 1.0);
            AddMeta(m, "HU", "Hungary", "Budapest", "EMEA", 47.16, 19.50, 1.0);
            AddMeta(m, "RO", "Romania", "Bucharest", "EMEA", 45.94, 24.97, 2.0);
            AddMeta(m, "BG", "Bulgaria", "Sofia", "EMEA", 42.73, 25.49, 2.0);
            AddMeta(m, "HR", "Croatia", "Zagreb", "EMEA", 45.10, 15.20, 1.0);
            AddMeta(m, "SI", "Slovenia", "Ljubljana", "EMEA", 46.15, 14.99, 1.0);
            AddMeta(m, "RS", "Serbia", "Belgrade", "EMEA", 44.79, 20.45, 1.0);
            AddMeta(m, "BA", "Bosnia and Herzegovina", "Sarajevo", "EMEA", 43.86, 18.41, 1.0);
            AddMeta(m, "AL", "Albania", "Tirana", "EMEA", 41.33, 19.82, 1.0);
            AddMeta(m, "MK", "North Macedonia", "Skopje", "EMEA", 42.00, 21.43, 1.0);
            AddMeta(m, "ME", "Montenegro", "Podgorica", "EMEA", 42.44, 19.26, 1.0);
            AddMeta(m, "EE", "Estonia", "Tallinn", "EMEA", 58.60, 25.01, 2.0);
            AddMeta(m, "LV", "Latvia", "Riga", "EMEA", 56.88, 24.60, 2.0);
            AddMeta(m, "LT", "Lithuania", "Vilnius", "EMEA", 55.17, 23.88, 2.0);
            AddMeta(m, "BY", "Belarus", "Minsk", "EMEA", 53.71, 27.95, 3.0);
            AddMeta(m, "MD", "Moldova", "Chisinau", "EMEA", 47.41, 28.37, 2.0);
            AddMeta(m, "UA", "Ukraine", "Kyiv", "EMEA", 48.38, 31.17, 2.0);
            AddMeta(m, "RU", "Russia", "Moscow", "EMEA", 55.75, 37.62, 3.0);
            AddMeta(m, "TR", "Turkey", "Istanbul", "EMEA", 38.96, 35.24, 3.0);
            AddMeta(m, "GE", "Georgia", "Tbilisi", "EMEA", 42.32, 43.36, 4.0);
            AddMeta(m, "AM", "Armenia", "Yerevan", "EMEA", 40.07, 45.04, 4.0);
            AddMeta(m, "AZ", "Azerbaijan", "Baku", "EMEA", 40.41, 49.87, 4.0);
            AddMeta(m, "AE", "United Arab Emirates", "Dubai", "EMEA", 25.20, 55.27, 4.0);
            AddMeta(m, "SA", "Saudi Arabia", "Riyadh", "EMEA", 23.89, 45.08, 3.0);
            AddMeta(m, "QA", "Qatar", "Doha", "EMEA", 25.29, 51.53, 3.0);
            AddMeta(m, "KW", "Kuwait", "Kuwait City", "EMEA", 29.38, 47.98, 3.0);
            AddMeta(m, "BH", "Bahrain", "Manama", "EMEA", 26.23, 50.59, 3.0);
            AddMeta(m, "OM", "Oman", "Muscat", "EMEA", 23.59, 58.38, 4.0);
            AddMeta(m, "IL", "Israel", "Tel Aviv", "EMEA", 32.09, 34.78, 2.0);
            AddMeta(m, "JO", "Jordan", "Amman", "EMEA", 30.59, 36.24, 3.0);
            AddMeta(m, "LB", "Lebanon", "Beirut", "EMEA", 33.89, 35.50, 2.0);
            AddMeta(m, "IQ", "Iraq", "Baghdad", "EMEA", 33.31, 44.37, 3.0);
            AddMeta(m, "IR", "Iran", "Tehran", "EMEA", 32.43, 53.69, 3.5);
            AddMeta(m, "EG", "Egypt", "Cairo", "EMEA", 26.82, 30.80, 2.0);
            AddMeta(m, "MA", "Morocco", "Casablanca", "EMEA", 33.57, -7.59, 1.0);
            AddMeta(m, "TN", "Tunisia", "Tunis", "EMEA", 36.81, 10.18, 1.0);
            AddMeta(m, "DZ", "Algeria", "Algiers", "EMEA", 36.75, 3.06, 1.0);
            AddMeta(m, "NG", "Nigeria", "Lagos", "EMEA", 6.52, 3.38, 1.0);
            AddMeta(m, "KE", "Kenya", "Nairobi", "EMEA", -1.29, 36.82, 3.0);
            AddMeta(m, "ZA", "South Africa", "Johannesburg", "EMEA", -30.56, 22.94, 2.0);

            // Asia & Oceania (28)
            AddMeta(m, "KZ", "Kazakhstan", "Almaty", "Asia & Oceania", 43.24, 76.95, 5.0);
            AddMeta(m, "UZ", "Uzbekistan", "Tashkent", "Asia & Oceania", 41.30, 69.24, 5.0);
            AddMeta(m, "IN", "India", "Mumbai", "Asia & Oceania", 20.59, 78.96, 5.5);
            AddMeta(m, "PK", "Pakistan", "Karachi", "Asia & Oceania", 24.86, 67.00, 5.0);
            AddMeta(m, "BD", "Bangladesh", "Dhaka", "Asia & Oceania", 23.68, 90.35, 6.0);
            AddMeta(m, "LK", "Sri Lanka", "Colombo", "Asia & Oceania", 6.93, 79.86, 5.5);
            AddMeta(m, "NP", "Nepal", "Kathmandu", "Asia & Oceania", 27.72, 85.32, 5.75);
            AddMeta(m, "MV", "Maldives", "Male", "Asia & Oceania", 3.20, 73.22, 5.0);
            AddMeta(m, "SG", "Singapore", "Singapore", "Asia & Oceania", 1.35, 103.82, 8.0);
            AddMeta(m, "TH", "Thailand", "Bangkok", "Asia & Oceania", 15.87, 100.99, 7.0);
            AddMeta(m, "VN", "Vietnam", "Ho Chi Minh City", "Asia & Oceania", 14.06, 108.28, 7.0);
            AddMeta(m, "KH", "Cambodia", "Phnom Penh", "Asia & Oceania", 12.57, 104.99, 7.0);
            AddMeta(m, "LA", "Laos", "Vientiane", "Asia & Oceania", 19.86, 102.50, 7.0);
            AddMeta(m, "MM", "Myanmar", "Yangon", "Asia & Oceania", 21.91, 95.96, 6.5);
            AddMeta(m, "ID", "Indonesia", "Jakarta", "Asia & Oceania", -0.79, 113.92, 7.0);
            AddMeta(m, "MY", "Malaysia", "Kuala Lumpur", "Asia & Oceania", 4.21, 101.98, 8.0);
            AddMeta(m, "PH", "Philippines", "Manila", "Asia & Oceania", 12.88, 121.77, 8.0);
            AddMeta(m, "BN", "Brunei", "Bandar Seri Begawan", "Asia & Oceania", 4.90, 114.94, 8.0);
            AddMeta(m, "HK", "Hong Kong", "Hong Kong", "Asia & Oceania", 22.32, 114.17, 8.0);
            AddMeta(m, "MO", "Macau", "Macau", "Asia & Oceania", 22.20, 113.54, 8.0);
            AddMeta(m, "TW", "Taiwan", "Taipei", "Asia & Oceania", 23.70, 120.96, 8.0);
            AddMeta(m, "CN", "China", "Shanghai", "Asia & Oceania", 35.86, 104.20, 8.0);
            AddMeta(m, "MN", "Mongolia", "Ulaanbaatar", "Asia & Oceania", 47.92, 106.92, 8.0);
            AddMeta(m, "KR", "South Korea", "Seoul", "Asia & Oceania", 36.50, 127.80, 9.0);
            AddMeta(m, "JP", "Japan", "Tokyo", "Asia & Oceania", 36.20, 138.25, 9.0);
            AddMeta(m, "AU", "Australia", "Sydney", "Asia & Oceania", -25.27, 133.78, 10.0);
            AddMeta(m, "NZ", "New Zealand", "Auckland", "Asia & Oceania", -40.90, 174.89, 12.0);
            AddMeta(m, "VU", "Vanuatu", "Port Vila", "Asia & Oceania", -15.38, 166.96, 11.0);

            return m;
        }

        public static void ResolveShihabCountryCoordinates(string countryShort, out double lat, out double lon)
        {
            ShihabCountryMeta meta;
            if (!string.IsNullOrWhiteSpace(countryShort) && ShihabCountryTable.TryGetValue(countryShort.Trim(), out meta))
            {
                lat = meta.Lat;
                lon = meta.Lon;
                return;
            }
            lat = 38.90;
            lon = -97.00;
        }

        public static string ResolveShihabCountryCity(string countryShort)
        {
            ShihabCountryMeta meta;
            if (!string.IsNullOrWhiteSpace(countryShort) && ShihabCountryTable.TryGetValue(countryShort.Trim(), out meta))
            {
                return meta.City;
            }
            return "Global Gateway";
        }

        public static string ResolveShihabCountryRegion(string countryShort)
        {
            ShihabCountryMeta meta;
            if (!string.IsNullOrWhiteSpace(countryShort) && ShihabCountryTable.TryGetValue(countryShort.Trim(), out meta))
            {
                return meta.Region;
            }
            return "Global";
        }

        public static double ResolveShihabCountryUtcOffsetHours(string countryShort)
        {
            ShihabCountryMeta meta;
            if (!string.IsNullOrWhiteSpace(countryShort) && ShihabCountryTable.TryGetValue(countryShort.Trim(), out meta))
            {
                return meta.UtcOffset;
            }
            return 0.0;
        }

        public static string FormatShihabCountryLocalClock(string countryShort, DateTime utcNow)
        {
            double offsetHours = ResolveShihabCountryUtcOffsetHours(countryShort);
            DateTime local = utcNow.AddMinutes(offsetHours * 60.0);
            int totalMins = (int)Math.Round(Math.Abs(offsetHours) * 60.0);
            int oh = totalMins / 60;
            int om = totalMins % 60;
            string sign = offsetHours >= 0.0 ? "+" : "-";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm} (UTC{1}{2:00}:{3:00})",
                local,
                sign,
                oh,
                om);
        }

        public static string NormalizeShihabCountryTitle(string rawTitle, string countryShort)
        {
            string cc = (countryShort ?? string.Empty).Trim().ToUpperInvariant();
            if (cc == "UK") cc = "GB";

            ShihabCountryMeta meta;
            if (!string.IsNullOrEmpty(cc) && ShihabCountryTable.TryGetValue(cc, out meta))
            {
                return meta.Name;
            }

            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return "Global Relay";
            }

            string t = rawTitle.Trim();
            int paren = t.IndexOf('(');
            if (paren > 1)
            {
                t = t.Substring(0, paren).Trim();
            }

            if (string.Equals(t, "Korea Republic of", StringComparison.OrdinalIgnoreCase)) return "South Korea";
            if (string.Equals(t, "Russian Federation", StringComparison.OrdinalIgnoreCase)) return "Russia";
            if (string.Equals(t, "Viet Nam", StringComparison.OrdinalIgnoreCase)) return "Vietnam";
            if (string.Equals(t, "Lao People's Democratic Republic", StringComparison.OrdinalIgnoreCase)) return "Laos";
            if (string.Equals(t, "Moldova Republic of", StringComparison.OrdinalIgnoreCase)) return "Moldova";
            if (string.Equals(t, "Iran", StringComparison.OrdinalIgnoreCase)) return "Iran";
            if (string.Equals(t, "Czechia", StringComparison.OrdinalIgnoreCase)) return "Czech Republic";
            if (string.Equals(t, "Turkiye", StringComparison.OrdinalIgnoreCase)) return "Turkey";
            return t;
        }

        public static string LookupShihabCountryCodeByName(string countryName)
        {
            if (string.IsNullOrWhiteSpace(countryName))
            {
                return "GL";
            }
            string clean = NormalizeShihabCountryTitle(countryName, null);
            foreach (KeyValuePair<string, ShihabCountryMeta> kv in ShihabCountryTable)
            {
                if (kv.Key == "UK") continue;
                if (string.Equals(kv.Value.Name, clean, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv.Value.Name, countryName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    countryName.IndexOf(kv.Value.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kv.Key;
                }
            }
            return "GL";
        }

        public static bool TryDecodeShihabOpenVpnConfig(ShihabVpnServerNode node, out string ovpnText)
        {
            ovpnText = string.Empty;
            if (node == null)
            {
                return false;
            }

            if (node.Protocol == ShihabVpnProtocol.OpenVpn && !string.IsNullOrWhiteSpace(node.RawConfigProfile))
            {
                ovpnText = node.RawConfigProfile;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(node.OpenVpnConfigBase64))
            {
                try
                {
                    byte[] raw = Convert.FromBase64String(node.OpenVpnConfigBase64.Trim());
                    string decoded = Encoding.UTF8.GetString(raw);
                    if (!string.IsNullOrWhiteSpace(decoded) &&
                        decoded.IndexOf("remote ", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ovpnText = decoded;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        public static void TryExtractOpenVpnTransportFromBase64(
            string ovpnBase64,
            out bool isTcpTransport,
            out int remotePort)
        {
            isTcpTransport = false;
            remotePort = 0;
            if (string.IsNullOrWhiteSpace(ovpnBase64))
            {
                return;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(ovpnBase64.Trim());
                string text = Encoding.UTF8.GetString(bytes);
                using (StringReader sr = new StringReader(text))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string c = line.Trim();
                        if (string.IsNullOrEmpty(c) || c.StartsWith("#") || c.StartsWith(";"))
                        {
                            continue;
                        }
                        if (c.StartsWith("proto ", StringComparison.OrdinalIgnoreCase) ||
                            c.StartsWith("proto\t", StringComparison.OrdinalIgnoreCase))
                        {
                            isTcpTransport = c.IndexOf("tcp", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        else if (c.StartsWith("remote ", StringComparison.OrdinalIgnoreCase) ||
                                 c.StartsWith("remote\t", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = c.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                int p;
                                if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p > 0 && p <= 65535)
                                {
                                    remotePort = p;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static List<ShihabVpnServerNode> ParseShihabVpnGateCsv(string rawCsv)
        {
            List<ShihabVpnServerNode> results = new List<ShihabVpnServerNode>();
            if (string.IsNullOrWhiteSpace(rawCsv))
            {
                return results;
            }

            using (StringReader reader = new StringReader(rawCsv))
            {
                string line;
                bool headerSeen = false;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("*"))
                    {
                        continue;
                    }
                    if (!headerSeen && trimmed.StartsWith("#HostName", StringComparison.OrdinalIgnoreCase))
                    {
                        headerSeen = true;
                        continue;
                    }
                    if (trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    List<string> cols = SplitShihabCsvLine(line);
                    if (cols.Count < 14)
                    {
                        continue;
                    }

                    string hostShort = cols[0].Trim();
                    string ip = cols[1].Trim();
                    if (string.IsNullOrEmpty(hostShort) || string.IsNullOrEmpty(ip))
                    {
                        continue;
                    }

                    long score = ParseLongSafe(cols[2]);
                    int pingMs = ParseIntSafe(cols[3]);
                    long speedBps = ParseLongSafe(cols[4]);
                    string countryShort = cols[6].Trim().ToUpperInvariant();
                    if (countryShort == "UK") countryShort = "GB";
                    string countryLong = NormalizeShihabCountryTitle(cols[5], countryShort);
                    int sessions = ParseIntSafe(cols[7]);
                    string operatorName = cols[12].Trim();
                    string ovpnB64 = cols.Count > 14 ? cols[14].Trim() : string.Empty;

                    // Extract true SoftEther TLS/MS-SSTP TCP listener port from embedded OpenVPN config!
                    bool isTcpOvpn;
                    int extractedPort;
                    TryExtractOpenVpnTransportFromBase64(ovpnB64, out isTcpOvpn, out extractedPort);
                    int sstpPort = (isTcpOvpn && extractedPort > 0) ? extractedPort : 443;

                    double lat, lon;
                    ResolveShihabCountryCoordinates(countryShort, out lat, out lon);

                    int loadPct = 8;
                    if (sessions > 0)
                    {
                        loadPct = Math.Min(96, Math.Max(5, (int)Math.Round((sessions / 220.0) * 100.0)));
                    }

                    string bareHost;
                    int parsedInlinePort;
                    ParseHostAndPort(hostShort, sstpPort, out bareHost, out parsedInlinePort);
                    if (bareHost.IndexOf('.') < 0)
                    {
                        bareHost = bareHost + ".opengw.net";
                    }

                    string ccFinal = string.IsNullOrEmpty(countryShort) ? "GL" : countryShort;

                    ShihabVpnServerNode node = new ShihabVpnServerNode();
                    node.Id = "pub_" + bareHost.ToLowerInvariant() + "_" + parsedInlinePort;
                    node.SourceKind = ShihabServerSourceKind.PublicOpenRelay;
                    node.Protocol = ShihabVpnProtocol.AutoSstp;
                    node.HostName = hostShort;
                    node.SstpEndpoint = bareHost;
                    node.IpAddress = ip;
                    node.Port = parsedInlinePort;
                    node.CountryLong = countryLong;
                    node.CountryShort = ccFinal;
                    node.CityName = ResolveShihabCountryCity(ccFinal);
                    node.RegionGroup = ResolveShihabCountryRegion(ccFinal);
                    node.DisplayName = string.Format("{0} ({1})", node.CountryLong, hostShort);
                    node.PingMs = pingMs > 0 ? pingMs : 45;
                    node.SpeedBps = speedBps;
                    node.ActiveSessions = sessions;
                    node.LoadPercent = loadPct;
                    node.Score = score;
                    node.OperatorName = operatorName;
                    node.Latitude = lat;
                    node.Longitude = lon;
                    node.Username = "vpn";
                    node.OpenVpnConfigBase64 = ovpnB64;
                    node.IsTcp443Reachable = isTcpOvpn;
                    node.WasProbed = isTcpOvpn;

                    results.Add(node);
                }
            }

            return results;
        }

        public static List<ShihabVpnServerNode> ParseShihabVpnGateHtmlTable(string html)
        {
            List<ShihabVpnServerNode> list = new List<ShihabVpnServerNode>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return list;
            }

            MatchCollection rows = Regex.Matches(html, @"(?s)<tr[^>]*>(.*?)</tr>");
            for (int i = 0; i < rows.Count; i++)
            {
                string r = rows[i].Groups[1].Value;
                if (r.IndexOf("SSTP Hostname", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                Match flagM = Regex.Match(r, @"flags/([A-Za-z]{2})\.png[^>]*><br\s*/?>([^<]+)</td>", RegexOptions.IgnoreCase);
                Match sstpM = Regex.Match(r, @"SSTP Hostname\s*:<br\s*/?><b><span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                if (!flagM.Success || !sstpM.Success)
                {
                    continue;
                }

                string cc = flagM.Groups[1].Value.Trim().ToUpperInvariant();
                if (cc == "UK") cc = "GB";
                string countryTitle = NormalizeShihabCountryTitle(flagM.Groups[2].Value, cc);
                string rawSstp = sstpM.Groups[1].Value.Trim();

                string bareHost;
                int sstpPort;
                ParseHostAndPort(rawSstp, 443, out bareHost, out sstpPort);
                if (string.IsNullOrEmpty(bareHost))
                {
                    continue;
                }

                Match ipM = Regex.Match(r, @"\b(\d{1,3}(?:\.\d{1,3}){3})\b");
                string ip = ipM.Success ? ipM.Groups[1].Value : bareHost;

                Match mbpsM = Regex.Match(r, @"([\d\.]+)\s*Mbps", RegexOptions.IgnoreCase);
                double mbps = 45.0;
                if (mbpsM.Success)
                {
                    double.TryParse(mbpsM.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out mbps);
                }

                Match pingM = Regex.Match(r, @"Ping:\s*<b>(\d+)\s*ms</b>", RegexOptions.IgnoreCase);
                int ping = pingM.Success ? ParseIntSafe(pingM.Groups[1].Value) : 28;

                Match sessM = Regex.Match(r, @"(\d+)\s*sessions", RegexOptions.IgnoreCase);
                int sessions = sessM.Success ? ParseIntSafe(sessM.Groups[1].Value) : 12;

                bool hasL2tp = r.IndexOf("howto_l2tp.aspx", StringComparison.OrdinalIgnoreCase) >= 0;

                double lat, lon;
                ResolveShihabCountryCoordinates(cc, out lat, out lon);

                string shortLabel = bareHost.EndsWith(".opengw.net", StringComparison.OrdinalIgnoreCase)
                    ? bareHost.Substring(0, bareHost.Length - ".opengw.net".Length)
                    : bareHost;

                ShihabVpnServerNode node = new ShihabVpnServerNode();
                node.Id = "pub_" + bareHost.ToLowerInvariant() + "_" + sstpPort;
                node.SourceKind = ShihabServerSourceKind.PublicOpenRelay;
                node.Protocol = ShihabVpnProtocol.AutoSstp;
                node.HostName = shortLabel;
                node.SstpEndpoint = bareHost;
                node.IpAddress = ip;
                node.Port = sstpPort;
                node.CountryShort = cc;
                node.CountryLong = countryTitle;
                node.CityName = ResolveShihabCountryCity(cc);
                node.RegionGroup = ResolveShihabCountryRegion(cc);
                node.DisplayName = string.Format("{0} ({1})", countryTitle, shortLabel);
                node.PingMs = ping > 0 ? ping : 28;
                node.SpeedBps = (long)(mbps * 1000000.0);
                node.ActiveSessions = sessions;
                node.LoadPercent = Math.Min(95, Math.Max(5, (int)Math.Round((sessions / 220.0) * 100.0)));
                node.Latitude = lat;
                node.Longitude = lon;
                node.Username = "vpn";
                node.SupportsL2tpIpsec = hasL2tp;
                node.IsTcp443Reachable = true;
                node.WasProbed = true;
                node.IsLocallyProbed = false;
                node.IsSstpVerified = false;
                list.Add(node);
            }

            return list;
        }

        public static List<ShihabVpnServerNode> ParseShihabGlobalPoolJson(string poolJson)
        {
            List<ShihabVpnServerNode> list = new List<ShihabVpnServerNode>();
            if (string.IsNullOrWhiteSpace(poolJson))
            {
                return list;
            }

            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 36 * 1024 * 1024;
                Dictionary<string, object> root = ser.DeserializeObject(poolJson) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("servers"))
                {
                    return list;
                }

                IEnumerable serversEnum = root["servers"] as IEnumerable;
                if (serversEnum == null)
                {
                    return list;
                }

                foreach (object rawObj in serversEnum)
                {
                    Dictionary<string, object> s = rawObj as Dictionary<string, object>;
                    if (s == null) continue;

                    string hostShort = GetDictString(s, "hostname");
                    string ip = GetDictString(s, "ip");
                    string cc = GetDictString(s, "country_code").ToUpperInvariant();
                    if (cc == "UK") cc = "GB";
                    if (string.IsNullOrEmpty(hostShort) || string.IsNullOrEmpty(cc))
                    {
                        continue;
                    }

                    bool tcpReachable = false;
                    int resolvedPort = 443;
                    int probeConnectMs = 0;

                    if (s.ContainsKey("tcp_probe") && s["tcp_probe"] is Dictionary<string, object>)
                    {
                        Dictionary<string, object> tp = (Dictionary<string, object>)s["tcp_probe"];
                        string status = GetDictString(tp, "status");
                        if (string.Equals(status, "reachable", StringComparison.OrdinalIgnoreCase))
                        {
                            tcpReachable = true;
                            probeConnectMs = GetDictInt(tp, "connect_ms");
                            if (tp.ContainsKey("connected_endpoint") && tp["connected_endpoint"] is Dictionary<string, object>)
                            {
                                Dictionary<string, object> cep = (Dictionary<string, object>)tp["connected_endpoint"];
                                int cp = GetDictInt(cep, "port");
                                if (cp > 0 && cp <= 65535)
                                {
                                    resolvedPort = cp;
                                }
                                string cip = GetDictString(cep, "ip");
                                if (!string.IsNullOrEmpty(cip))
                                {
                                    ip = cip;
                                }
                            }
                        }
                    }

                    if (!tcpReachable && s.ContainsKey("probe_targets") && s["probe_targets"] is IEnumerable)
                    {
                        foreach (object ptObj in (IEnumerable)s["probe_targets"])
                        {
                            Dictionary<string, object> pt = ptObj as Dictionary<string, object>;
                            if (pt != null)
                            {
                                int p = GetDictInt(pt, "port");
                                if (p > 0 && p <= 65535)
                                {
                                    resolvedPort = p;
                                    break;
                                }
                            }
                        }
                    }

                    // For huge mega-hubs (JP / KR / RU / TH / VN), keep only verified TCP-reachable nodes so quality stays high.
                    // For underrepresented countries, also keep unprobed candidates so the engine can test SoftEther ports 443/995/5555 on demand.
                    if (!tcpReachable && (cc == "JP" || cc == "KR" || cc == "RU" || cc == "TH" || cc == "VN"))
                    {
                        continue;
                    }

                    string bareHost;
                    int finalPort;
                    ParseHostAndPort(hostShort, resolvedPort, out bareHost, out finalPort);
                    if (bareHost.IndexOf('.') < 0)
                    {
                        bareHost = bareHost + ".opengw.net";
                    }

                    string countryLong = NormalizeShihabCountryTitle(GetDictString(s, "country_name"), cc);
                    int pingMs = GetDictInt(s, "ping_ms");
                    if (pingMs <= 0 && probeConnectMs > 0) pingMs = probeConnectMs;
                    if (pingMs <= 0) pingMs = 35;

                    long speedBps = GetDictLong(s, "speed_bps");
                    int sessions = GetDictInt(s, "num_vpn_sessions");
                    long score = GetDictLong(s, "score");

                    double lat, lon;
                    ResolveShihabCountryCoordinates(cc, out lat, out lon);

                    ShihabVpnServerNode node = new ShihabVpnServerNode();
                    node.Id = "pub_" + bareHost.ToLowerInvariant() + "_" + finalPort;
                    node.SourceKind = ShihabServerSourceKind.PublicOpenRelay;
                    node.Protocol = ShihabVpnProtocol.AutoSstp;
                    node.HostName = hostShort;
                    node.SstpEndpoint = bareHost;
                    node.IpAddress = string.IsNullOrEmpty(ip) ? bareHost : ip;
                    node.Port = finalPort;
                    node.CountryShort = cc;
                    node.CountryLong = countryLong;
                    node.CityName = ResolveShihabCountryCity(cc);
                    node.RegionGroup = ResolveShihabCountryRegion(cc);
                    node.DisplayName = string.Format("{0} ({1})", countryLong, hostShort);
                    node.PingMs = pingMs;
                    node.SpeedBps = speedBps > 0 ? speedBps : 45000000L;
                    node.ActiveSessions = sessions;
                    node.LoadPercent = sessions > 0 ? Math.Min(95, Math.Max(5, (int)Math.Round((sessions / 220.0) * 100.0))) : 10;
                    node.Score = score;
                    node.Latitude = lat;
                    node.Longitude = lon;
                    node.Username = "vpn";
                    node.IsTcp443Reachable = tcpReachable;
                    node.WasProbed = tcpReachable;
                    node.IsLocallyProbed = false;
                    node.IsSstpVerified = false;

                    list.Add(node);
                }
            }
            catch
            {
            }

            return list;
        }

        public static List<ShihabVpnServerNode> ParseShihabSstpSubscriptionFeed(string feedText)
        {
            List<ShihabVpnServerNode> list = new List<ShihabVpnServerNode>();
            if (string.IsNullOrWhiteSpace(feedText))
            {
                return list;
            }

            using (StringReader sr = new StringReader(feedText))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("#"))
                    {
                        continue;
                    }

                    // Format A: sstp://vpn:vpn@vpn924983651.opengw.net:1442#US
                    if (t.StartsWith("sstp://", StringComparison.OrdinalIgnoreCase))
                    {
                        int atIdx = t.IndexOf('@');
                        int hashIdx = t.LastIndexOf('#');
                        if (atIdx > 0 && hashIdx > atIdx)
                        {
                            string hostPort = t.Substring(atIdx + 1, hashIdx - atIdx - 1).Trim();
                            string cc = t.Substring(hashIdx + 1).Trim().ToUpperInvariant();
                            if (cc == "UK") cc = "GB";
                            string bareHost;
                            int port;
                            ParseHostAndPort(hostPort, 443, out bareHost, out port);
                            if (!string.IsNullOrEmpty(bareHost) && bareHost.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string countryTitle = NormalizeShihabCountryTitle(null, cc);
                                double lat, lon;
                                ResolveShihabCountryCoordinates(cc, out lat, out lon);
                                string shortH = bareHost.Substring(0, bareHost.Length - ".opengw.net".Length);

                                ShihabVpnServerNode n = new ShihabVpnServerNode();
                                n.Id = "pub_" + bareHost.ToLowerInvariant() + "_" + port;
                                n.SourceKind = ShihabServerSourceKind.PublicOpenRelay;
                                n.Protocol = ShihabVpnProtocol.AutoSstp;
                                n.HostName = shortH;
                                n.SstpEndpoint = bareHost;
                                n.IpAddress = bareHost;
                                n.Port = port;
                                n.CountryShort = cc;
                                n.CountryLong = countryTitle;
                                n.CityName = ResolveShihabCountryCity(cc);
                                n.RegionGroup = ResolveShihabCountryRegion(cc);
                                n.DisplayName = string.Format("{0} ({1})", countryTitle, shortH);
                                n.PingMs = 30;
                                n.SpeedBps = 65000000L;
                                n.LoadPercent = 12;
                                n.Latitude = lat;
                                n.Longitude = lon;
                                n.Username = "vpn";
                                n.IsTcp443Reachable = true;
                                n.WasProbed = true;
                                n.IsLocallyProbed = false;
                                n.IsSstpVerified = false;
                                list.Add(n);
                            }
                        }
                        continue;
                    }

                    // Format B: mehdihoore sstps.csv -> idx,country,ddns_ip,ping,sstp,server,port
                    if (t.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) >= 0 && t.IndexOf(',') >= 0)
                    {
                        List<string> cols = SplitShihabCsvLine(t);
                        if (cols.Count >= 6)
                        {
                            string rawCountry = cols[1].Trim();
                            string cc = LookupShihabCountryCodeByName(rawCountry);
                            if (cc == "GL") continue;
                            string sstpCol = cols[4].Trim();
                            string bareHost;
                            int port;
                            ParseHostAndPort(sstpCol, 443, out bareHost, out port);
                            if (!string.IsNullOrEmpty(bareHost) && bareHost.IndexOf('.') >= 0)
                            {
                                string countryTitle = NormalizeShihabCountryTitle(rawCountry, cc);
                                Match ipM = Regex.Match(cols[2], @"(\d{1,3}(?:\.\d{1,3}){3})");
                                double lat, lon;
                                ResolveShihabCountryCoordinates(cc, out lat, out lon);
                                string shortH = bareHost.EndsWith(".opengw.net", StringComparison.OrdinalIgnoreCase)
                                    ? bareHost.Substring(0, bareHost.Length - ".opengw.net".Length)
                                    : bareHost;

                                ShihabVpnServerNode n = new ShihabVpnServerNode();
                                n.Id = "pub_" + bareHost.ToLowerInvariant() + "_" + port;
                                n.SourceKind = ShihabServerSourceKind.PublicOpenRelay;
                                n.Protocol = ShihabVpnProtocol.AutoSstp;
                                n.HostName = shortH;
                                n.SstpEndpoint = bareHost;
                                n.IpAddress = ipM.Success ? ipM.Groups[1].Value : bareHost;
                                n.Port = port;
                                n.CountryShort = cc;
                                n.CountryLong = countryTitle;
                                n.CityName = ResolveShihabCountryCity(cc);
                                n.RegionGroup = ResolveShihabCountryRegion(cc);
                                n.DisplayName = string.Format("{0} ({1})", countryTitle, shortH);
                                n.PingMs = Math.Max(8, ParseIntSafe(cols[3]));
                                n.SpeedBps = 75000000L;
                                n.LoadPercent = 10;
                                n.Latitude = lat;
                                n.Longitude = lon;
                                n.Username = "vpn";
                                n.IsTcp443Reachable = true;
                                n.WasProbed = true;
                                n.IsLocallyProbed = false;
                                n.IsSstpVerified = false;
                                list.Add(n);
                            }
                        }
                    }
                }
            }

            return list;
        }

        public static ShihabResult<ShihabVpnServerNode> ParseShihabWireGuardConf(string confContent, string profileTitle)
        {
            if (string.IsNullOrWhiteSpace(confContent))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("EMPTY_CONF", "WireGuard configuration text is empty.", 0);
            }

            string endpointHost = string.Empty;
            int endpointPort = 51820;
            string publicKey = string.Empty;
            string privateKey = string.Empty;
            string address = string.Empty;

            string currentSection = string.Empty;
            using (StringReader sr = new StringReader(confContent))
            {
                string rawLine;
                while ((rawLine = sr.ReadLine()) != null)
                {
                    int hashIdx = rawLine.IndexOf('#');
                    string clean = (hashIdx >= 0 ? rawLine.Substring(0, hashIdx) : rawLine).Trim();
                    if (string.IsNullOrEmpty(clean))
                    {
                        continue;
                    }
                    if (clean.StartsWith("[") && clean.EndsWith("]"))
                    {
                        currentSection = clean.Substring(1, clean.Length - 2).Trim();
                        continue;
                    }

                    int eqIdx = clean.IndexOf('=');
                    if (eqIdx <= 0)
                    {
                        continue;
                    }

                    string key = clean.Substring(0, eqIdx).Trim();
                    string val = clean.Substring(eqIdx + 1).Trim();

                    if (string.Equals(currentSection, "Interface", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "PrivateKey", StringComparison.OrdinalIgnoreCase))
                        {
                            privateKey = val;
                        }
                        else if (string.Equals(key, "Address", StringComparison.OrdinalIgnoreCase))
                        {
                            address = val;
                        }
                    }
                    else if (string.Equals(currentSection, "Peer", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "PublicKey", StringComparison.OrdinalIgnoreCase))
                        {
                            publicKey = val;
                        }
                        else if (string.Equals(key, "Endpoint", StringComparison.OrdinalIgnoreCase))
                        {
                            ParseHostAndPort(val, 51820, out endpointHost, out endpointPort);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(privateKey) || string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(endpointHost))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "INVALID_WG_CONF",
                    "WireGuard .conf requires [Interface] PrivateKey and [Peer] PublicKey + Endpoint.",
                    0);
            }

            ShihabVpnServerNode node = new ShihabVpnServerNode();
            node.Id = "priv_wg_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            node.SourceKind = ShihabServerSourceKind.PrivateCustomServer;
            node.Protocol = ShihabVpnProtocol.WireGuard;
            node.DisplayName = string.IsNullOrWhiteSpace(profileTitle) ? ("WireGuard - " + endpointHost) : profileTitle.Trim();
            node.HostName = endpointHost;
            node.SstpEndpoint = endpointHost;
            node.IpAddress = endpointHost;
            node.Port = endpointPort;
            node.CountryLong = "Private WireGuard";
            node.CountryShort = "WG";
            node.CityName = "Custom Endpoint";
            node.RegionGroup = "Private";
            node.LoadPercent = 5;
            node.Latitude = 46.82;
            node.Longitude = 8.23;
            node.OperatorName = string.IsNullOrEmpty(address) ? "Private Tunnel" : ("Interface " + address);
            node.RawConfigProfile = confContent;

            return ShihabResult<ShihabVpnServerNode>.Ok(node, 0);
        }

        public static ShihabResult<ShihabVpnServerNode> ParseShihabOpenVpnProfile(string ovpnContent, string profileTitle)
        {
            if (string.IsNullOrWhiteSpace(ovpnContent))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("EMPTY_OVPN", "OpenVPN .ovpn profile text is empty.", 0);
            }

            string remoteHost = string.Empty;
            int remotePort = 1194;

            using (StringReader sr = new StringReader(ovpnContent))
            {
                string rawLine;
                while ((rawLine = sr.ReadLine()) != null)
                {
                    string clean = rawLine.Trim();
                    if (string.IsNullOrEmpty(clean) || clean.StartsWith("#") || clean.StartsWith(";"))
                    {
                        continue;
                    }
                    if (clean.StartsWith("remote ", StringComparison.OrdinalIgnoreCase) ||
                        clean.StartsWith("remote\t", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = clean.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            remoteHost = parts[1].Trim();
                            if (parts.Length >= 3)
                            {
                                int parsedP;
                                if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedP) && parsedP > 0 && parsedP <= 65535)
                                {
                                    remotePort = parsedP;
                                }
                            }
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(remoteHost))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "INVALID_OVPN",
                    "OpenVPN .ovpn profile does not contain a valid 'remote <host> [port]' directive.",
                    0);
            }

            ShihabVpnServerNode node = new ShihabVpnServerNode();
            node.Id = "priv_ovpn_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            node.SourceKind = ShihabServerSourceKind.PrivateCustomServer;
            node.Protocol = ShihabVpnProtocol.OpenVpn;
            node.DisplayName = string.IsNullOrWhiteSpace(profileTitle) ? ("OpenVPN - " + remoteHost) : profileTitle.Trim();
            node.HostName = remoteHost;
            node.SstpEndpoint = remoteHost;
            node.IpAddress = remoteHost;
            node.Port = remotePort;
            node.CountryLong = "Private OpenVPN";
            node.CountryShort = "OV";
            node.CityName = "Custom Endpoint";
            node.RegionGroup = "Private";
            node.LoadPercent = 6;
            node.Latitude = 51.16;
            node.Longitude = 10.45;
            node.OperatorName = "OpenVPN Port " + remotePort;
            node.RawConfigProfile = ovpnContent;

            return ShihabResult<ShihabVpnServerNode>.Ok(node, 0);
        }

        public static ShihabResult<ShihabVpnServerNode> ParseShihabCustomProxyProfile(
            string rawEndpointOrUri,
            string profileTitle,
            string explicitUsername,
            string explicitPassword,
            out string resolvedPlainPassword)
        {
            resolvedPlainPassword = explicitPassword ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawEndpointOrUri))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("EMPTY_PROXY", "HTTP/HTTPS proxy endpoint cannot be empty.", 0);
            }

            string s = rawEndpointOrUri.Trim();
            string scheme = "http";
            int defaultPort = 8080;

            if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
                defaultPort = 443;
                s = s.Substring(8).Trim();
            }
            else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "http";
                defaultPort = 8080;
                s = s.Substring(7).Trim();
            }

            int slashIdx = s.IndexOf('/');
            if (slashIdx >= 0)
            {
                s = s.Substring(0, slashIdx).Trim();
            }

            string resolvedUser = (explicitUsername ?? string.Empty).Trim();
            int atIdx = s.LastIndexOf('@');
            if (atIdx > 0 && atIdx < s.Length - 1)
            {
                string userInfo = s.Substring(0, atIdx);
                s = s.Substring(atIdx + 1).Trim();
                int colonCred = userInfo.IndexOf(':');
                if (colonCred >= 0)
                {
                    if (string.IsNullOrEmpty(resolvedUser))
                    {
                        resolvedUser = Uri.UnescapeDataString(userInfo.Substring(0, colonCred));
                    }
                    if (string.IsNullOrEmpty(resolvedPlainPassword))
                    {
                        resolvedPlainPassword = Uri.UnescapeDataString(userInfo.Substring(colonCred + 1));
                    }
                }
                else if (string.IsNullOrEmpty(resolvedUser))
                {
                    resolvedUser = Uri.UnescapeDataString(userInfo);
                }
            }

            string host;
            int port;
            ParseHostAndPort(s, defaultPort, out host, out port);
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "INVALID_PROXY_ENDPOINT",
                    "Proxy server requires a valid host:port (for example proxy.example.com:8080).",
                    0);
            }

            string title = string.IsNullOrWhiteSpace(profileTitle)
                ? ("Proxy - " + host + ":" + port)
                : profileTitle.Trim();

            string inferredCc = LookupShihabCountryCodeByName(title);
            if (inferredCc == "GL")
            {
                inferredCc = "PX";
            }

            double lat, lon;
            ResolveShihabCountryCoordinates(inferredCc == "PX" ? "US" : inferredCc, out lat, out lon);

            ShihabVpnServerNode node = new ShihabVpnServerNode();
            node.Id = "priv_proxy_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            node.SourceKind = ShihabServerSourceKind.PrivateCustomServer;
            node.Protocol = ShihabVpnProtocol.HttpSystemProxy;
            node.DisplayName = title;
            node.HostName = host;
            node.SstpEndpoint = host;
            node.IpAddress = host;
            node.Port = port;
            node.CountryLong = inferredCc == "PX" ? title : NormalizeShihabCountryTitle(title, inferredCc);
            node.CountryShort = inferredCc;
            node.CityName = inferredCc == "PX" ? "Custom Proxy" : ResolveShihabCountryCity(inferredCc);
            node.RegionGroup = inferredCc == "PX" ? "Private" : ResolveShihabCountryRegion(inferredCc);
            node.Latitude = lat;
            node.Longitude = lon;
            node.LoadPercent = 5;
            node.PingMs = 22;
            node.Username = resolvedUser;
            node.OperatorName = scheme.ToUpperInvariant() + " System Proxy";
            node.RawConfigProfile = scheme;

            return ShihabResult<ShihabVpnServerNode>.Ok(node, 0);
        }

        public static void ParseHostAndPort(string input, int defaultPort, out string host, out int port)
        {
            host = string.Empty;
            port = defaultPort;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }
            string s = input.Trim();
            if (s.StartsWith("["))
            {
                int closeBracket = s.IndexOf(']');
                if (closeBracket > 1)
                {
                    host = s.Substring(1, closeBracket - 1);
                    if (closeBracket + 2 < s.Length && s[closeBracket + 1] == ':')
                    {
                        int p;
                        if (int.TryParse(s.Substring(closeBracket + 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p > 0 && p <= 65535)
                        {
                            port = p;
                        }
                    }
                    return;
                }
            }

            int lastColon = s.LastIndexOf(':');
            if (lastColon > 0 && lastColon < s.Length - 1)
            {
                int p2;
                if (int.TryParse(s.Substring(lastColon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out p2) && p2 > 0 && p2 <= 65535)
                {
                    host = s.Substring(0, lastColon).Trim();
                    port = p2;
                    return;
                }
            }
            host = s;
        }

        private static string GetDictString(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                return Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
            }
            return string.Empty;
        }

        private static int GetDictInt(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                if (v is int) return (int)v;
                int parsed;
                if (int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return 0;
        }

        private static long GetDictLong(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                if (v is long) return (long)v;
                if (v is int) return (int)v;
                long parsed;
                if (long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return 0L;
        }

        private static List<string> SplitShihabCsvLine(string line)
        {
            List<string> fields = new List<string>(16);
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }

        private static int ParseIntSafe(string s)
        {
            int v;
            if (int.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }
            return 0;
        }

        private static long ParseLongSafe(string s)
        {
            long v;
            if (long.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }
            return 0L;
        }
    }
}
