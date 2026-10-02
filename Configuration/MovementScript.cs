using Camera2.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using UnityEngine;

namespace Camera2.Configuration {

#if DEBUG
	public
#endif
	class MovementScript {
		//public enum PositionType {
		//	Absolute,
		//	Relative
		//}
		public enum MoveType {
			Linear,
			Eased
		}

		public class Frame {
			//[JsonConverter(typeof(StringEnumConverter))]
			//public PositionType posType = PositionType.Absolute;
			[JsonConverter(typeof(StringEnumConverter)), DefaultValue(MoveType.Linear)]
			public MoveType transition = MoveType.Linear;
			[JsonConverter(typeof(Vector3Converter))]
			public Vector3 position = Vector3.zero;

			[JsonIgnore]
			public Quaternion rotation = Quaternion.identity;
			[JsonConverter(typeof(Vector3Converter)), JsonProperty("rotation")]
			public Vector3 rotationEuler {
				get { return rotation.eulerAngles; }
				set { rotation = Quaternion.Euler(value); }
			}


			[DefaultValue(0f)]
			public float FOV = 0f;
			public float duration = 0f;
			public float holdTime = 0f;

			[JsonIgnore]
			public float startTime = 0f;
			[JsonIgnore]
			public float transitionEndTime = 0f;
			[JsonIgnore]
			public float endTime = 0f;
		}

		[JsonProperty("syncToSong")]
		public bool syncToSong { get; private set; } = false;

		[JsonProperty("loop")]
		public bool loop { get; private set; } = true;

		public List<Frame> frames { get; private set; } = new List<Frame>();

		[JsonIgnore]
		public float scriptDuration { get; private set; } = 0f;

		private void PopulateTimes() {
			var time = 0f;
			foreach(var frame in frames) {
				frame.startTime = time;
				time = frame.transitionEndTime =
					time + frame.duration;

				time = frame.endTime =
					time + frame.holdTime;
			}
			scriptDuration = time;
		}

		public static MovementScript Load(string name) {
			ConfigFiles.Flush();
			var scriptPath = ConfigUtil.GetMovementScriptPath(name);
			var file = ConfigFiles.Read(scriptPath, true, System.Threading.CancellationToken.None);
			if(!file.exists)
				return null;
			var script = LoadPrepared(file);
			if(file.legacyMovement) {
				File.Move(scriptPath, $"{scriptPath}.cameraPlusFormat");
				File.WriteAllText(scriptPath, script.MigrationText());
			}
			return script;
		}

		internal static MovementScript LoadPrepared(ConfigFile file) {
			if(file.error != null) throw file.error;
			var script = new MovementScript();
			// Not a Noodle movement script
			if(!file.legacyMovement) {
				file.Populate(script);
			} else {
				// Camera Plus movement script, we need to convert it...
				dynamic camPlusScript = file.data;

				script.syncToSong = camPlusScript.activeinpausemenu != "true";

				foreach(dynamic movement in camPlusScript.movements) {
					script.frames.Add(new Frame() {
						position = new Vector3((float)movement.startpos.x, (float)movement.startpos.y, (float)movement.startpos.z),
						rotationEuler = new Vector3((float)movement.startrot.x, (float)movement.startrot.y, (float)movement.startrot.z),
						FOV = (float)(movement.startpos.fov ?? 0f)
					});

					script.frames.Add(new Frame() {
						position = new Vector3((float)movement.endpos.x, (float)movement.endpos.y, (float)movement.endpos.z),
						rotationEuler = new Vector3((float)movement.endrot.x, (float)movement.endrot.y, (float)movement.endrot.z),
						duration = movement.duration,
						holdTime = movement.delay,
						FOV = (float)(movement.endpos.fov ?? 0f),
						transition = movement.easetransition == "true" ? MoveType.Eased : MoveType.Linear
					});
				}

			}

			//if(frames[0].posType == PositionType.Relative) {
			//	Plugin.Log.Warn("The first frame in a Movement script cannot have a relative position, not loaded");
			//	return null;
			//}

			script.PopulateTimes();

			return script;
		}

		internal string MigrationText() => JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings {
			DefaultValueHandling = DefaultValueHandling.Ignore
		});
	}
}
