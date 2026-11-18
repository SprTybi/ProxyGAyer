using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ProxyGAyer.Core;

public sealed record RegistryData(RegistryValueKind Kind, string? Text = null, int Number = 0);
public sealed record WinHttpData(int AccessType, string? Proxy, string? Bypass);

public sealed class WindowsResources : IResourceStore
{
    public string? Read(string key)
    {
        if (key.StartsWith("file|"))
        {
            var path = key[5..];
            if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("فایل تنظیمات یک پیوند نمادین است. برای حفظ پیوند، ویرایش مستقیم آن پشتیبانی نمی‌شود.");
            return File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;
        }
        if (key.StartsWith("reg|"))
        {
            var parts = key.Split('|');
            using var registry = Registry.CurrentUser.OpenSubKey(parts[1]);
            if (registry is null || !registry.GetValueNames().Contains(parts[2], StringComparer.OrdinalIgnoreCase)) return null;
            var kind = registry.GetValueKind(parts[2]);
            var value = registry.GetValue(parts[2], null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return kind switch
            {
                RegistryValueKind.DWord => JsonSerializer.Serialize(new RegistryData(kind, Number: (int)value!)),
                RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Serialize(new RegistryData(kind, (string?)value)),
                _ => throw new InvalidOperationException("نوع مقدار رجیستری پشتیبانی نمی‌شود؛ هیچ تغییری اعمال نشد.")
            };
        }
        if (key == "winhttp") return JsonSerializer.Serialize(Native.GetWinHttp());
        throw new ArgumentException("Unknown resource: " + key);
    }

    public void Write(string key, string? value)
    {
        if (key.StartsWith("file|"))
        {
            var path = key[5..];
            if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("فایل تنظیمات به پیوند نمادین تبدیل شده است؛ عملیات متوقف شد.");
            if (value is null) { File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".proxygayer-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, Convert.FromBase64String(value));
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return;
        }
        if (key.StartsWith("reg|"))
        {
            var parts = key.Split('|');
            using var registry = Registry.CurrentUser.CreateSubKey(parts[1]);
            if (value is null) registry.DeleteValue(parts[2], false);
            else
            {
                var data = JsonSerializer.Deserialize<RegistryData>(value)!;
                registry.SetValue(parts[2], data.Kind == RegistryValueKind.DWord ? data.Number : data.Text!, data.Kind);
            }
            if (parts[1] == "Environment") Native.BroadcastEnvironment();
            else Native.RefreshInternet();
            return;
        }
        if (key == "winhttp")
        {
            Native.SetWinHttp(JsonSerializer.Deserialize<WinHttpData>(value!)!);
            return;
        }
        throw new ArgumentException("Unknown resource: " + key);
    }

    public static string StringValue(string value) => JsonSerializer.Serialize(new RegistryData(RegistryValueKind.String, value));
    public static string DwordValue(int value) => JsonSerializer.Serialize(new RegistryData(RegistryValueKind.DWord, Number: value));
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProxyInfo { public int AccessType; public IntPtr Proxy; public IntPtr Bypass; }
    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpGetDefaultProxyConfiguration(out ProxyInfo info);
    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpSetDefaultProxyConfiguration(ref ProxyInfo info);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr pointer);
    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr parameter, string value, uint flags, uint timeout, out IntPtr result);

    public static WinHttpData GetWinHttp()
    {
        if (!WinHttpGetDefaultProxyConfiguration(out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { return new(info.AccessType, Marshal.PtrToStringUni(info.Proxy), Marshal.PtrToStringUni(info.Bypass)); }
        finally { GlobalFree(info.Proxy); GlobalFree(info.Bypass); }
    }

    public static void SetWinHttp(WinHttpData data)
    {
        var info = new ProxyInfo { AccessType = data.AccessType,
            Proxy = data.Proxy is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(data.Proxy),
            Bypass = data.Bypass is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(data.Bypass) };
        try
        {
            if (!WinHttpSetDefaultProxyConfiguration(ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "تنظیم WinHTTP ناموفق بود. برنامه را با Run as administrator اجرا کنید.");
        }
        finally { Marshal.FreeHGlobal(info.Proxy); Marshal.FreeHGlobal(info.Bypass); }
    }

    public static void RefreshInternet()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }
    public static void BroadcastEnvironment() => SendMessageTimeout(new IntPtr(0xffff), 0x001a,
        IntPtr.Zero, "Environment", 2, 1000, out _);
}
