using System.Text;

namespace DiscordScheduler
{
    public sealed class PayloadTextNormalizer
    {
        public string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Normalize(NormalizationForm.FormC);
            var sb = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                var ch = normalized[i];
                if (ch == '\n')
                {
                    sb.Append(ch);
                    continue;
                }

                if (char.IsControl(ch))
                {
                    sb.Append(' ');
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        public int GetNormalizedLength(string value)
        {
            return Normalize(value).Length;
        }

        public bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(Normalize(value));
        }

        public string BuildNormalContent(string title, string body)
        {
            var normalizedTitle = Normalize(title);
            var normalizedBody = Normalize(body);

            if (string.IsNullOrWhiteSpace(normalizedTitle))
                return normalizedBody;

            if (string.IsNullOrWhiteSpace(normalizedBody))
                return normalizedTitle;

            return normalizedTitle + "\n" + normalizedBody;
        }
    }
}
