using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordScheduler
{
    public static class JsonUtil
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length + 8);
            foreach (var ch in value)
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
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return ids;

            var parts = csv.Split(',');
            foreach (var part in parts)
            {
                var trimmedId = (part ?? "").Trim();
                if (trimmedId.Length == 0) continue;
                // keep only digits (Discord IDs are snowflakes)
                bool containsOnlyDigits = true;
                for (int i = 0; i < trimmedId.Length; i++)
                {
                    if (trimmedId[i] < '0' || trimmedId[i] > '9')
                    {
                        containsOnlyDigits = false;
                        break;
                    }
                }
                if (containsOnlyDigits) ids.Add(trimmedId);
            }
            return ids;
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
            if (m == null)
                return "{\"parse\":[]}";

            var users = m.allowUsers ? SplitCsvIds(m.userIdsCsv) : new List<string>();
            var roles = m.allowRoles ? SplitCsvIds(m.roleIdsCsv) : new List<string>();

            // Discord does not allow parse.users/parse.roles together with explicit arrays
            // for the same mention type. Explicit IDs mean "allow only these IDs".
            var parse = new List<string>();
            if (m.allowUsers && users.Count == 0) parse.Add("users");
            if (m.allowRoles && roles.Count == 0) parse.Add("roles");
            if (m.allowEveryone) parse.Add("everyone");

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
