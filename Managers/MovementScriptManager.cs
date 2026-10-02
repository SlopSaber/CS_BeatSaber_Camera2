using Camera2.Configuration;
using Camera2.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Camera2.Managers {

#if DEBUG
	public
#endif
	static class MovementScriptManager {
		public static Dictionary<string, MovementScript> movementScripts { get; private set; } = new Dictionary<string, MovementScript>();

		internal sealed class PreparedBatch {
			internal string[] names;
			internal MovementScript[] scripts;
			internal Exception[] errors;
			internal MovementMigration[] migrations;
		}

		internal static PreparedBatch Prepare(ConfigFile[] files) {
			var batch = new PreparedBatch {
				names = new string[files.Length], scripts = new MovementScript[files.Length],
				errors = new Exception[files.Length], migrations = new MovementMigration[files.Length]
			};
			for(var i = 0; i < files.Length; i++) {
				var file = files[i];
				batch.names[i] = Path.GetFileNameWithoutExtension(file.path);
				try {
					var script = MovementScript.LoadPrepared(file);
					if(file.legacyMovement)
						batch.migrations[i] = new MovementMigration { path = file.path, text = script.MigrationText() };
					if(script.frames.Count < 2) throw new Exception("Movement scripts must contain at least two keyframes");
					batch.scripts[i] = script;
				} catch(Exception ex) { batch.errors[i] = ex; }
			}
			return batch;
		}

		internal static void Apply(PreparedBatch batch, Exception[] migrationErrors, bool reload) {
			var loaded = new HashSet<string>();
			for(var i = 0; i < batch.names.Length; i++) {
				var error = batch.errors[i] ?? migrationErrors[i];
				if(error != null) {
					Plugin.Log.Error($"Failed to load Movement script {batch.names[i]}.json");
					Plugin.Log.Error(error);
					continue;
				}
				movementScripts[batch.names[i]] = batch.scripts[i];
				loaded.Add(batch.names[i]);
			}
			if(reload) foreach(var name in movementScripts.Keys.Where(x => !loaded.Contains(x)).ToArray())
				movementScripts.Remove(name);
		}

		public static void LoadMovementScripts(bool reload = false) {
			if(!Directory.Exists(ConfigUtil.MovementScriptsDir)) {
				Directory.CreateDirectory(ConfigUtil.MovementScriptsDir);
			} else {
				var loadedNames = new List<string>();

				foreach(var cam in Directory.GetFiles(ConfigUtil.MovementScriptsDir, "*.json")) {
					try {
						var name = Path.GetFileNameWithoutExtension(cam);

						var script = MovementScript.Load(name);

						if(script.frames.Count() < 2)
							throw new Exception("Movement scripts must contain at least two keyframes");

#if DEBUG
						Plugin.Log.Info($"Loaded Movement script {name}");
						Plugin.Log.Info($"Sync to song: {script.syncToSong}");
						Plugin.Log.Info($"Duration: {script.scriptDuration} ({script.frames.Count()} frames)");
#endif

						movementScripts[name] = script;

						if(reload && script != null)
							loadedNames.Add(name);
					} catch(Exception ex) {
						Plugin.Log.Error($"Failed to load Movement script {Path.GetFileName(cam)}");
						Plugin.Log.Error(ex);
					}
				}
				if(reload) foreach(var deletedScript in movementScripts.Where(x => !loadedNames.Contains(x.Key))) {
						movementScripts.Remove(deletedScript.Key);
					}
			}
		}
	}
}
