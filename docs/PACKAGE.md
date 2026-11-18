# ProxyGAyer — Quick guide

Copyleft by Crazy

## Run

Use Windows 10/11 with the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Extract the complete ZIP and run `ProxyGAyer.exe`. Keep all the included files together.

1. Start an existing HTTP proxy.
2. Choose a tool and enter the proxy's actual host and port.
3. Use **Test connection**, then **Set proxy**.
4. For a launch profile, use **Open terminal** or **Launch application**.
5. For multiple tools, open **Batch set**, check your tools, and use **Set selected**.

**Reset** restores the configuration before the first Set. **Reset All** processes configured sections separately. Neither action reconfigures processes already running.

Git settings apply to that user's Git repositories. User environment and Windows settings have a shared scope. WinHTTP changes require Administrator access. Check the scope shown in the app and the individual batch results.

The app configures proxy-aware tools; it does not provide a VPN, TUN, or proxy server. The current UI supports HTTP proxies without username/password authentication. SOCKS and Git SSH remotes are outside its current coverage.

Profiles and backups are stored in `%LOCALAPPDATA%/ProxyGAyer/state.json`. This file can include credentials from backed-up configuration files; keep it private and retain it until any needed Reset is complete.

## راهنمای سریع

روی ویندوز ۱۰ یا ۱۱، **.NET 10 Desktop Runtime** را نصب کن. کل ZIP را استخراج کن و فایل `ProxyGAyer.exe` را اجرا کن؛ همهٔ فایل‌های بسته باید کنار هم بمانند.

پروکسی HTTP خودت را روشن کن، ابزار را انتخاب کن، آدرس و پورت را بده و بعد از بررسی اتصال **Set proxy** را بزن. برای پروفایل‌های اجرا از **Open terminal** یا **Launch application** استفاده کن. برای چند ابزار، **اعمال گروهی** را باز کن و تیکشان را بزن.

**Reset** وضعیت قبل از اولین Set را برمی‌گرداند. تنظیم Git برای همهٔ مخزن‌های کاربر است؛ بخش‌های محیط کاربر و ویندوز دامنهٔ مشترک دارند. WinHTTP به Administrator نیاز دارد. بازگردانی، پنجره‌ها و برنامه‌های قبلاً بازشده را تغییر نمی‌دهد.

فایل `state.json` را خصوصی نگه دار؛ ممکن است اطلاعات محرمانهٔ فایل‌های پشتیبان در آن باشد. برای توضیح کامل، اسکچ‌ها و رفع مشکل به README مخزن پروژه مراجعه کن.
