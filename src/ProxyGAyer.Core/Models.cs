using System.Net;
using System.Text.RegularExpressions;

namespace ProxyGAyer.Core;

public sealed record ProxyEndpoint(string Host, int Port)
{
    public string Authority => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
    // This is the transport TO the proxy, for both HTTP and HTTPS destinations.
    public string Url => "http://" + Authority;

    public static ProxyEndpoint Parse(string host, string port)
    {
        host = host.Trim().Trim('[', ']');
        port = new string(port.Trim().Select(c => c is >= '\u06f0' and <= '\u06f9' ? (char)('0' + c - '\u06f0')
            : c is >= '\u0660' and <= '\u0669' ? (char)('0' + c - '\u0660') : c).ToArray());
        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || host.Contains('%') ||
            (Uri.CheckHostName(host) == UriHostNameType.Unknown && !IPAddress.TryParse(host, out _)))
            throw new ArgumentException("آدرس پروکسی باید یک IP یا نام میزبان معتبر باشد؛ بدون http:// یا مسیر.");
        if (!Regex.IsMatch(port, "^[0-9]{1,5}$") || !int.TryParse(port, out var number) || number is < 1 or > 65535)
            throw new ArgumentException("پورت باید عددی بین 1 و 65535 باشد.");
        if (!IPAddress.TryParse(host, out _)) host = new System.Globalization.IdnMapping().GetAscii(host);
        return new(host, number);
    }
}

public sealed record ProxyProfile(ProxyEndpoint Http, ProxyEndpoint Https, bool SameForHttps = true, string Bypass = "localhost,127.0.0.1,::1")
{
    public static ProxyProfile Default => new(new("127.0.0.1", 7890), new("127.0.0.1", 7890));
    public void Validate()
    {
        _ = ProxyEndpoint.Parse(Http.Host, Http.Port.ToString());
        _ = ProxyEndpoint.Parse(Https.Host, Https.Port.ToString());
        if (Bypass.Length > 2048 || Bypass.Any(c => char.IsControl(c) || c is '"' or '\'' or '\\'))
            throw new ArgumentException("فهرست استثناها نامعتبر است.");
    }
}

public enum AdapterKind { Session, Git, Npm, Yarn, Pip, Gradle, UserEnvironment, WindowsInternet, WinHttp }

public sealed record ServiceDefinition(string Id, string Name, string Group, AdapterKind Kind, string Description,
    string? Executable = null, bool SingleProxy = false)
{
    public bool IsSession => Kind == AdapterKind.Session;
    [System.Text.Json.Serialization.JsonIgnore]
    public string ScopeLabel => Kind switch
    {
        AdapterKind.Session => "فقط اجرا از داخل برنامه",
        AdapterKind.Git => "همه مخزن‌های Git این کاربر",
        AdapterKind.Npm => "تنظیم کاربر؛ مشترک npm/pnpm",
        AdapterKind.UserEnvironment => "مشترک بین ابزارهای پشتیبان",
        AdapterKind.WindowsInternet => "پروکسی اینترنت کاربر ویندوز",
        AdapterKind.WinHttp => "پیش‌فرض دستگاه؛ نیازمند Admin",
        _ => "تنظیم کاربرِ همین ابزار"
    };
    [System.Text.Json.Serialization.JsonIgnore]
    public string ScopeTag => Kind switch
    {
        AdapterKind.Session => "SESSION ONLY",
        AdapterKind.Git => "GIT USER · ALL REPOS",
        AdapterKind.UserEnvironment => "SHARED · USER ENV",
        AdapterKind.WindowsInternet => "SHARED · WINDOWS USER",
        AdapterKind.WinHttp => "MACHINE · ADMIN",
        _ => "TOOL USER SETTINGS"
    };
    public override string ToString() => Name;
}

public static class ServiceCatalog
{
    public static IReadOnlyList<ServiceDefinition> All { get; } =
    [
        new("cmd", "CMD", "TERMINALS", AdapterKind.Session, "پروفایل مستقل برای CMD. بعد از Set، ترمینال را از همین برنامه باز کنید. فقط ابزارهایی که متغیرهای پروکسی را پشتیبانی می‌کنند از آن استفاده می‌کنند.", "cmd.exe"),
        new("powershell", "PowerShell 7", "TERMINALS", AdapterKind.Session, "پروفایل مستقل برای PowerShell 7 و برنامه‌های داخل آن. پنجره‌های قبلی تغییر نمی‌کنند.", "pwsh.exe"),
        new("windows-powershell", "Windows PowerShell", "TERMINALS", AdapterKind.Session, "پروفایل مستقل برای Windows PowerShell 5.1. پروکسی پیش‌فرض .NET هم در همین نشست تنظیم می‌شود؛ یک پروکسی برای هر دو نوع مقصد.", "powershell.exe", true),
        new("git", "Git", "DEVELOPER TOOLS", AdapterKind.Git, "تنظیم پایدار http.proxy در فایل سراسری Git؛ برای هر دو نوع مقصد HTTP و HTTPS. SSH پوشش داده نمی‌شود. تنظیمات پروژه و URLهای خاص می‌توانند اولویت بیشتری داشته باشند.", "git.exe", true),
        new("npm", "npm / pnpm", "DEVELOPER TOOLS", AdapterKind.Npm, "تنظیم proxy و https-proxy در فایل کاربر .npmrc. این فایل بین npm و نسخه‌های سازگار pnpm مشترک است. تنظیمات پروژه می‌توانند اولویت بیشتری داشته باشند.", "npm.cmd"),
        new("yarn", "Yarn 2+", "DEVELOPER TOOLS", AdapterKind.Yarn, "تنظیم httpProxy و httpsProxy در فایل کاربر .yarnrc.yml برای Yarn مدرن؛ تنظیمات پروژه ممکن است آن را بازنویسی کنند.", "yarn.cmd"),
        new("pip", "Python / pip", "DEVELOPER TOOLS", AdapterKind.Pip, "تنظیم global.proxy در pip.ini کاربر. یک پروکسی برای همه مقصدها. محیط مجازی، PIP_CONFIG_FILE و گزینه‌های خط فرمان می‌توانند اولویت بیشتری داشته باشند.", "python.exe", true),
        new("gradle", "Gradle", "DEVELOPER TOOLS", AdapterKind.Gradle, "تنظیم پروکسی HTTP و HTTPS در gradle.properties کاربر. برای نتیجه مطمئن، daemonهای قبلی Gradle را مجدداً راه‌اندازی کنید.", "gradle.bat"),
        new("cargo", "Rust / Cargo", "LAUNCH PROFILES", AdapterKind.Session, "ترمینال مخصوص Cargo با CARGO_HTTP_PROXY و متغیرهای استاندارد. یک پروکسی برای هر دو نوع مقصد؛ فایل‌های Cargo تغییر نمی‌کنند.", "cmd.exe", true),
        new("rustup", "Rustup", "LAUNCH PROFILES", AdapterKind.Session, "ترمینال با HTTP_PROXY و HTTPS_PROXY برای Rustup. دستور rustup را در ترمینال بازشده اجرا کنید.", "cmd.exe"),
        new("curl", "curl / wget", "LAUNCH PROFILES", AdapterKind.Session, "ترمینال با http_proxy و https_proxy برای curl و wget. تنظیمات صریح خط فرمان یا فایل ابزار ممکن است اولویت بیشتری داشته باشند.", "cmd.exe"),
        new("custom", "Custom application", "LAUNCH PROFILES", AdapterKind.Session, "یک فایل اجرایی دلخواه را با متغیرهای پروکسی اجرا کنید. فقط برنامه‌های پشتیبان HTTP_PROXY و HTTPS_PROXY پوشش داده می‌شوند."),
        new("environment", "User environment", "WINDOWS", AdapterKind.UserEnvironment, "متغیرهای پایدار کاربر برای ابزارهای پشتیبان پروکسی. بین CMD و PowerShell و دیگر برنامه‌ها مشترک است. برنامه‌ها یا ترمینال‌های قبلی باید بسته و دوباره باز شوند؛ گاهی ورود مجدد به ویندوز لازم است."),
        new("windows", "Windows / browsers", "WINDOWS", AdapterKind.WindowsInternet, "پروکسی دستی کاربر ویندوز برای برنامه‌هایی که تنظیمات اینترنت سیستم را رعایت می‌کنند، مانند Edge و Chrome. PAC، تنظیمات سازمانی و تنظیمات اختصاصی برنامه ممکن است اولویت داشته باشند."),
        new("winhttp", "WinHTTP services", "WINDOWS", AdapterKind.WinHttp, "پروکسی پیش‌فرض WinHTTP در سطح دستگاه. برای سرویس‌هایی که از این پیش‌فرض استفاده می‌کنند؛ نیازمند اجرای برنامه با Administrator است. تنظیمات پیشرفته و اختصاصی سرویس‌ها جدا هستند.")
    ];
}

public sealed class AppState
{
    public int Version { get; set; } = 1;
    public List<ServiceDefinition> CustomServices { get; set; } = [];
    public Dictionary<string, ProxyProfile> Profiles { get; set; } = [];
    public Dictionary<string, ChangeSet> Changes { get; set; } = [];
}

public sealed class ChangeSet
{
    public bool Pending { get; set; }
    public List<ResourceChange> Resources { get; set; } = [];
}

public sealed record ResourceChange(string Key, string? Before, string? After, string? Previous = null);
