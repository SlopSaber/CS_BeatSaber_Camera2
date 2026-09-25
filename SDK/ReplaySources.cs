using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Camera2.Utils;
using UnityEngine;

namespace Camera2.SDK {
	public static class ReplaySources {
		internal static HashSet<ISource> sources = new HashSet<ISource>();

		public interface ISource {
			public string name { get; }
			public bool isInReplay { get; }
			public Vector3 localHeadPosition { get; }
			public Quaternion localHeadRotation { get; }
		}

		public class GenericSource : ISource {
			public string name { get; private set; }
			public bool isInReplay { get; private set; }
			public Vector3 localHeadPosition { get; private set; }
			public Quaternion localHeadRotation { get; private set; }

			public GenericSource(string name) {
				this.name = name;
			}

			public void Update(ref Vector3 localHeadPosition, ref Quaternion localHeadRotation) {
				this.localHeadPosition = localHeadPosition;
				this.localHeadRotation = localHeadRotation;
			}

			public void SetActive(bool isInReplay) {
				this.isInReplay = isInReplay;
			}
		}

		// Replay mods with a moving player origin can supply a world pose directly.
		public class WorldSource : GenericSource {
			public Vector3 worldHeadPosition { get; private set; }
			public Quaternion worldHeadRotation { get; private set; } = Quaternion.identity;
			public Transform originTransform { get; private set; }
			public bool hasOriginPose { get; private set; }
			public int poseJumpVersion { get; private set; }
			private bool hasPose;

			public WorldSource(string name) : base(name) { }

			public void UpdateWorld(Vector3 position, Quaternion rotation) {
				hasOriginPose = false;
				originTransform = null;
				if(!hasPose || (position - worldHeadPosition).sqrMagnitude > 25f || Quaternion.Angle(worldHeadRotation, rotation) > 25f)
					poseJumpVersion++;
				hasPose = true;
				worldHeadPosition = position;
				worldHeadRotation = rotation;
			}

			public void UpdateWorldFromOrigin(Transform originTransform, Vector3 localHeadPosition, Quaternion localHeadRotation) {
				Update(ref localHeadPosition, ref localHeadRotation);
				UpdateWorld(originTransform.TransformPoint(localHeadPosition), originTransform.rotation * localHeadRotation);
				this.originTransform = originTransform;
				hasOriginPose = true;
			}
		}

		public static void Register(ISource source) => sources.Add(source);

		public static void Unregister(ISource source) => sources.Remove(source);
	}
}
