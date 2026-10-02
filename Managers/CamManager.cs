using BeatSaberMarkupLanguage.Util;
using Camera2.Behaviours;
using Camera2.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;

namespace Camera2.Managers {

#if DEBUG
	public
#endif
	static class CamManager {
		public static Dictionary<string, Cam2> cams { get; private set; } = new Dictionary<string, Cam2>();
		internal static CamerasViewport customScreen { get; private set; }
		public static int baseCullingMask { get; internal set; }
		public static int clearedBaseCullingMask { get; private set; }
		static readonly CancellationTokenSource lifetime = new CancellationTokenSource();
		static readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		static Task<ConfigInputs> preloadTask;
		static CancellationTokenSource loadCancellation;
		static Task loadingTask;
		static int loadVersion;
		internal static Task Ready => ready.Task;

		internal static void Preload() {
			if(preloadTask == null)
				preloadTask = ConfigFiles.ReadInputsAsync(ConfigUtil.CamsDir, ConfigUtil.MovementScriptsDir, ConfigUtil.ScenesCfg, lifetime.Token);
		}

		internal static void BeginInit() {
			SetupOwner();
			BeginLoad(false);
		}

		internal static void BeginReload() => BeginLoad(true);

		internal static void EnsureReady() {
			if(!ConfigFiles.stopped && !ready.Task.IsCompleted && (loadingTask == null || loadingTask.IsCompleted) && CanCloneMainCamera())
				BeginLoad(cams.Count > 0);
		}

		static bool CanCloneMainCamera() => Camera.main != null || GameObject.FindGameObjectsWithTag("MainCamera").Length > 0;

		static void InvalidatePendingLoad() {
			loadVersion++;
			loadCancellation?.Cancel();
		}

		static void BeginLoad(bool reload) {
			if(ConfigFiles.stopped || customScreen == null) return;
			loadCancellation?.Cancel();
			var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
			loadCancellation = cancellation;
			var version = ++loadVersion;
			var expected = cams.ToDictionary(x => x.Key, x => x.Value);
			var snapshots = expected.ToDictionary(x => x.Key, x => x.Value.settings.Snapshot());
			var scenesSnapshot = ScenesManager.settings.Snapshot();
			var inputTask = !reload && preloadTask != null ? preloadTask :
				ConfigFiles.ReadInputsAsync(ConfigUtil.CamsDir, ConfigUtil.MovementScriptsDir, ConfigUtil.ScenesCfg, cancellation.Token);
			preloadTask = null;
			loadingTask = LoadAsync(inputTask, reload, version, cancellation, expected, snapshots, scenesSnapshot);
			ConfigFiles.Retain(loadingTask);
		}

		static bool IsCurrent(int version, CancellationToken token, Dictionary<string, Cam2> expected, Dictionary<string, string> snapshots, string scenesSnapshot) {
			if(ConfigFiles.stopped || token.IsCancellationRequested || version != loadVersion || customScreen == null || cams.Count != expected.Count)
				return false;
			foreach(var item in expected)
				if(!cams.TryGetValue(item.Key, out var cam) || cam != item.Value || cam == null || cam.settings.Snapshot() != snapshots[item.Key])
					return false;
			return ScenesManager.settings.Snapshot() == scenesSnapshot;
		}

		static async Task LoadAsync(Task<ConfigInputs> inputTask, bool reload, int version, CancellationTokenSource cancellation,
			Dictionary<string, Cam2> expected, Dictionary<string, string> snapshots, string scenesSnapshot) {
			var token = cancellation.Token;
			try {
				var inputs = await inputTask.ConfigureAwait(false);
				token.ThrowIfCancellationRequested();
				var batch = await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					if(!IsCurrent(version, token, expected, snapshots, scenesSnapshot)) return null;
					return MovementScriptManager.Prepare(inputs.movements);
				}).ConfigureAwait(false);
				if(batch == null) return;
				var errors = await ConfigFiles.MigrateAsync(batch.migrations, token).ConfigureAwait(false);
				await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					if(!IsCurrent(version, token, expected, snapshots, scenesSnapshot) || !CanCloneMainCamera()) return;
					MovementScriptManager.Apply(batch, errors, reload);
					LoadCamerasPrepared(inputs.cameras, reload);
					ScenesManager.settings.LoadPrepared(inputs.scenes);
					if(reload) ShaderManager.Reload();
					ready.TrySetResult(true);
				}).ConfigureAwait(false);
			} catch(OperationCanceledException) { }
			catch(Exception ex) {
				await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					if(ConfigFiles.stopped || token.IsCancellationRequested || version != loadVersion) return;
					Plugin.Log.Error("Failed to load Camera2 config:");
					Plugin.Log.Error(ex);
					if(!CanCloneMainCamera()) return;
					if(cams.Count == 0) InitCameraPrepared("Main", new ConfigFile { path = ConfigUtil.GetCameraPath("Main") }, false);
					ready.TrySetResult(true);
				});
			} finally {
				await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					if(loadCancellation == cancellation) loadCancellation = null;
					cancellation.Dispose();
				});
			}
		}

		internal static void StopLoads() {
			lifetime.Cancel();
			ready.TrySetCanceled();
		}

		public static void Init() {
			InvalidatePendingLoad();
			SetupOwner();
			LoadCameras();
			ScenesManager.settings.Load();
			ready.TrySetResult(true);
		}

		static void SetupOwner() {
			clearedBaseCullingMask = baseCullingMask != 0 ? baseCullingMask : SceneUtil.GetMainCameraButReally().GetComponent<Camera>().cullingMask;

			foreach(int mask in Enum.GetValues(typeof(VisibilityMasks)))
				clearedBaseCullingMask &= ~mask;

			//Adding _THIS_IS_NORMAL so that ends up in the stupid warning Unity logs when having a SS overlay w/ active VR
			customScreen = new GameObject("Cam2_Viewport_THIS_IS_NORMAL").AddComponent<CamerasViewport>();

			new GameObject("Cam2_Positioner", typeof(CamPositioner));
		}

		static void LoadCamerasPrepared(ConfigFile[] files, bool reload) {
			var loaded = new HashSet<string>();
			foreach(var file in files) {
				var name = Path.GetFileNameWithoutExtension(file.path);
				try {
					InitCameraPrepared(name, file, reload);
					loaded.Add(name);
				} catch(Exception ex) {
					Plugin.Log.Error($"Failed to load Camera {Path.GetFileName(file.path)}");
					Plugin.Log.Error(ex);
				}
			}
			if(reload) foreach(var item in cams.Where(x => !loaded.Contains(x.Key)).ToArray()) {
				GameObject.Destroy(item.Value);
				cams.Remove(item.Key);
			}
			if(cams.Count == 0) InitCameraPrepared("Main", new ConfigFile { path = ConfigUtil.GetCameraPath("Main") }, false);
			ApplyCameraValues(viewLayer: true);
		}

		static Cam2 InitCameraPrepared(string name, ConfigFile file, bool reload) {
			if(cams.TryGetValue(name, out var cam)) {
				if(!reload) throw new Exception("Already exists??");
				cam.settings.ReloadPrepared(file);
				return cam;
			}
			cam = new GameObject($"Cam2_{name}").AddComponent<Cam2>();
			try { cam.InitPrepared(name, customScreen.AddNewView(), file); }
			catch {
				GameObject.DestroyImmediate(cam);
				throw;
			}
			cams[name] = cam;
			return cam;
		}

		private static void LoadCameras(bool reload = false) {
			ConfigFiles.Flush();
			if(!Directory.Exists(ConfigUtil.CamsDir))
				Directory.CreateDirectory(ConfigUtil.CamsDir);

			var loadedNames = new List<string>();

			foreach(var cam in Directory.GetFiles(ConfigUtil.CamsDir, "*.json")) {
				try {
					var name = Path.GetFileNameWithoutExtension(cam);

					InitCamera(name, true, reload);

					if(reload)
						loadedNames.Add(name);
				} catch(Exception ex) {
					Plugin.Log.Error($"Failed to load Camera {Path.GetFileName(cam)}");
					Plugin.Log.Error(ex);
				}
			}
			if(reload) foreach(var deletedCam in cams.Where(x => !loadedNames.Contains(x.Key))) {
				GameObject.Destroy(deletedCam.Value);
				cams.Remove(deletedCam.Key);
			}

			if(cams.Count == 0) {
				var cam = InitCamera("Main", false);
			}

			ApplyCameraValues(viewLayer: true);
		}

		public static void Reload() {
			InvalidatePendingLoad();
			LoadCameras(true);
			ScenesManager.settings.Load();
		}

		/* 
		 * Unfortunately the Canvas Images cannot have their "layer" / z-index set to arbitrary numbers,
		 * so we need to sort the cams by their set layer number and set the sibling index accordingly
		 */
		public static void ApplyCameraValues(bool viewLayer = false, bool bitMask = false, bool worldCam = false, bool posRot = false) {
			var collection = viewLayer ? cams.Values.OrderBy(x => x.isCurrentlySelectedInSettings ? int.MaxValue : x.settings.layer).AsEnumerable() : cams.Values;

			foreach(var cam in collection) {
				if(viewLayer) cam.previewImage.transform.SetAsLastSibling();
				if(bitMask) cam.settings.ApplyLayerBitmask();
				if(worldCam) cam.ShowWorldCamIfNecessary();
				if(posRot) cam.settings.ApplyPositionAndRotation();
			}
		}

		public static Cam2 InitCamera(string name, bool loadConfig = true, bool reload = false) {
			if(cams.TryGetValue(name, out var cam)) {
				if(reload) {
					cam.settings.Reload();
					return cam;
				}

				throw new Exception("Already exists??");
			}

			cam = new GameObject($"Cam2_{name}").AddComponent<Cam2>();

			try {
				cam.Init(name, customScreen.AddNewView(), loadConfig);
			} catch {
				GameObject.DestroyImmediate(cam);
				throw;
			}

			cams[name] = cam;

			//Newly added cameras should always be the last child and thus on top
			//ApplyCameraValues(viewLayer: true);

			return cam;
		}

		public static Cam2 AddNewCamera(string namePrefix = "Unnamed Camera") {
			var nameToUse = namePrefix;
			var i = 2;

			while(cams.ContainsKey(nameToUse))
				nameToUse = $"{namePrefix}{i++}";

			return InitCamera(nameToUse, false);
		}

		public static void DeleteCamera(Cam2 cam) {
			if(!cams.Values.Contains(cam))
				return;

			if(cams[cam.name] != cam)
				return;

			cams.Remove(cam.name);

			var cfgPath = ConfigUtil.GetCameraPath(cam.name);

			GameObject.DestroyImmediate(cam);

			ConfigFiles.Flush();
			if(File.Exists(cfgPath))
				File.Delete(cfgPath);

			foreach(var x in ScenesManager.settings.scenes.Values)
				if(x.Contains(cam.name))
					x.RemoveAll(x => x == cam.name);

			foreach(var x in ScenesManager.settings.customScenes.Values)
				if(x.Contains(cam.name))
					x.RemoveAll(x => x == cam.name);

			ScenesManager.settings.Save();
		}

		public static bool RenameCamera(Cam2 cam, string newName) {
			if(cams.ContainsKey(newName))
				return false;

			if(!cams.ContainsValue(cam))
				return false;

			newName = string.Concat(newName.Split(Path.GetInvalidFileNameChars())).Trim();

			if(newName.Length == 0)
				return false;

			var oldName = cam.name;

			if(newName == oldName)
				return true;

			cams[newName] = cam;
			cams.Remove(oldName);

			foreach(var scene in ScenesManager.settings.scenes.Values) {
				if(!scene.Contains(oldName))
					continue;

				scene.Add(newName);
				scene.Remove(oldName);
			}

			cam.settings.Save();
			File.Move(cam.configPath, ConfigUtil.GetCameraPath(newName));
			cam.Init(newName, rename: true);
			ScenesManager.settings.Save();

			return true;
		}
	}
}
