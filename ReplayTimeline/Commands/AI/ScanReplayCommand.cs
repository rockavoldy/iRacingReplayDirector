using iRacingReplayDirector.AI.Models;
using System;
using System.Media;
using System.Windows;
using System.Windows.Input;

namespace iRacingReplayDirector
{
	public class ScanReplayCommand : ICommand
	{
		public ReplayDirectorVM ReplayDirectorVM { get; set; }

		public event EventHandler CanExecuteChanged
		{
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}

		public ScanReplayCommand(ReplayDirectorVM vm)
		{
			ReplayDirectorVM = vm;
		}

		public bool CanExecute(object parameter)
		{
			try
			{
				if (!ReplayDirectorVM.IsSessionReady())
					return false;

				if (ReplayDirectorVM.AIDirector == null)
					return false;

				if (ReplayDirectorVM.AIDirector.IsBusy)
					return false;

				if (ReplayDirectorVM.FinalFrame <= 0)
					return false;

				return true;
			}
			catch
			{
				return false;
			}
		}

		public async void Execute(object parameter)
		{
			try
			{
				int startFrame = ReplayDirectorVM.CurrentFrame;
				int endFrame = ReplayDirectorVM.FinalFrame;

				if (endFrame <= startFrame)
				{
					MessageBox.Show(
						"Cannot scan: Invalid frame range. Make sure a replay is loaded.",
						"Scan Error",
						MessageBoxButton.OK,
						MessageBoxImage.Warning);
					return;
				}

				var confirmation = MessageBox.Show(
					$"Scan replay from frame {startFrame} to {endFrame}?\n\nThis will take some time and the replay will jump through frames.",
					"Confirm Scan",
					MessageBoxButton.YesNo,
					MessageBoxImage.Question);

				if (confirmation != MessageBoxResult.Yes)
					return;

				var aiDirector = ReplayDirectorVM.AIDirector;
				if (aiDirector == null)
					return;

				// Run the scan via the AIDirector's async path. It steps through frames
				// using awaited delays and dispatcher calls, so the UI thread is not
				// blocked for the duration of the scan (unlike the previous DoEvents loop).
				ReplayScanResult scanResult = await aiDirector.ScanReplayAsync(startFrame, endFrame);

				if (scanResult == null)
				{
					string errorMsg = aiDirector.StatusMessage ?? "Scan failed";
					if (errorMsg.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						MessageBox.Show(
							errorMsg,
							"Scan Error",
							MessageBoxButton.OK,
							MessageBoxImage.Error);
					}
					return;
				}

				// Play completion chime
				SystemSounds.Asterisk.Play();

				MessageBox.Show(
					$"Scan complete!\n\nDetected {scanResult.Events.Count} events.\n\nYou can now generate a camera plan.",
					"Scan Complete",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				try { ReplayDirectorVM.AIDirector?.ClearResults(); } catch { }

				MessageBox.Show(
					$"Error scanning replay: {ex.Message}",
					"Scan Error",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}
	}
}
