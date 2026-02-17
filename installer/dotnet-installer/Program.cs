using System;
using System.Windows.Forms;

namespace ModProfileSwitcherInstaller
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Support silent install: /S or --silent
            bool silent = false;
            foreach (var a in args)
            {
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                    silent = true;
            }

            if (silent)
            {
                var installer = new InstallerEngine();
                installer.Install(
                    InstallerEngine.DefaultInstallDir,
                    createDesktopShortcut: true,
                    launchOnFinish: false);
                return;
            }

            Application.Run(new InstallerForm());
        }
    }
}
