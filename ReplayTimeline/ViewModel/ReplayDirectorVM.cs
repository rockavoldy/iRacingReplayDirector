using iRacingReplayDirector.AI.Director;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using iRacingSdkWrapper;
using iRacingSimulator;


namespace iRacingReplayDirector
{
	public partial class ReplayDirectorVM : INotifyPropertyChanged
	{
		public ReplayDirectorVM()
		{
			InitSDK();
			InitVariables();

			StatusBarText = "iRacing Not Connected.";
			CamChangeBtnText = "Add Cam Change";
			RecordBtnText = "Record";

			SaveLoadHelper.LoadSettings(this);

			ToggleInactiveDriverDisplay();
		}

		private void InitSDK()
		{
			Sim.Instance.Connected += SdkConnectedCallback;
			Sim.Instance.Disconnected += SdkDisconnectedCallback;
			Sim.Instance.SessionInfoUpdated += SessionInfoUpdatedCallback;
			Sim.Instance.TelemetryUpdated += TelemetryUpdatedCallback;

			Sim.Instance.Start();
		}

		// Number of diagnostic lines logged; kept small so a live run reveals why the UI
		// stays disabled without flooding crash.log.
		private int m_DiagnosticsLogged;

		private void InitVariables()
		{
			NodeCollection = new NodeCollection();
			TimelineNodesView = CollectionViewSource.GetDefaultView(NodeCollection.Nodes);
			Drivers = new ObservableCollection<Driver>();
			DriversView = CollectionViewSource.GetDefaultView(Drivers);
			Cameras = new ObservableCollection<Camera>();

			TimelineNodesView.SortDescriptions.Clear();
			SortDescription frameSort = new SortDescription("Frame", ListSortDirection.Ascending);
			TimelineNodesView.SortDescriptions.Add(frameSort);
			SortDescription idSort = new SortDescription("NumberRaw", ListSortDirection.Ascending);
			DriversView.SortDescriptions.Add(idSort);
			SortDriversById = true;

			CaptureModes = new ObservableCollection<CaptureModeBase>() { new CaptureMode_None(), new CaptureMode_Iracing(), new CaptureMode_OBS(), new CaptureMode_ShadowPlay() };

			CamChangeNodeCommand = new CamChangeNodeCommand(this);
			FrameSkipNodeCommand = new FrameSkipNodeCommand(this);
			ClearAllNodesCommand = new ClearAllNodesCommand(this);
			NextStoredFrameCommand = new NextStoredFrameCommand(this);
			PreviousStoredFrameCommand = new PreviousStoredFrameCommand(this);
			DeleteStoredFrameCommand = new DeleteStoredFrameCommand(this);
			PlayPauseCommand = new PlayPauseCommand(this);
			RewindCommand = new RewindCommand(this);
			FastForwardCommand = new FastForwardCommand(this);
			SlowMotionCommand = new SlowMotionCommand(this);
			SkipFrameBackCommand = new SkipFrameBackCommand(this);
			SkipFrameForwardCommand = new SkipFrameForwardCommand(this);
			NextLapCommand = new NextLapCommand(this);
			PreviousLapCommand = new PreviousLapCommand(this);
			NextSessionCommand = new NextSessionCommand(this);
			PreviousSessionCommand = new PreviousSessionCommand(this);
			NextDriverCommand = new NextDriverCommand(this);
			PreviousDriverCommand = new PreviousDriverCommand(this);
			ToggleDriverSortOptionCommand = new ToggleDriverSortOptionCommand(this);
			ToggleRecordingCommand = new ToggleRecordingCommand(this);
			ToggleShowInactiveDriversCommand = new ToggleShowInactiveDriversCommand(this);

			ManualFrameEntryCommand = new ManualFrameEntryCommand(this);

			ToggleInSimUICommand = new ToggleInSimUICommand(this);
			ToggleSimUIOnPlaybackCommand = new ToggleSimUIOnPlaybackCommand(this);
			ToggleSimUIOnRecordingCommand = new ToggleSimUIOnRecordingCommand(this);
			ToggleRecordingOnFinalNodeCommand = new ToggleRecordingOnFinalNodeCommand(this);

			ApplicationQuitCommand = new ApplicationQuitCommand(this);
			ResetAppSettingsCommand = new ResetAppSettingsCommand(this);
			OpenURLCommand = new OpenURLCommand(this);
			AboutCommand = new AboutCommand(this);

			// Initialize AI Director
			AIDirector = new AIDirector(this);
			ScanReplayCommand = new ScanReplayCommand(this);
			GenerateCameraPlanCommand = new GenerateCameraPlanCommand(this);
			ApplyAIPlanCommand = new ApplyAIPlanCommand(this);
			ClearAIResultsCommand = new ClearAIResultsCommand(this);
			OpenAISettingsCommand = new OpenAISettingsCommand(this);
		}

		public void ApplicationClosing(Size windowSize)
		{
			Sim.Instance.Connected -= SdkConnectedCallback;
			Sim.Instance.Disconnected -= SdkDisconnectedCallback;
			Sim.Instance.SessionInfoUpdated -= SessionInfoUpdatedCallback;
			Sim.Instance.TelemetryUpdated -= TelemetryUpdatedCallback;

			Sim.Instance.Stop();

			ApplicationQuitCommand.Execute(this);
		}

		private void SdkConnectedCallback(object sender, EventArgs e)
		{
			StatusBarText = "iRacing Connected.";
		}

		private void SdkDisconnectedCallback(object sender, EventArgs e)
		{
			StatusBarText = "iRacing Disconnected.";

			// Unload session info, clear information
			SessionInfoLoaded = false;
			Drivers.Clear();
			Cameras.Clear();
			NodeCollection.Nodes.Clear();
			StatusBarSessionID = "No Session Loaded.";
			StatusBarCurrentSessionInfo = "";

			RefreshCommandStates();
		}

		/// <summary>
		/// Re-evaluate the CanExecute of menu commands (e.g. Auto Director's Scan/Generate/Apply)
		/// when session readiness changes. CommandManager.RequerySuggested alone does not fire
		/// when state changes from SDK callbacks, which leaves those menu items greyed out.
		/// </summary>
		private static void RefreshCommandStates()
		{
			try { System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
			catch { }
		}

		private bool DriverOnTrackFilter(object item)
		{
			Driver driver = item as Driver;

			if (driver != CurrentDriver)
			{
				_currentDriver = driver;
				OnPropertyChanged("CurrentDriver");
			}

			return driver.TrackSurface != TrackSurfaces.NotInWorld;
		}

		private void SessionInfoUpdatedCallback(object sender, SdkWrapper.SessionInfoUpdatedEventArgs e)
		{
			ProcessSessionInfo(e.SessionInfo);
		}

		/// <summary>
		/// Load/refresh session information from the SDK. Called from the normal
		/// SessionInfoUpdated event and, as a fallback, when the iRacingSimulator wrapper
		/// throws before re-raising that event (see App.OnDispatcherUnhandledException).
		/// </summary>
		public void ProcessSessionInfo(iRacingSdkWrapper.SessionInfo sessionInfo)
		{
			if (sessionInfo == null)
				sessionInfo = Sim.Instance.SessionInfo;

			if (sessionInfo == null)
				return;

			GetSessionDrivers();
			GetSessionCameras();
			VerifyExistingNodeCameras();

			YamlQuery weekendInfoQuery = sessionInfo["WeekendInfo"];
			SessionID = int.Parse(weekendInfoQuery["SubSessionID"].GetValue("-1"));
			StatusBarSessionID = $"Session ID: {SessionID}";

			if (!SessionInfoLoaded)
			{
				try
				{
					InitSession();
				}
				catch (Exception ex)
				{
					// Session ID is shown but the UI stays disabled when InitSession throws
					// before it sets SessionInfoLoaded. Record the real cause, then rethrow so
					// behaviour is otherwise unchanged.
					LogDiagnosticThrottled("InitSession failed: " + ex);
					throw;
				}
			}

			LogReadinessState();
			RefreshCommandStates();
		}

		/// <summary>
		/// Fallback invoked when the closed-source iRacingSimulator wrapper throws while
		/// handling session/telemetry updates, which prevents its re-raised events from
		/// reaching this app. Re-drives the app's own processing directly from Sim.Instance
		/// so drivers, cameras, the timeline and the Auto Director stay functional.
		/// </summary>
		public void RecoverFromWrapperError()
		{
			try { ProcessSessionInfo(Sim.Instance?.SessionInfo); }
			catch (Exception ex) { LogDiagnosticThrottled("Recover: ProcessSessionInfo failed: " + ex); }

			try { ProcessTelemetry(Sim.Instance?.Telemetry); }
			catch (Exception ex) { LogDiagnosticThrottled("Recover: ProcessTelemetry failed: " + ex); }

			RefreshCommandStates();
		}

		/// <summary>
		/// Log the state that decides whether the UI (Auto Director, Add Cam Change) is
		/// enabled, a few times per run, so a live session reveals the cause of a disabled UI.
		/// </summary>
		private void LogReadinessState()
		{
			LogDiagnosticThrottled(
				$"Readiness: SessionInfoLoaded={SessionInfoLoaded}, IsLiveSession={IsLiveSession}, " +
				$"SessionID={SessionID}, Drivers={Drivers.Count}, Cameras={Cameras.Count}, " +
				$"CurrentDriver={(CurrentDriver?.Name ?? "<null>")}, CurrentCamera={(CurrentCamera?.GroupName ?? "<null>")}, " +
				$"CurrentFrame={CurrentFrame}, FinalFrame={FinalFrame}, PlaybackEnabled={PlaybackEnabled}");
		}

		/// <summary>
		/// Write a diagnostic line to crash.log, but only a handful per run, so a repeating
		/// wrapper error cannot flood the log.
		/// </summary>
		private void LogDiagnosticThrottled(string message)
		{
			if (m_DiagnosticsLogged >= 5)
				return;

			m_DiagnosticsLogged++;
			App.LogDiagnostic(message);
		}

		private void GetSessionDrivers()
		{
			YamlQuery weekendOptionsQuery = Sim.Instance.SessionInfo["WeekendInfo"]["WeekendOptions"];
			int currentStarters = int.Parse(weekendOptionsQuery["NumStarters"].GetValue());

			var sessionDrivers = new List<Driver>();

			for (int i = 0; i < currentStarters+1; i++)
			{
				YamlQuery query = Sim.Instance.SessionInfo["DriverInfo"]["Drivers"]["CarIdx", i];
				Driver newDriver;

				string driverName = query["UserName"].GetValue("");

				if (!string.IsNullOrEmpty(driverName))
				{
					// Get driver if driver is in previous list
					newDriver = Drivers.FirstOrDefault(d => d.Name == driverName);

					// If not...
					if (newDriver == null)
					{
						// Populate driver info
						newDriver = new Driver()
						{
							Id = i,
							Name = driverName,
							CustomerId = int.Parse(query["UserID"].GetValue("0")), // default value 0
							Number = query["CarNumber"].GetValue("").TrimStart('\"').TrimEnd('\"'), // trim the quotes
							NumberRaw = int.Parse(query["CarNumberRaw"].GetValue("")),
							TeamName = query["TeamName"].GetValue("")
						};
					}

					// Add to drivers list
					sessionDrivers.Add(newDriver);
				}
			}

			Drivers.Clear();
			foreach (var newDriver in sessionDrivers)
			{
				Drivers.Add(newDriver);
			}
		}

		private void GetSessionCameras()
		{
			int id = 1;
			Camera newCam;

			var sessionCameras = new List<Camera>();

			// Loop through cameras until none are found anymore
			do
			{
				newCam = null;
				YamlQuery query = Sim.Instance.SessionInfo["CameraInfo"]["Groups"]["GroupNum", id];

				// Get Camera Group name
				string groupName = query["GroupName"].GetValue("");

				if (!string.IsNullOrEmpty(groupName))
				{
					// Get Camera if it is in previous list
					newCam = Cameras.FirstOrDefault(c => c.GroupName == groupName);

					// Create new camera if not in previous lsit
					if (newCam == null)
					{
						newCam = new Camera() { GroupName = groupName, GroupNum = id };
					}
					else
					{
						// If it does exist, update the group number to ensure it matches
						newCam.GroupNum = id;
					}

					sessionCameras.Add(newCam);

					id++;
				}
			}
			while (newCam != null);

			Cameras.Clear();
			foreach (var newCamera in sessionCameras)
			{
				Cameras.Add(newCamera);
			}
		}

		private void VerifyExistingNodeCameras()
		{
			foreach (var currentNode in NodeCollection.Nodes)
			{
				if (currentNode is CamChangeNode)
				{
					CamChangeNode node = currentNode as CamChangeNode;

					// Check for node camera in camera list, based on name
					var existingCameraInSession = Cameras.FirstOrDefault(c => c.GroupName == node.Camera.GroupName);

					if (existingCameraInSession == null)
					{
						// Disable node if it wasn't found
						node.Enabled = false;
					}
					else
					{
						// If a camera is found but the group number is different...
						if (node.Camera.GroupNum != existingCameraInSession.GroupNum)
						{
							// Set the camera group number to the one in the active camera liust
							node.Camera.GroupNum = existingCameraInSession.GroupNum;
						}
					}
				}
			}
		}

		private void InitSession()
		{
			if (!m_LiveSessionPopupVisible)
			{
				var simMode = Sim.Instance.SessionInfo["WeekendInfo"]["SimMode"].GetValue();
				IsLiveSession = simMode.ToLower().Contains("replay") ? false : true;

				if (IsLiveSession)
				{
					m_LiveSessionPopupVisible = true;

					var sessionWarningResult = MessageBox.Show("You seem to be running a live session.\n\nThis tool is designed for loaded replays. Some stuff may work, but it's not supported.",
						"Viewing a Live session", MessageBoxButton.OK, MessageBoxImage.Warning);

					if (sessionWarningResult == MessageBoxResult.OK || sessionWarningResult == MessageBoxResult.Cancel)
					{
						m_LiveSessionPopupVisible = false;

						StatusBarText = "iRacing Connected. Running a live session, not currently supported.";
					}
				}

				NodeCollection.SetSessionId(SessionID);
				LoadExistingProjectFile();

				// Must not prevent the session being marked ready. In the wrapper-recovery
				// path this runs inside an unhandled-exception handler, where calling into
				// the SDK can fail; log the failure but still enable the UI.
				try { InSimUIEnabled = true; }
				catch (Exception ex) { LogDiagnosticThrottled("InitSession: enabling sim UI failed: " + ex); }

				SessionInfoLoaded = true;

				RefreshCommandStates();
			}

			// Telemetry can be unavailable when the wrapper failed mid-update; don't let
			// that stop the session becoming usable.
			try
			{
				CurrentDriver = Drivers.FirstOrDefault(d => d.Id == Sim.Instance.Telemetry.CamCarIdx.Value);
				CurrentCamera = Cameras.FirstOrDefault(c => c.GroupNum == Sim.Instance.Telemetry.CamGroupNumber.Value);
			}
			catch (Exception ex) { LogDiagnosticThrottled("InitSession: reading telemetry selections failed: " + ex); }
		}

		private void TelemetryUpdatedCallback(object sender, SdkWrapper.TelemetryUpdatedEventArgs e)
		{
			ProcessTelemetry(e.TelemetryInfo);
		}

		/// <summary>
		/// Update frame/timing state from telemetry. Called from the normal TelemetryUpdated
		/// event and, as a fallback, when the iRacingSimulator wrapper throws before re-raising
		/// that event (see App.OnDispatcherUnhandledException).
		/// </summary>
		public void ProcessTelemetry(TelemetryInfo telemetry)
		{
			// Leave now if the session info hasn't already been loaded
			if (!SessionInfoLoaded)
				return;

			if (telemetry == null)
				telemetry = Sim.Instance.Telemetry;

			if (telemetry == null)
				return;

			CurrentFrame = telemetry.ReplayFrameNum.Value;
			FinalFrame = CurrentFrame + telemetry.ReplayFrameNumEnd.Value;
			SessionTime = telemetry.SessionTime.Value;
			CurrentPlaybackSpeed = telemetry.ReplayPlaySpeed.Value;
			PlaybackEnabled = CurrentPlaybackSpeed != 0;
			SlowMotionEnabled = telemetry.ReplayPlaySlowMotion.Value;
			NormalPlaybackSpeedEnabled = CurrentPlaybackSpeed == 1 && !SlowMotionEnabled;
			InSimCaptureSettingEnabled = Sim.Instance.Sdk.GetTelemetryValue<bool>("VidCapEnabled").Value;
			InSimCaptureActive = Sim.Instance.Sdk.GetTelemetryValue<bool>("VidCapActive").Value;

			// Set current car/camera based on sim selections (won't update unless different to app)
			CurrentDriver = Drivers.FirstOrDefault(d => d.Id == telemetry.CamCarIdx.Value);
			CurrentCamera = Cameras.FirstOrDefault(c => c.GroupNum == telemetry.CamGroupNumber.Value);

			// Update driver telemetry info
			UpdateDriverTelemetry();

			// Update status bar session info
			UpdateSessionInformation();

			if (!PlaybackEnabled)
			{
				var currentNode = NodeCollection.GetNodeOnCurrentFrame(CurrentFrame);
				_currentNode = currentNode; OnPropertyChanged("CurrentNode");

				UpdateUILabels();

				if (IsCaptureActive())
				{
					// Stop recording if playback is stopped (usually at end of replay...)
					StopRecording();
				}
			}

			PlaybackCameraSwitching();
			RateLimitedChecks();
		}

		private void UpdateDriverTelemetry()
		{
			var laps = Sim.Instance.Telemetry.CarIdxLap.Value;
			var lapDistances = Sim.Instance.Telemetry.CarIdxLapDistPct.Value;
			var trackSurfaces = Sim.Instance.Telemetry.CarIdxTrackSurface.Value;

			// Loop through the list of current drivers
			foreach (Driver driver in Drivers)
			{
				// Set the details belonging to this driver
				driver.Lap = laps[driver.Id];
				driver.LapDistance = lapDistances[driver.Id];

				var updatedSurface = (TrackSurfaces)trackSurfaces[driver.Id];
				var previousSurface = driver.TrackSurface;
				driver.TrackSurface = updatedSurface;

				// If driver was previously In World (On track, in pits etc.)
				if (previousSurface != TrackSurfaces.NotInWorld)
				{
					// If they are now Not In World (left session, not in car...), refresh the filter...
					if (updatedSurface == TrackSurfaces.NotInWorld)
					{
						DriversView.Refresh(); // Console.WriteLine($"{driver.Name} is now Out Of World");
					}
				}
				else
				{
					// If driver was previously Not In World (Not in the car...)
					// If they are now In World, update filter
					if (updatedSurface != TrackSurfaces.NotInWorld)
					{
						DriversView.Refresh(); // Console.WriteLine($"{driver.Name} is now On Track");
					}
				}
			}

			YamlQuery sessionInfoQuery = Sim.Instance.SessionInfo["SessionInfo"]["Sessions"]["SessionNum", Sim.Instance.Telemetry.SessionNum.Value];
			var sessionType = sessionInfoQuery["SessionType"].GetValue("");
			
			// Order list by lap then lap distance (sort by position)
			var orderedDriverList = Drivers.OrderByDescending(d => d.Lap).ThenByDescending(d => d.LapDistance).ToList();
			for (int i = 0; i < orderedDriverList.Count; i++)
			{
				// Set position in race session, otherwise set to 999
				if (sessionType.Contains("Race"))
				{
					// Set driver position
					orderedDriverList[i].Position = (i + 1);
					if (orderedDriverList[i].NumberRaw == 0) // Pace Car always placed in last place
					{
						orderedDriverList[i].Position = 999;
					}
				}
				else
				{
					orderedDriverList[i].Position = 999;
				}
			}

			// Update ahead/behind overlays when positions change during playback
			if (_overlayDriver != null && PlaybackEnabled)
			{
				UpdateRelativeDrivers();
			}
		}

		// Cached session number
		int currentSessionId = -1;

		private void UpdateSessionInformation()
		{
			// Cache session ID to check for changes
			if (currentSessionId != Sim.Instance.Telemetry.SessionNum.Value)
			{
				// If session has changed, force sorting command to sort by ID
				SortDriversById = false;
				ToggleDriverSortOptionCommand.Execute(this);

				currentSessionId = Sim.Instance.Telemetry.SessionNum.Value;
			}

			YamlQuery sessionInfoQuery = Sim.Instance.SessionInfo["SessionInfo"]["Sessions"]["SessionNum", Sim.Instance.Telemetry.SessionNum.Value];
			var sessionType = sessionInfoQuery["SessionType"].GetValue("");

			StatusBarCurrentSessionInfo = $"Current Session: {sessionType}";
		}

		// Used to limit updates on refreshing driver position ordering
		int updateCounter = 0;
		int updateRefreshRate = 90;

		private void RateLimitedChecks()
		{
			// Any updates that require less frequent checks can go here
			updateCounter++;
			if (updateCounter > updateRefreshRate)
			{
				updateCounter = 0;

				DriversView.Refresh();
			}
		}

		private void PlaybackCameraSwitching()
		{
			if (!NormalPlaybackSpeedEnabled)
				return;

			IEnumerable<Node> orderedNodes = TimelineNodesView.Cast<Node>();
			Node nodeToApply = orderedNodes.LastOrDefault(node => node.Frame <= CurrentFrame);

			if (StopRecordingOnFinalNode && IsCaptureActive())
			{
				var orderedNodesList = orderedNodes.ToList();
				var finalNode = orderedNodesList[orderedNodesList.Count - 1];

				if (CurrentFrame >= finalNode.Frame)
				{
					StopRecording();
					return;
				}
			}

			if (nodeToApply == null || nodeToApply == _lastAppliedNode)
				return;

			_lastAppliedNode = nodeToApply;
			CurrentNode = nodeToApply;
		}

		private void UpdateUILabels()
		{
			CamChangeBtnText = CurrentNode == null ? "Add Cam Change" : "Update Camera";

			//CaptureModeText = "Capture Mode: None";
			//if (UseOBSCapture) CaptureModeText = "Capture Mode: OBS";
			//else if (UseInSimCapture) CaptureModeText = "Capture Mode: iRacing";
		}

		private void LoadExistingProjectFile()
		{
			var loaddedProject = SaveLoadHelper.LoadProject(SessionID);
			if (loaddedProject.Nodes.Count > 0)
			{
				NodeCollection.Nodes.Clear();

				foreach (var node in loaddedProject.Nodes)
				{
					if (node.NodeType == NodeType.CamChange)
					{
						var foundDriver = Drivers.FirstOrDefault(d => d.NumberRaw == node.DriverNumber);
						var foundCamera = Cameras.FirstOrDefault(c => c.GroupName == node.CameraName);

						if (foundDriver == null)
						{
							Console.WriteLine($"ERROR: Couldn't find driver with number {node.DriverNumber}. Node ignored.");
						}
						else
						{
							if (foundCamera == null)
								foundCamera = new Camera() { GroupName = node.CameraName };

							CamChangeNode newCamChangeNode = new CamChangeNode(node.Enabled, node.Frame, foundDriver, foundCamera);

							NodeCollection.Nodes.Add(newCamChangeNode);
						}
					}
					else if (node.NodeType == NodeType.FrameSkip)
					{
						FrameSkipNode newFrameSkipNode = new FrameSkipNode(node.Enabled, node.Frame);

						NodeCollection.Nodes.Add(newFrameSkipNode);
					}
				}

				// Add node links if there is more than one node saved
				if (NodeCollection.Nodes.Count > 1)
				{
					for (int i = 0; i < NodeCollection.Nodes.Count; i++)
					{
						if (i > 0 && i < NodeCollection.Nodes.Count - 1)
						{
							// If not the first or last nodes, assign both previous and next nodes from list
							NodeCollection.Nodes[i].NextNode = NodeCollection.Nodes[i + 1];
							NodeCollection.Nodes[i].PreviousNode = NodeCollection.Nodes[i - 1];
						}
						else
						{
							// If first or last, assign just next/previous depending on which
							if (i == 0)
							{
								NodeCollection.Nodes[i].NextNode = NodeCollection.Nodes[i + 1];
							}
							if (i == NodeCollection.Nodes.Count - 1)
							{
								NodeCollection.Nodes[i].PreviousNode = NodeCollection.Nodes[i - 1];
							}
						}
					}
				}

				VerifyExistingNodeCameras();
			}
		}

		public bool IsSessionReady()
		{
			return SessionInfoLoaded && !IsLiveSession;
		}

		public bool IsCaptureAvailable()
		{
			return SelectedCaptureMode.IsReadyToRecord();
		}

		public bool IsCaptureActive()
		{
			return InSimCaptureActive || ExternalCaptureActive;
		}

		public void StartRecording()
		{
			Sim.Instance.Sdk.Replay.SetPlaybackSpeed(1);
			InSimUIEnabled = !DisableUIWhenRecording;

			if (SelectedCaptureMode.IsReadyToRecord())
			{
				SelectedCaptureMode.StartRecording();
				RecordBtnText = "Stop Rec";
				ExternalCaptureActive = SelectedCaptureMode.Name != "In-Sim Capture"; // TODO: Not sure about this. Should I use a property?
			}
		}

		public async void StopRecording()
		{
			Sim.Instance.Sdk.Replay.SetPlaybackSpeed(0);

			if (SelectedCaptureMode.IsReadyToRecord())
			{
				SelectedCaptureMode.StopRecording();
				RecordBtnText = "Record";
				ExternalCaptureActive = false;
			}

			await Task.Delay(500);
			
			InSimUIEnabled = true;
		}

		public void ToggleInactiveDriverDisplay()
		{
			if (ShowInactiveDrivers) DriversView.Filter = null;
			else DriversView.Filter = DriverOnTrackFilter;

			DriversView.Refresh();
		}
	}
}
