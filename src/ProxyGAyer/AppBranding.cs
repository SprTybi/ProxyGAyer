namespace ProxyGAyer;

internal static class AppBranding
{
    public static Icon Icon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(AppBranding).Assembly.GetManifestResourceStream("ProxyGAyer.AppIcon")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using var embedded = new Icon(stream);
        return (Icon)embedded.Clone();
    }
}
