using System.Diagnostics;
using System.Text;

namespace ProxyGAyer.Core;

public static class SessionLauncher
{
    public static Dictionary<string, string> EnvironmentFor(ServiceDefinition service, ProxyProfile profile)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["http_proxy"] = profile.Http.Url,
            ["https_proxy"] = profile.Https.Url,
            ["no_proxy"] = profile.Bypass
        };
        if (service.Id == "cargo") values["CARGO_HTTP_PROXY"] = profile.Http.Url;
        return values;
    }

    public static ProcessStartInfo CreateStartInfo(ServiceDefinition service, ProxyProfile profile, string? customExecutable = null)
    {
        profile.Validate();
        var executable = service.Executable ?? customExecutable;
        if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentException("یک فایل اجرایی انتخاب کنید.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        foreach (var pair in EnvironmentFor(service, profile))
        {
            foreach (var key in info.Environment.Keys.Where(k => k.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)).ToArray())
                info.Environment.Remove(key);
            info.Environment[pair.Key] = pair.Value;
        }
        if (service.Id == "windows-powershell")
        {
            const string bootstrap = "$p = New-Object System.Net.WebProxy($env:http_proxy); " +
                "$p.BypassProxyOnLocal = $true; [System.Net.WebRequest]::DefaultWebProxy = $p; " +
                "Write-Host 'ProxyGAyer session: proxy configured.' -ForegroundColor Cyan";
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap)) }) info.ArgumentList.Add(argument);
        }
        else if (service.Id == "powershell")
        {
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NoExit" }) info.ArgumentList.Add(argument);
        }
        else if (executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add("/D");
        return info;
    }
}
