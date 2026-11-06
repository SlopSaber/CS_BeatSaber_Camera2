using Camera2.Managers;
using IPA.Utilities;
using System.IO;

namespace Camera2.Utils {
	static class ConfigUtil {
		public static readonly string RootDir = Path.Combine(UnityGame.UserDataPath, "Camera2");
		public static readonly string ProfilesDir = Path.Combine(RootDir, "Profiles");
		public static readonly string ProfilesCfg = Path.Combine(RootDir, "Profiles.json");

		public static string ConfigDir => ProfileManager.activeProfileDir ?? RootDir;
		public static string ScenesCfg => Path.Combine(ConfigDir, "Scenes.json");
		public static string CamsDir => Path.Combine(ConfigDir, "Cameras");
		public static string MovementScriptsDir => Path.Combine(ConfigDir, "MovementScripts");

		public static string GetCameraPath(string name) {
			return Path.Combine(CamsDir, $"{name}.json");
		}
		public static string GetMovementScriptPath(string name) {
			return Path.Combine(MovementScriptsDir, $"{name}.json");
		}
	}
}
