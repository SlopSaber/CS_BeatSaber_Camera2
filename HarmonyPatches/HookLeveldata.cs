using Camera2.Utils;
using HarmonyLib;
using IPA.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Camera2.HarmonyPatches {
	[HarmonyPatch]
	static class HookLeveldata {
		public static BeatmapLevel beatmapLevel;
		public static GameplayModifiers gameplayModifiers;
		public static bool is360Level = false;
		public static bool isModdedMap = false;
		public static bool hasCustomWallVisuals = false;
		public static bool isWallMap = false;

		[HarmonyTargetMethods]
		static IEnumerable<MethodBase> TargetMethods() {
            foreach(var m in AccessTools.GetDeclaredMethods(typeof(StandardLevelScenesTransitionSetupData)))
                if(m.Name == nameof(StandardLevelScenesTransitionSetupData.Init))
					yield return m;

            foreach(var m in AccessTools.GetDeclaredMethods(typeof(MissionLevelScenesTransitionSetupData)))
                if(m.Name == nameof(MissionLevelScenesTransitionSetupData.Init))
					yield return m;

			yield return AccessTools.FirstMethod(
                typeof(MultiplayerLevelScenesTransitionSetupData),
				x => x.Name == "Init"
			);
		}

		[HarmonyPostfix]
		static void Postfix(BeatmapKey beatmapKey, BeatmapLevel beatmapLevel, GameplayModifiers gameplayModifiers) {
#if DEBUG
			Plugin.Log.Info("Got level data!");
#endif
			HookLeveldata.beatmapLevel = beatmapLevel;
			HookLeveldata.gameplayModifiers = gameplayModifiers;

			isModdedMap = ModMapUtil.IsModdedMap(beatmapKey);
			hasCustomWallVisuals = ModMapUtil.HasCustomWallVisuals(beatmapKey);
            is360Level = beatmapKey.characteristic == BeatmapCharacteristic.Degree360;
			isWallMap = ModMapUtil.IsProbablyWallmap(beatmapKey);
		}

		internal static void Reset() {
			is360Level = isModdedMap = isWallMap = hasCustomWallVisuals = false;
			beatmapLevel = null;
		}
	}
}
