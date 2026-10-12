using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace iRacingReplayDirector
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private static readonly object LogLock = new object();
		private static DateTime _lastLoggedUtc = DateTime.MinValue;
		private static string _lastSignature;
		private static bool _knownWrapperErrorLogged;

		// Known-benign exceptions thrown intermittently inside the closed-source
		// iRacingSimulator.dll when handling certain replay/session states. They are
		// outside this app's control; we keep the app alive and log the first one per
		// session instead of flooding the log or spamming dialogs.
		private static readonly string[] KnownWrapperSignatures =
		{
			"iRacingSimulator.TimeDelta.Update",                // IndexOutOfRange during telemetry updates
			"iRacingSimulator.TrackConditions.TrackUsageFromString", // NullReference during session updates
		};

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			// Catch crashes that would otherwise silently close the app.
			DispatcherUnhandledException += OnDispatcherUnhandledException;
			AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		}

		private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			e.Handled = true; // Keep the app alive so the user can see the error and save work.

			// The telemetry/session wrapper throws these repeatedly; logging/alerting each time
			// would flood the disk and the screen, so treat them as known, handled conditions
			// and record only the first occurrence per session.
			if (IsKnownWrapperError(e.Exception))
			{
				if (!_knownWrapperErrorLogged)
				{
					_knownWrapperErrorLogged = true;
					LogException("iRacing wrapper error (handled, logged once)", e.Exception, silent: true);
				}

				// The wrapper threw before re-raising its SessionInfoUpdated/TelemetryUpdated
				// events, so the app missed them. Re-drive the app's own processing directly.
				RecoverFromWrapperError();
				return;
			}

			LogException("DispatcherUnhandledException", e.Exception);
			ShowError(e.Exception);
		}

		private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			var ex = e.ExceptionObject as Exception;
			LogException("AppDomainUnhandledException", ex);
		}

		private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
		{
			LogException("UnobservedTaskException", e.Exception);
			e.SetObserved();
		}

		private static bool IsKnownWrapperError(Exception exception)
		{
			string stack = exception?.StackTrace;
			if (string.IsNullOrEmpty(stack))
				return false;

			foreach (var signature in KnownWrapperSignatures)
			{
				if (stack.Contains(signature))
					return true;
			}

			return false;
		}

		private static void RecoverFromWrapperError()
		{
			try
			{
				var vm = (Current?.MainWindow as MainWindow)?.DataContext as ReplayDirectorVM;
				vm?.RecoverFromWrapperError();
			}
			catch
			{
				// Recovery is best-effort; never let it mask the original issue.
			}
		}

		/// <summary>
		/// Append a plain diagnostic line to crash.log. Used by the recovery path to
		/// reveal the state of the ViewModel when the wrapper swallows events.
		/// </summary>
		public static void LogDiagnostic(string message)
		{
			try
			{
				lock (LogLock)
				{
					string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
					string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
					File.AppendAllText(logPath, entry);
				}
			}
			catch
			{
				// Never let logging itself bring down the app.
			}
		}

		private static void LogException(string source, Exception exception, bool silent = false)
		{
			string signature = GetSignature(exception);

			lock (LogLock)
			{
				// Throttle repeated identical entries to at most one per second.
				if (signature == _lastSignature && (DateTime.UtcNow - _lastLoggedUtc) < TimeSpan.FromSeconds(1))
					return;

				_lastSignature = signature;
				_lastLoggedUtc = DateTime.UtcNow;

				try
				{
					string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
					string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
					File.AppendAllText(logPath, entry);
				}
				catch
				{
					// Never let logging itself bring down the app.
				}
			}

			if (!silent)
			{
				ShowError(exception);
			}
		}

		private static string GetSignature(Exception exception)
		{
			if (exception == null)
				return "null";

			string stack = exception.StackTrace ?? string.Empty;
			int newline = stack.IndexOf(Environment.NewLine, StringComparison.Ordinal);
			string firstFrame = newline >= 0 ? stack.Substring(0, newline) : stack;
			return exception.GetType().FullName + "|" + firstFrame;
		}

		private static void ShowError(Exception exception)
		{
			try
			{
				MessageBox.Show(
					$"An unexpected error occurred:{Environment.NewLine}{Environment.NewLine}{exception?.Message}{Environment.NewLine}{Environment.NewLine}Details were written to crash.log.",
					"iRacing Sequence Director",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
			catch { }
		}
	}
}
