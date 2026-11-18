using System.Net;

namespace ProxyGAyer.Core;

public static class ProxyTester
{
    public static async Task<string> TestAsync(ProxyProfile profile, string target, CancellationToken cancellationToken = default)
    {
        profile.Validate();
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("آدرس تست باید یک URL معتبر HTTP یا HTTPS باشد.");
        using var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new ExplicitProxy(profile),
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"پاسخ مقصد یا پروکسی: HTTP {(int)response.StatusCode}. اتصال موفق تأیید نشد.");
        var endpoint = uri.Scheme == "https" ? profile.Https : profile.Http;
        return $"{uri.Scheme.ToUpperInvariant()}: HTTP {(int)response.StatusCode} — پروکسی آزمایش‌شده: {endpoint.Authority}";
    }

    private sealed class ExplicitProxy(ProxyProfile profile) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => new(destination.Scheme == "https" ? profile.Https.Url : profile.Http.Url);
        public bool IsBypassed(Uri host) => false;
    }
}
