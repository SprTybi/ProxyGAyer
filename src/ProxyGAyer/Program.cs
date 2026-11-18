using ProxyGAyer.Core;

namespace ProxyGAyer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Preview mode only uses a supplied workspace directory and never changes
        // external proxy configuration or the normal application's saved state.
        var preview = args.Length == 2 && args[0] == "--preview";
        using var mutex = new Mutex(true, @"Local\ProxyGAyer-Desktop-v1", out var acquired);
        if (!acquired)
        {
            MessageBox.Show("ProxyGAyer در حال اجراست.", "ProxyGAyer");
            return 1;
        }
        try
        {
            var directory = preview ? Path.Combine(Path.GetFullPath(args[1]), "preview-data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyGAyer");
            var store = new StateStore(directory);
            var state = store.Load();
            IResourceStore resources = preview ? new PreviewResources() : new WindowsResources();
            var manager = new ChangeManager(store, resources, state);
            var planner = new ConfigPlanner(resources, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            using var window = new MainForm(manager, planner, store);
            if (preview)
            {
                Directory.CreateDirectory(Path.GetFullPath(args[1]));
                window.Opacity = 0;
                window.Show();
                Application.DoEvents();
                window.ExportPreviews(Path.GetFullPath(args[1]));
                window.Close();
                return 0;
            }
            Application.Run(window);
            return 0;
        }
        catch (Exception exception)
        {
            if (preview) throw;
            MessageBox.Show(exception.Message, "ProxyGAyer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private sealed class PreviewResources : IResourceStore
    {
        private readonly Dictionary<string, string?> values = [];
        public string? Read(string key) => values.GetValueOrDefault(key);
        public void Write(string key, string? value)
        {
            if (value is null) values.Remove(key);
            else values[key] = value;
        }
    }
}
