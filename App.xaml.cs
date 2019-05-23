using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TwitterNotifier
{
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			SetupExceptionHandling();
		}

		private void SetupExceptionHandling()
		{
			AppDomain.CurrentDomain.UnhandledException += (s, e) =>
				LogUnhandledException((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");

			DispatcherUnhandledException += (s, e) =>
				LogUnhandledException(e.Exception, "Application.Current.DispatcherUnhandledException");

			TaskScheduler.UnobservedTaskException += (s, e) =>
				LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
		}

		private void LogUnhandledException(Exception exception, string source)
		{
			File.AppendAllLines("error.log", new string[]
			{
				$"{DateTime.Now.ToString("MM/dd/yyyy @ HH:mm:ss")} - Unhandled exception ({source}) - ({exception.Message})",
				exception.StackTrace
			});
		}
	}
}
