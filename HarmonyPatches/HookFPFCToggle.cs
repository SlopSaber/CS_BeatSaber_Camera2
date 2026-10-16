using Camera2.Managers;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Camera2.HarmonyPatches {
    static class HookFPFCToggle {
        private static readonly System.Type FlightType = typeof(IPA.Utilities.UnityGame).Assembly.GetType("IPA.Desktop.DesktopFlight");
        private static readonly PropertyInfo FlightInstance = FlightType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly PropertyInfo FlightCamera = FlightType?.GetProperty("Camera", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo FlightReady = FlightType?.GetProperty("Ready", BindingFlags.Instance | BindingFlags.NonPublic);
        public static Transform fpfcTransform { get; private set; }
        private static bool active;
        public static bool isInFPFC => active && fpfcTransform != null && fpfcTransform.gameObject.activeInHierarchy;

        internal static void Update() {
            if(FlightType == null) return;
            var flight = FlightInstance?.GetValue(null) as MonoBehaviour;
            var camera = flight != null ? FlightCamera?.GetValue(flight) as Camera : null;
            bool enabled = flight != null && flight.isActiveAndEnabled && camera != null && (bool)(FlightReady?.GetValue(flight) ?? false);
            SetFPFCActive(enabled ? camera.transform : null, enabled);
        }

        public static void SetFPFCActive(Transform transform, bool isActive = true) {
            if(fpfcTransform == transform && active == isActive) return;
            fpfcTransform = transform;
            active = isActive;
            ScenesManager.ActiveSceneChanged();
        }

        [HarmonyPatch(typeof(FirstPersonFlyingController), nameof(FirstPersonFlyingController.OnEnable))]
        static class HookBasegameFPFC {
            static void Postfix(Transform ____camera) {
                if(FlightType == null) SetFPFCActive(____camera);
            }
        }
    }
}
