using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Camera2.HarmonyPatches {
	[HarmonyPatch(typeof(SmoothCamera), nameof(SmoothCamera.OnEnable))]
	static class DisableSmoothCamera {
		static bool Prefix(SmoothCamera __instance, Camera ____camera) {
			// The serialized Camera can already be enabled before OnEnable runs.
			if(____camera != null)
				____camera.enabled = false;
			__instance.enabled = false;
#if DEBUG
			Plugin.Log.Info("Prevented Smooth camera from activating");
#endif
			return false;
		}
	}
}
