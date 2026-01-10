using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordScheduler
{
    public static class JsonUtil
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32)
                            sb.AppendFormat("\\u{0:X4}", (int)ch);
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        public static List<string> SplitCsvIds(string csv)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return res;

            var parts = csv.Split(',');
            foreach (var p in parts)
            {
                var t = (p ?? "").Trim();
                if (t.Length == 0) continue;
                // keep only digits (Discord IDs are snowflakes)
                bool ok = true;
                for (int i = 0; i < t.Length; i++)
                {
                    if (t[i] < '0' || t[i] > '9') { ok = false; break; }
                }
                if (ok) res.Add(t);
            }
            return res;
        }

        public static string JsonArrayOfStrings(List<string> values)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(values[i])).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string BuildAllowedMentions(AllowedMentions m)
        {
            // parse array controls actual parsing of mentions
            var parse = new List<string>();
            if (m.allowUsers) parse.Add("users");
            if (m.allowRoles) parse.Add("roles");
            if (m.allowEveryone) parse.Add("everyone");

            var users = m.allowUsers ? SplitCsvIds(m.userIdsCsv) : new List<string>();
            var roles = m.allowRoles ? SplitCsvIds(m.roleIdsCsv) : new List<string>();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"parse\":").Append(JsonArrayOfStrings(parse));
            if (users.Count > 0) sb.Append(",\"users\":").Append(JsonArrayOfStrings(users));
            if (roles.Count > 0) sb.Append(",\"roles\":").Append(JsonArrayOfStrings(roles));
            sb.Append("}");
            return sb.ToString();
        }
    }
}