using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Nexar;

/// <summary>
/// HTTP Digest authentication (RFC 7616, compatible with RFC 2617 and RFC 2069 servers).
/// </summary>
internal sealed class DigestAuthenticator : IAuthenticator
{
    private readonly string _username;
    private readonly string _password;
    private readonly Func<string> _createCnonce;
    private readonly object _gate = new();
    private Challenge? _challenge;
    private int _nonceCount;

    public DigestAuthenticator(string username, string password, Func<string>? createCnonce = null)
    {
        _username = username;
        _password = password;
        _createCnonce = createCnonce ?? (() => RandomNumberGenerator.GetHexString(32, lowercase: true));
    }

    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Challenge? challenge;
        int nonceCount;
        lock (_gate)
        {
            challenge = _challenge;
            if (challenge == null)
            {
                return ValueTask.CompletedTask;
            }
            nonceCount = ++_nonceCount;
        }

        var parameter = BuildAuthorization(challenge, request.Method.Method, request.RequestUri!.PathAndQuery, nonceCount, _createCnonce());
        request.Headers.Authorization = new AuthenticationHeaderValue("Digest", parameter);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnUnauthorizedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var challenge = Challenge.Select(response.Headers.WwwAuthenticate);
        if (challenge == null)
        {
            return ValueTask.FromResult(false);
        }

        lock (_gate)
        {
            // Same nonce rejected without stale=true means the credentials are wrong; don't try again.
            var sentDigest = string.Equals(response.RequestMessage?.Headers.Authorization?.Scheme, "Digest", StringComparison.OrdinalIgnoreCase);
            if (sentDigest && !challenge.Stale && challenge.Nonce == _challenge?.Nonce)
            {
                return ValueTask.FromResult(false);
            }

            _challenge = challenge;
            _nonceCount = 0;
        }
        return ValueTask.FromResult(true);
    }

    private string BuildAuthorization(Challenge challenge, string method, string uri, int nonceCount, string cnonce)
    {
        var nc = nonceCount.ToString("x8", CultureInfo.InvariantCulture);

        var ha1 = Hash(challenge.Algorithm, $"{_username}:{challenge.Realm}:{_password}");
        if (challenge.IsSession)
        {
            ha1 = Hash(challenge.Algorithm, $"{ha1}:{challenge.Nonce}:{cnonce}");
        }
        var ha2 = Hash(challenge.Algorithm, $"{method}:{uri}");
        var response = challenge.Qop == null
            ? Hash(challenge.Algorithm, $"{ha1}:{challenge.Nonce}:{ha2}")
            : Hash(challenge.Algorithm, $"{ha1}:{challenge.Nonce}:{nc}:{cnonce}:{challenge.Qop}:{ha2}");

        var builder = new StringBuilder()
            .Append("username=").Append(Quote(_username))
            .Append(", realm=").Append(Quote(challenge.Realm))
            .Append(", nonce=").Append(Quote(challenge.Nonce))
            .Append(", uri=").Append(Quote(uri));
        if (challenge.AlgorithmToken != null)
        {
            builder.Append(", algorithm=").Append(challenge.AlgorithmToken);
        }
        builder.Append(", response=").Append(Quote(response));
        if (challenge.Qop != null)
        {
            builder.Append(", qop=").Append(challenge.Qop)
                .Append(", nc=").Append(nc)
                .Append(", cnonce=").Append(Quote(cnonce));
        }
        if (challenge.Opaque != null)
        {
            builder.Append(", opaque=").Append(Quote(challenge.Opaque));
        }
        return builder.ToString();
    }

    private static string Hash(DigestAlgorithm algorithm, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = algorithm == DigestAlgorithm.Sha256 ? SHA256.HashData(bytes) : MD5.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private enum DigestAlgorithm { Md5, Sha256 }

    private sealed record Challenge(
        string Realm,
        string Nonce,
        string? Opaque,
        string? Qop,
        DigestAlgorithm Algorithm,
        string? AlgorithmToken,
        bool IsSession,
        bool Stale)
    {
        /// <summary>
        /// Picks the strongest supported Digest challenge: SHA-256 over MD5.
        /// </summary>
        public static Challenge? Select(IEnumerable<AuthenticationHeaderValue> headers)
        {
            return headers
                .Where(h => string.Equals(h.Scheme, "Digest", StringComparison.OrdinalIgnoreCase) && h.Parameter != null)
                .Select(h => TryParse(h.Parameter!))
                .Where(c => c != null)
                .OrderByDescending(c => c!.Algorithm)
                .FirstOrDefault();
        }

        private static Challenge? TryParse(string parameter)
        {
            var values = ParseParameters(parameter);
            if (!values.TryGetValue("realm", out var realm) || !values.TryGetValue("nonce", out var nonce))
            {
                return null;
            }

            values.TryGetValue("algorithm", out var algorithmToken);
            var normalized = (algorithmToken ?? "MD5").ToUpperInvariant();
            var isSession = normalized.EndsWith("-SESS", StringComparison.Ordinal);
            DigestAlgorithm algorithm;
            switch (isSession ? normalized[..^5] : normalized)
            {
                case "MD5": algorithm = DigestAlgorithm.Md5; break;
                case "SHA-256": algorithm = DigestAlgorithm.Sha256; break;
                default: return null;
            }

            string? qop = null;
            if (values.TryGetValue("qop", out var qopOptions))
            {
                // Only "auth" is supported; a challenge offering just "auth-int" is skipped.
                if (!qopOptions.Split(',').Any(q => q.Trim().Equals("auth", StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }
                qop = "auth";
            }

            values.TryGetValue("opaque", out var opaque);
            var stale = values.TryGetValue("stale", out var staleValue) && staleValue.Equals("true", StringComparison.OrdinalIgnoreCase);

            return new Challenge(realm, nonce, opaque, qop, algorithm, algorithmToken, isSession, stale);
        }

        private static Dictionary<string, string> ParseParameters(string input)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            while (i < input.Length)
            {
                while (i < input.Length && (input[i] == ',' || char.IsWhiteSpace(input[i]))) i++;
                var keyStart = i;
                while (i < input.Length && input[i] != '=' && input[i] != ',') i++;
                var key = input[keyStart..i].Trim();
                if (i >= input.Length || input[i] != '=')
                {
                    continue;
                }
                i++;
                while (i < input.Length && char.IsWhiteSpace(input[i])) i++;

                var value = new StringBuilder();
                if (i < input.Length && input[i] == '"')
                {
                    i++;
                    while (i < input.Length && input[i] != '"')
                    {
                        if (input[i] == '\\' && i + 1 < input.Length) i++;
                        value.Append(input[i++]);
                    }
                    i++;
                }
                else
                {
                    while (i < input.Length && input[i] != ',') value.Append(input[i++]);
                }

                if (key.Length > 0)
                {
                    result[key] = value.ToString().Trim();
                }
            }
            return result;
        }
    }
}
