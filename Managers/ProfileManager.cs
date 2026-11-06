using Camera2.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Camera2.Managers {
	/// <summary>
	/// Optional sets of cameras, scenes and movement scripts. Each profile is a folder in
	/// UserData/Camera2/Profiles; the Camera2 folder itself is the default profile.
	/// </summary>
	static class ProfileManager {
		public const string DefaultProfile = "Default";

		class ProfileSettings {
			public bool enabled = false;
			public string active = null;
		}

		static ProfileSettings settings = LoadSettings();

		public static bool enabled => settings.enabled;

		public static string activeProfile =>
			settings.enabled && GetProfileDir(settings.active) != null ? settings.active : DefaultProfile;

		internal static string activeProfileDir => settings.enabled ? GetProfileDir(settings.active) : null;

		public static List<string> GetProfiles() {
			var profiles = new List<string> { DefaultProfile };

			if(Directory.Exists(ConfigUtil.ProfilesDir)) {
				profiles.AddRange(Directory.GetDirectories(ConfigUtil.ProfilesDir)
					.Select(Path.GetFileName)
					.Where(x => !string.Equals(x, DefaultProfile, StringComparison.OrdinalIgnoreCase))
					.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
			}

			return profiles;
		}

		public static void SetEnabled(bool value) {
			if(settings.enabled == value)
				return;

			var previousDir = ConfigUtil.ConfigDir;
			settings.enabled = value;
			Apply(previousDir);
		}

		public static void SwitchTo(string profile) {
			if(profile == DefaultProfile)
				profile = null;

			if(settings.active == profile)
				return;

			var previousDir = ConfigUtil.ConfigDir;
			settings.active = profile;
			Apply(previousDir);
		}

		static void Apply(string previousDir) {
			SaveSettings();

			if(string.Equals(previousDir, ConfigUtil.ConfigDir, StringComparison.OrdinalIgnoreCase))
				return;

			Plugin.Log.Info($"Switching to Camera2 profile {activeProfile}");
			MovementScriptManager.LoadMovementScripts(true);
			CamManager.ReloadProfile();
		}

		static string GetProfileDir(string profile) {
			if(string.IsNullOrEmpty(profile) || profile.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
				return null;

			var dir = Path.Combine(ConfigUtil.ProfilesDir, profile);

			return Directory.Exists(dir) ? dir : null;
		}

		static ProfileSettings LoadSettings() {
			try {
				if(File.Exists(ConfigUtil.ProfilesCfg))
					return JsonConvert.DeserializeObject<ProfileSettings>(File.ReadAllText(ConfigUtil.ProfilesCfg)) ?? new ProfileSettings();
			} catch(Exception ex) {
				Plugin.Log.Error("Failed to load Camera2 profile settings:");
				Plugin.Log.Error(ex);
			}

			return new ProfileSettings();
		}

		static void SaveSettings() {
			try {
				Directory.CreateDirectory(ConfigUtil.RootDir);
				File.WriteAllText(ConfigUtil.ProfilesCfg, JsonConvert.SerializeObject(settings, Formatting.Indented));
			} catch(Exception ex) {
				Plugin.Log.Error("Failed to save Camera2 profile settings:");
				Plugin.Log.Error(ex);
			}
		}
	}
}
