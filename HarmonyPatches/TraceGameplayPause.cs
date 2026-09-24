using HarmonyLib;
using System.Diagnostics;
using System.Linq;

namespace Camera2.HarmonyPatches {
	// Temporary diagnostic: identify the caller when gameplay repeatedly pauses.
	[HarmonyPatch(typeof(PauseController), nameof(PauseController.Pause))]
	static class TraceGameplayPause {
		static void Prefix(PauseController __instance) {
			var xr = AccessTools.Field(typeof(PauseController), "_xrSystemState")?.GetValue(__instance) as IXRSystemState;
			var frames = new StackTrace(1, false).GetFrames();
			var callers = frames == null ? "unknown" : string.Join(" <- ", frames
				.Where(frame => frame.GetMethod() != null)
				.Take(10)
				.Select(frame => $"{frame.GetMethod().DeclaringType?.FullName}.{frame.GetMethod().Name}"));
			Plugin.Log.Info($"Pause trace: inputFocus={xr?.hasInputFocus}, hmdMounted={xr?.hasHmdMounted}, appFocusLost={xr?.IsAppFocusCurrentlyLost()}, callers={callers}");
		}
	}
}
