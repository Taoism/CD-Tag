using System;
using System.Windows;
using System.Windows.Threading;
using CDTag.Common;
using CDTag.Common.ApplicationServices;

namespace CDTag
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            // Every Regex in the process without its own timeout gets this one (before any is created),
            // so a pathological file name, tag or log can't hang the UI.
            AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(CDTag.Common.RegexTimeouts.Milliseconds));
        }

        public App()
        {
            // Note: Known bug with App.xaml having no StartupUri and the theme speicified in XAML's Application.Resources
            Resources = (ResourceDictionary)LoadComponent(new Uri("/Themes/Default/Theme.xaml", UriKind.RelativeOrAbsolute));

            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var bootstrapper = new Bootstrapper();
            bootstrapper.Run();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            IoC.Resolve<IDialogService>().ShowError(e.Exception);
            e.Handled = true;
        }
    }
}
