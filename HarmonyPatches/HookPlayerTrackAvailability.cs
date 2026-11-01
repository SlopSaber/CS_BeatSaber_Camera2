using Camera2.Middlewares;
using HarmonyLib;
using System.Reflection;

namespace Camera2.HarmonyPatches {

	[HarmonyPatch]
	static class HookPlayerTrackAvailability {
		private static MethodInfo initialize;
		private static PropertyInfo active;

		static bool Prepare() {
			var manager = AccessTools.TypeByName("NoodleExtensions.Managers.NoodlePlayerTransformManager");
			if(manager == null)
				return false;
			initialize = AccessTools.Method(manager, "Initialize");
			active = AccessTools.Property(manager, "Active");
			return initialize != null && active?.PropertyType == typeof(bool) && active.GetGetMethod() != null;
		}

		static MethodBase TargetMethod() => initialize;

		static void Postfix(object __instance) {
			ModmapExtensions.PlayerTrackAvailable = (bool)active.GetValue(__instance);
		}
	}
}
