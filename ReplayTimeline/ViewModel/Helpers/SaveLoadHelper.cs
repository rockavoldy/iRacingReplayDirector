using System;
using System.Collections.Generic;
using System.IO;
using iRacingReplayDirector.AI.Director;
using Newtonsoft.Json;
using System.Linq;


namespace iRacingReplayDirector
{
	public class SaveLoadHelper
	{
		private const string m_ProjectsSubFolder = "Saves";
		private const string m_ProjectExtension = ".timeline";

		private const Formatting m_FileFormatting = Formatting.Indented;


		public static void SaveSettings(ReplayDirectorVM vm)
		{
			SaveAIDirectorSettings(vm.AIDirector?.Settings);
			Properties.Settings.Default.Save();
		}

		public static void LoadSettings(ReplayDirectorVM vm)
		{
			vm.WindowWidth = Properties.Settings.Default.WindowWidth;
			vm.WindowHeight = Properties.Settings.Default.WindowHeight;
			vm.WindowAlwaysOnTop = Properties.Settings.Default.WindowOnTop;

			vm.ShowVisualTimeline = Properties.Settings.Default.ShowVisualTimeline;
			vm.ShowRecordingControls = Properties.Settings.Default.ShowRecordingControls;
			vm.ShowSessionLapSkipControls = Properties.Settings.Default.ShowSessionLapSkipControls;
			vm.FrameSkipInfoShown = Properties.Settings.Default.FrameSkipInfoShown;

			vm.DisableSimUIOnPlayback = Properties.Settings.Default.DisableSimUIOnPlayback;
			vm.DisableUIWhenRecording = Properties.Settings.Default.DisableUIWhenRecording;
			vm.StopRecordingOnFinalNode = Properties.Settings.Default.StopRecordingOnFinalNode;

			vm.ShowInactiveDrivers = Properties.Settings.Default.ShowInactiveDrivers;

			// The Auto Director overlay settings are the source of truth for the driver and
			// leaderboard overlays; fall back to the legacy top-level flags only if the Auto
			// Director settings have not been created yet.
			var aiSettings = vm.AIDirector?.Settings;
			if (aiSettings != null)
			{
				LoadAIDirectorSettings(aiSettings);

				vm.ShowDriverOverlay = aiSettings.ShowCurrentDriverOverlay;
				vm.ShowLeaderboardOverlay = aiSettings.ShowLeaderboardOverlay;
			}
			else
			{
				vm.ShowDriverOverlay = Properties.Settings.Default.ShowDriverOverlay;
				vm.ShowLeaderboardOverlay = Properties.Settings.Default.ShowLeaderboardOverlay;
			}

			vm.SelectedCaptureMode = vm.CaptureModes.FirstOrDefault(m => m.Name == Properties.Settings.Default.CaptureModeSelected);
		}

		/// <summary>
		/// Copies the global Auto Director settings from user settings into the supplied
		/// settings object. These are application-wide (LLM credentials, camera exclusions,
		/// detection toggles, weights, camera-plan timing, focus driver and overlays) and are
		/// intentionally not tied to any session.
		/// </summary>
		public static void LoadAIDirectorSettings(AIDirectorSettings settings)
		{
			if (settings == null)
				return;

			var s = Properties.Settings.Default;

			// LLM provider settings
			settings.SelectedProvider = s.AIDirectorSelectedProvider;
			settings.OpenAIApiKey = s.AIDirectorOpenAIApiKey;
			settings.OpenAIModel = s.AIDirectorOpenAIModel;
			settings.LocalModelEndpoint = s.AIDirectorLocalModelEndpoint;
			settings.LocalModelName = s.AIDirectorLocalModelName;
			settings.OpenAICompatibleBaseUrl = s.AIDirectorOpenAICompatibleBaseUrl;
			settings.OpenAICompatibleApiKey = s.AIDirectorOpenAICompatibleApiKey;
			settings.OpenAICompatibleModel = s.AIDirectorOpenAICompatibleModel;

			// Event detection settings
			settings.DetectIncidents = s.AIDirectorDetectIncidents;
			settings.DetectOvertakes = s.AIDirectorDetectOvertakes;
			settings.DetectBattles = s.AIDirectorDetectBattles;
			settings.ScanIntervalFrames = s.AIDirectorScanIntervalFrames;

			// Driver selection weights
			settings.IncidentWeight = s.AIDirectorIncidentWeight;
			settings.OvertakeWeight = s.AIDirectorOvertakeWeight;
			settings.BattleWeight = s.AIDirectorBattleWeight;
			settings.MomentumWeight = s.AIDirectorMomentumWeight;
			settings.PackWeight = s.AIDirectorPackWeight;
			settings.FreshActionWeight = s.AIDirectorFreshActionWeight;
			settings.PositionWeight = s.AIDirectorPositionWeight;
			settings.VarietyPenalty = s.AIDirectorVarietyPenalty;
			settings.VarietyDampening = s.AIDirectorVarietyDampening;
			settings.MinCutsPerMinute = s.AIDirectorMinCutsPerMinute;

			// Camera plan generation settings
			settings.UseAIForCameraPlan = s.AIDirectorUseAIForCameraPlan;
			settings.EventAnticipationSeconds = s.AIDirectorEventAnticipationSeconds;
			settings.MaxSecondsBetweenCuts = s.AIDirectorMaxSecondsBetweenCuts;
			settings.MinSecondsBetweenCuts = s.AIDirectorMinSecondsBetweenCuts;
			settings.SecondsBetweenCuts = s.AIDirectorSecondsBetweenCuts;

			// Focus driver settings
			settings.FocusDriverNumber = s.AIDirectorFocusDriverNumber;
			settings.FocusDriverBonus = s.AIDirectorFocusDriverBonus;

			// Camera exclusion settings
			settings.ExcludedCameras = s.AIDirectorExcludedCameras;

			// Overlay settings
			settings.ShowCurrentDriverOverlay = s.AIDirectorShowCurrentDriverOverlay;
			settings.ShowAheadDriverOverlay = s.AIDirectorShowAheadDriverOverlay;
			settings.ShowBehindDriverOverlay = s.AIDirectorShowBehindDriverOverlay;
			settings.ShowLeaderboardOverlay = s.AIDirectorShowLeaderboardOverlay;
			settings.OverlayPosition = s.AIDirectorOverlayPosition;
			settings.OverlayOffset = s.AIDirectorOverlayOffset;
			settings.OverlayFontSize = s.AIDirectorOverlayFontSize;
		}

		/// <summary>
		/// Copies the supplied Auto Director settings into user settings. Call
		/// <see cref="SaveSettings"/> (or <c>Properties.Settings.Default.Save()</c>) afterwards
		/// to persist them to disk.
		/// </summary>
		public static void SaveAIDirectorSettings(AIDirectorSettings settings)
		{
			if (settings == null)
				return;

			var s = Properties.Settings.Default;

			// LLM provider settings
			s.AIDirectorSelectedProvider = settings.SelectedProvider;
			s.AIDirectorOpenAIApiKey = settings.OpenAIApiKey;
			s.AIDirectorOpenAIModel = settings.OpenAIModel;
			s.AIDirectorLocalModelEndpoint = settings.LocalModelEndpoint;
			s.AIDirectorLocalModelName = settings.LocalModelName;
			s.AIDirectorOpenAICompatibleBaseUrl = settings.OpenAICompatibleBaseUrl;
			s.AIDirectorOpenAICompatibleApiKey = settings.OpenAICompatibleApiKey;
			s.AIDirectorOpenAICompatibleModel = settings.OpenAICompatibleModel;

			// Event detection settings
			s.AIDirectorDetectIncidents = settings.DetectIncidents;
			s.AIDirectorDetectOvertakes = settings.DetectOvertakes;
			s.AIDirectorDetectBattles = settings.DetectBattles;
			s.AIDirectorScanIntervalFrames = settings.ScanIntervalFrames;

			// Driver selection weights
			s.AIDirectorIncidentWeight = settings.IncidentWeight;
			s.AIDirectorOvertakeWeight = settings.OvertakeWeight;
			s.AIDirectorBattleWeight = settings.BattleWeight;
			s.AIDirectorMomentumWeight = settings.MomentumWeight;
			s.AIDirectorPackWeight = settings.PackWeight;
			s.AIDirectorFreshActionWeight = settings.FreshActionWeight;
			s.AIDirectorPositionWeight = settings.PositionWeight;
			s.AIDirectorVarietyPenalty = settings.VarietyPenalty;
			s.AIDirectorVarietyDampening = settings.VarietyDampening;
			s.AIDirectorMinCutsPerMinute = settings.MinCutsPerMinute;

			// Camera plan generation settings
			s.AIDirectorUseAIForCameraPlan = settings.UseAIForCameraPlan;
			s.AIDirectorEventAnticipationSeconds = settings.EventAnticipationSeconds;
			s.AIDirectorMaxSecondsBetweenCuts = settings.MaxSecondsBetweenCuts;
			s.AIDirectorMinSecondsBetweenCuts = settings.MinSecondsBetweenCuts;
			s.AIDirectorSecondsBetweenCuts = settings.SecondsBetweenCuts;

			// Focus driver settings
			s.AIDirectorFocusDriverNumber = settings.FocusDriverNumber;
			s.AIDirectorFocusDriverBonus = settings.FocusDriverBonus;

			// Camera exclusion settings
			s.AIDirectorExcludedCameras = settings.ExcludedCameras;

			// Overlay settings
			s.AIDirectorShowCurrentDriverOverlay = settings.ShowCurrentDriverOverlay;
			s.AIDirectorShowAheadDriverOverlay = settings.ShowAheadDriverOverlay;
			s.AIDirectorShowBehindDriverOverlay = settings.ShowBehindDriverOverlay;
			s.AIDirectorShowLeaderboardOverlay = settings.ShowLeaderboardOverlay;
			s.AIDirectorOverlayPosition = settings.OverlayPosition;
			s.AIDirectorOverlayOffset = settings.OverlayOffset;
			s.AIDirectorOverlayFontSize = settings.OverlayFontSize;
		}

		public static void SaveProject(List<Node> nodes, int sessionID)
		{
			TimelineProject newProject = new TimelineProject();
			newProject.SessionID = sessionID;

			foreach (var node in nodes)
			{
				if (node is CamChangeNode)
				{
					CamChangeNode camChangeNode = node as CamChangeNode;
					NodeSaveFile newSaveNode = new NodeSaveFile(camChangeNode.Enabled, camChangeNode.Frame, NodeType.CamChange, 1, camChangeNode.Driver.NumberRaw, camChangeNode.Camera.GroupName);

					newProject.Nodes.Add(newSaveNode);
				}
				else
				{
					FrameSkipNode frameSkipNode = node as FrameSkipNode;
					NodeSaveFile newSaveNode = new NodeSaveFile(frameSkipNode.Enabled, frameSkipNode.Frame, NodeType.FrameSkip);

					newProject.Nodes.Add(newSaveNode);
				}
			}

			var saveFilePath = GenerateProjectFilePath(sessionID);
			SaveToFile(saveFilePath, newProject);
		}

		public static TimelineProject LoadProject(int sessionID)
		{
			var saveFilePath = GenerateProjectFilePath(sessionID);
			TimelineProject loadedProject = new TimelineProject();

			if (File.Exists(saveFilePath))
			{
				try
				{
					string fileContents = File.ReadAllText(saveFilePath);
					loadedProject = JsonConvert.DeserializeObject<TimelineProject>(fileContents);

					if (loadedProject == null)
						loadedProject = new TimelineProject();
				}
				catch (Exception e)
				{
					Console.WriteLine(e.Message);
				}
			}

			return loadedProject;
		}

		private static string GenerateProjectFilePath(int sessionID)
		{
			var finalFilename = $"{sessionID}{m_ProjectExtension}";
			var finalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, m_ProjectsSubFolder);

			if (!Directory.Exists(finalPath))
			{
				try
				{
					Directory.CreateDirectory(finalPath);
				}
				catch (Exception e)
				{
					Console.WriteLine(e.Message);
				}
			}

			return Path.Combine(finalPath, finalFilename);
		}

		private static void SaveToFile<T>(string fullSavePath, T saveData)
		{
			string jsonOutput = JsonConvert.SerializeObject(saveData, m_FileFormatting);

			try { File.WriteAllText(fullSavePath, jsonOutput); }
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}
	}
}
