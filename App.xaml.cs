﻿using System;
using System.Threading.Tasks;
using System.Windows;
using NLog;

namespace TwitterNotifier
{
	public partial class App : Application
	{
		#region Data Members

		private readonly Logger _logger = LogManager.GetCurrentClassLogger();

		#endregion

		#region Overrides

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			SetupExceptionHandling();
		}

		#endregion

		#region Methods

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
			_logger.Error(exception, source);
		}

		#endregion
	}
}
