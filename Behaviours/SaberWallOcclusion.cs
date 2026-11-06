#if BEAT_SABER_1453
using Camera2.Utils;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Camera2.Behaviours {
	class SaberWallOcclusion : MonoBehaviour {
		private Cam2 cam;
		private RenderObjects feature;
		private int layerMask;
		private RenderPassEvent passEvent;
		private SceneDepthPass sceneDepth;
		private SaberPass sabers;
		private RenderObjectsPass others;
		private bool logged;

		internal void Init(Cam2 owner) => cam = owner;

		internal bool Enqueue(RenderObjects source, ScriptableRenderer renderer) {
			var settings = source.settings;
			var saberMask = (int)VisibilityMasks.Sabers;
			if(cam == null || cam.settings == null || cam.settings.type != Configuration.CameraType.Positionable ||
				!cam.settings.visibleObjects.Sabers || !ScreenDisplacementEffectRendererFeature.enabled ||
				WallTextureLayers.ScreenDisplacement < 0 ||
				(cam.UCamera.cullingMask & (1 << WallTextureLayers.ScreenDisplacement)) == 0 ||
				(SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0 ||
				settings.passTag != "TransparentAfterScreenDisplacement" ||
				settings.filterSettings.RenderQueueType != RenderQueueType.Transparent ||
				(settings.filterSettings.LayerMask.value & saberMask) == 0 ||
				settings.Event != RenderPassEvent.BeforeRenderingPostProcessing ||
				settings.overrideMaterial != null || settings.overrideShader != null || settings.overrideDepthState ||
				settings.stencilSettings.overrideStencilState || settings.cameraSettings.overrideCamera ||
				(settings.filterSettings.PassNames != null && settings.filterSettings.PassNames.Length != 0))
				return false;

			if(feature != source || layerMask != settings.filterSettings.LayerMask.value || passEvent != settings.Event) {
				feature = source;
				layerMask = settings.filterSettings.LayerMask.value;
				passEvent = settings.Event;
				sceneDepth = new SceneDepthPass();
				sabers = new SaberPass(sceneDepth, settings);
				others = new RenderObjectsPass(settings.passTag, settings.Event, null,
					RenderQueueType.Transparent, layerMask & ~saberMask, settings.cameraSettings);
			}
			renderer.EnqueuePass(sceneDepth);
			renderer.EnqueuePass(sabers);
			if((layerMask & ~saberMask) != 0) renderer.EnqueuePass(others);
			if(!logged) {
				Plugin.Log.Info($"Camera {cam.name}: sabers use scene depth before screen-displacement walls.");
				logged = true;
			}
			return true;
		}

		private sealed class SceneDepthPass : ScriptableRenderPass {
			private sealed class PassData {
				internal TextureHandle source;
				internal TextureHandle destination;
			}

			internal TextureHandle depth;

			internal SceneDepthPass() {
				renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing - 1);
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) {
				depth = TextureHandle.nullHandle;
				var resources = frameData.Get<UniversalResourceData>();
				if(resources.isActiveTargetBackBuffer || !resources.activeDepthTexture.IsValid() ||
					resources.activeDepthTexture != resources.cameraDepth) return;

				var descriptor = graph.GetTextureDesc(resources.activeDepthTexture);
				descriptor.name = "Camera2 depth before walls";
				descriptor.clearBuffer = false;
				descriptor.memoryless = RenderTextureMemoryless.None;
				depth = graph.CreateTexture(descriptor);
				using(var builder = graph.AddUnsafePass<PassData>("Camera2 save depth before walls", out var data)) {
					data.source = resources.activeDepthTexture;
					data.destination = depth;
					builder.UseTexture(data.source, AccessFlags.Read);
					builder.UseTexture(data.destination, AccessFlags.WriteAll);
					builder.SetRenderFunc((PassData pass, UnsafeGraphContext context) => {
						// Copy the entire matching depth/stencil surface, including every MSAA sample.
						CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).CopyTexture(
							(RenderTargetIdentifier)pass.source, (RenderTargetIdentifier)pass.destination);
					});
				}
			}
		}

		private sealed class SaberPass : RenderObjectsPass {
			private readonly SceneDepthPass sceneDepth;

			internal SaberPass(SceneDepthPass depth, RenderObjects.RenderObjectsSettings settings)
				: base("Camera2 sabers through walls", settings.Event, null, RenderQueueType.Transparent,
					(int)VisibilityMasks.Sabers, settings.cameraSettings) {
				sceneDepth = depth;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) {
				var resources = frameData.Get<UniversalResourceData>();
				var original = resources.cameraDepth;
				if(!sceneDepth.depth.IsValid() || resources.activeDepthTexture != original) {
					base.RecordRenderGraph(graph, frameData);
					return;
				}
				// Only this renderer list binds the saved depth; subsequent passes retain the camera's depth.
				resources.cameraDepth = sceneDepth.depth;
				try {
					base.RecordRenderGraph(graph, frameData);
				} finally {
					resources.cameraDepth = original;
				}
			}
		}
	}

	[HarmonyPatch(typeof(RenderObjects), nameof(RenderObjects.AddRenderPasses))]
	static class SaberWallOcclusionPatch {
		static bool Prefix(RenderObjects __instance, ScriptableRenderer renderer, ref RenderingData renderingData) {
			var camera = renderingData.cameraData.camera;
			return camera == null || !camera.TryGetComponent<SaberWallOcclusion>(out var pass) || !pass.Enqueue(__instance, renderer);
		}
	}
}
#endif
