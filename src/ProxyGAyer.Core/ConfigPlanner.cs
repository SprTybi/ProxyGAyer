using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProxyGAyer.Core;

public sealed class ConfigPlanner(IResourceStore resources, string home, string appData, Func<string, string?>? environment = null)
{
    private readonly Func<string, string?> env = environment ?? Environment.GetEnvironmentVariable;
    private const string InternetPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public Dictionary<string, string?> Plan(ServiceDefinition service, ProxyProfile profile)
    {
        profile.Validate();
        if (service.SingleProxy && profile.Http != profile.Https)
            throw new ArgumentException("این ابزار یک پروکسی مشترک برای HTTP و HTTPS می‌پذیرد.");
        var result = new Dictionary<string, string?>();
        var http = profile.Http.Url;
        var https = profile.Https.Url;
        switch (service.Kind)
        {
            case AdapterKind.Session: break;
            case AdapterKind.Git:
                // Git supports one default http.proxy for HTTP AND HTTPS. It has
                // no separate default https.proxy setting.
                FilePatch(Override("GIT_CONFIG_GLOBAL", Path.Combine(Override("HOME", home), ".gitconfig")),
                    text => Append(text, "[http]\n\tproxy = " + JsonSerializer.Serialize(http)), result);
                break;
            case AdapterKind.Npm:
                FilePatch(Override("NPM_CONFIG_USERCONFIG", Path.Combine(home, ".npmrc")), text =>
                    SetProperties(text, new() { ["proxy"] = http, ["https-proxy"] = https }, '='), result);
                break;
            case AdapterKind.Yarn:
                FilePatch(Path.Combine(home, ".yarnrc.yml"), text =>
                    SetYamlProperties(text, new() { ["httpProxy"] = JsonSerializer.Serialize(http),
                        ["httpsProxy"] = JsonSerializer.Serialize(https) }, ':'), result);
                break;
            case AdapterKind.Pip:
                FilePatch(Path.Combine(appData, "pip", "pip.ini"), text => SetIni(text, "global", "proxy", http), result);
                break;
            case AdapterKind.Gradle:
                FilePatch(Path.Combine(Override("GRADLE_USER_HOME", Path.Combine(home, ".gradle")), "gradle.properties"), text =>
                    Append(text, $"systemProp.http.proxyHost={profile.Http.Host}\nsystemProp.http.proxyPort={profile.Http.Port}\n" +
                        $"systemProp.https.proxyHost={profile.Https.Host}\nsystemProp.https.proxyPort={profile.Https.Port}"), result);
                break;
            case AdapterKind.UserEnvironment:
                foreach (var pair in SessionLauncher.EnvironmentFor(service, profile))
                    result["reg|Environment|" + pair.Key] = WindowsResources.StringValue(pair.Value);
                break;
            case AdapterKind.WindowsInternet:
                result[$"reg|{InternetPath}|ProxyServer"] = WindowsResources.StringValue($"http={profile.Http.Authority};https={profile.Https.Authority}");
                result[$"reg|{InternetPath}|ProxyEnable"] = WindowsResources.DwordValue(1);
                break;
            case AdapterKind.WinHttp:
                result["winhttp"] = JsonSerializer.Serialize(new WinHttpData(3,
                    $"http={profile.Http.Authority};https={profile.Https.Authority}", null));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(service));
        }
        return result;
    }

    private string Override(string key, string fallback) => env(key) is { Length: > 0 } value ? Path.GetFullPath(value) : fallback;

    private void FilePatch(string path, Func<string, string> transform, Dictionary<string, string?> result)
    {
        var key = "file|" + Path.GetFullPath(path);
        var original = resources.Read(key);
        var bytes = original is null ? [] : Convert.FromBase64String(original);
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true);
        var text = reader.ReadToEnd();
        var encoding = reader.CurrentEncoding;
        // New files and UTF-8 files are emitted without a BOM (Git compatibility).
        var updated = encoding.GetBytes(transform(text));
        if (encoding.CodePage != Encoding.UTF8.CodePage)
            updated = [.. encoding.GetPreamble(), .. updated];
        result[key] = Convert.ToBase64String(updated);
    }

    private static string Append(string text, string addition) => text.TrimEnd('\r', '\n') + "\n\n" + addition + "\n";

    public static string SetYamlProperties(string text, Dictionary<string, string> pairs, char delimiter = ':')
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Any(l => l.Length > 0 && !char.IsWhiteSpace(l[0]) &&
            (l.StartsWith('{') || l.StartsWith('[') || l.StartsWith('%') || l.StartsWith("---") || l.StartsWith("..."))))
            throw new InvalidOperationException("ساختار پیشرفته این فایل YAML برای ویرایش مستقیم پشتیبانی نمی‌شود. از پروفایل اجرای برنامه استفاده کنید.");
        foreach (var key in pairs.Keys)
        {
            var pattern = "^(?:" + Regex.Escape(key) + "|'" + Regex.Escape(key) + "'|\"" + Regex.Escape(key) + "\")[ \\t]*:[ \\t]*(.*)$";
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var match = Regex.Match(lines[i], pattern);
                if (!match.Success) continue;
                var value = match.Groups[1].Value.Trim();
                var following = lines.Skip(i + 1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'));
                if (value.StartsWith('|') || value.StartsWith('>') || value.StartsWith('&') || value.StartsWith('*') ||
                    value.StartsWith('{') || value.StartsWith('[') ||
                    (following is { Length: > 0 } && char.IsWhiteSpace(following[0])) ||
                    (value.StartsWith('"') && !Regex.IsMatch(value, "^\"(?:[^\"\\\\]|\\\\.)*\"[ \\t]*(?:#.*)?$")) ||
                    (value.StartsWith('\'') && !Regex.IsMatch(value, "^'(?:[^']|'')*'[ \\t]*(?:#.*)?$")))
                    throw new InvalidOperationException("مقدار چندخطی یا پیچیده پروکسی در YAML پشتیبانی نمی‌شود. فایل بدون تغییر باقی ماند.");
                lines.RemoveAt(i);
            }
        }
        return SetProperties(string.Join("\n", lines), pairs, delimiter);
    }

    public static string SetProperties(string text, Dictionary<string, string> pairs, char delimiter)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        foreach (var pair in pairs)
        {
            // YAML keys must be at root. A nested key is unrelated.
            var whitespace = delimiter == ':' ? "" : "[ \\t]*";
            var pattern = "^" + whitespace + Regex.Escape(pair.Key) + "[ \\t]*" + Regex.Escape(delimiter.ToString());
            lines.RemoveAll(line => Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase));
            lines.Add(pair.Key + delimiter + (delimiter == ':' ? " " : "") + pair.Value);
        }
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }

    public static string SetIni(string text, string section, string key, string value)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) return Append(text, $"[{section}]\n{key} = {value}");
        var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (var i = end - 1; i > start; i--)
            if (Regex.IsMatch(lines[i], "^[ \\t]*" + Regex.Escape(key) + "[ \\t]*=", RegexOptions.IgnoreCase)) lines.RemoveAt(i);
        lines.Insert(start + 1, $"{key} = {value}");
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }
}
