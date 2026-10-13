using iRacingSimulator;
using System;
using System.Windows.Input;


namespace iRacingReplayDirector
{
	public class ApplicationQuitCommand : ICommand
	{
		// Execute() is reachable both from the menu and from MainWindow.Window_Closing
		// via ReplayDirectorVM.ApplicationClosing. Guard against running the shutdown
		// sequence twice.
		private bool _isQuitting;

		public ReplayDirectorVM ReplayDirectorVM { get; set; }

		public event EventHandler CanExecuteChanged
		{
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}


		public ApplicationQuitCommand(ReplayDirectorVM vm)
		{
			ReplayDirectorVM = vm;
		}

		public bool CanExecute(object parameter)
		{
			return true;
		}

		public void Execute(object parameter)
		{
			if (_isQuitting)
				return;

			_isQuitting = true;

			// Stop playback and any active recording, then persist settings, BEFORE
			// shutting the application down. Environment.Exit(0) terminates the process
			// immediately, so anything run after it would be lost.
			try { Sim.Instance?.Sdk?.Replay?.SetPlaybackSpeed(0); }
			catch { /* SDK may already be gone during shutdown */ }

			try { if (ReplayDirectorVM.IsCaptureActive()) ReplayDirectorVM.StopRecording(); }
			catch { }

			try { SaveLoadHelper.SaveSettings(ReplayDirectorVM); }
			catch { }

			try { App.Current.Shutdown(); }
			catch { }

			Environment.Exit(0);
		}
	}
}
