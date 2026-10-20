using System.Text.RegularExpressions;

namespace graduatio_project.Options
{
    public class GoogleAuthOptions
    {
        public const string SectionName = "GoogleAuth";

        public bool Enabled { get; set; }

        public string WebClientId { get; set; } = string.Empty;

        public bool HasValidWebClientId()
        {
            return !string.IsNullOrWhiteSpace(WebClientId)
                && WebClientId.Length <= 256
                && Regex.IsMatch(WebClientId,
                    @"\A[0-9]+-[A-Za-z0-9_-]+\.apps\.googleusercontent\.com\z",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
    }
}
