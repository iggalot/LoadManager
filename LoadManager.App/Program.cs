using System;
using System.IO;
using System.Windows;
using BuildingModel.Diagnostics;

namespace LoadManager.App
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // the log: %AppData%\LoadManager\logs until a project is open, then the project's logs folder
            Log.Start("LoadManager", "loads");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log.Error("App", "Unhandled error (the program is closing)" + (e.IsTerminating ? "" : " (not terminating)"), e.ExceptionObject as Exception);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error("App", "Unexpected error; the action was cancelled", e.Exception);
                MessageBox.Show("Something went wrong and that action was cancelled, but your work is still open.\n\n" + e.Exception.Message +
                                "\n\nThe details are in the log:\n" + Log.TodayFile, "Unexpected problem", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            app.Exit += (s, e) => Log.Info("App", "Load Manager closed.");
            var w = new MainWindow();
            if (args.Length > 0)
            {
                Log.Info("App", "Started with " + args[0]);
                // the hub's "--page foundation": the step's page, once the project is open
                int at = Array.FindIndex(args, a => string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase));
                string page = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
                // and "--select W4": the member, selected and in view (as in every app: the hub's "Show W4")
                int sel = Array.FindIndex(args, a => string.Equals(a, "--select", StringComparison.OrdinalIgnoreCase));
                string select = sel >= 0 && sel + 1 < args.Length ? args[sel + 1] : null;
                if (File.Exists(args[0])) w.Loaded += (s, e) =>
                {
                    w.Open(args[0]);
                    if (page != null && w.HasProject) w.ShowPage(page);
                    if (select != null && w.HasProject) w.Dispatcher.BeginInvoke(new Action(() => w.SelectMember(select)), System.Windows.Threading.DispatcherPriority.Loaded);
                };
                else Log.Warning("App", "The file to open does not exist: " + args[0], null);
            }
            return app.Run(w);
        }
    }
}
