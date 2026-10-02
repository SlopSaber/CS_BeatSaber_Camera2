using Camera2.Interfaces;
using Camera2.VMC;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Net;
using UnityEngine;

namespace Camera2.Configuration {
	enum VMCMode {
		Disabled,
		Sender,
		//Receiver
	}

	class Settings_VMCAvatar : CameraSubSettings {
		VMCMode _mode = VMCMode.Disabled;
		[JsonConverter(typeof(StringEnumConverter))]
		public VMCMode mode {
			get => _mode;
			set {
				if(_mode == value) return;
				_mode = value;
				settings.cam.GetComponent<Middlewares.VMCAvatar>()?.ResetSender();
			}
		}

		public string destination {
			get => address.ToString();
			set {
				string[] stuff = value.Split(':');

				var parsedAddress = IPAddress.Parse(stuff[0]);
				var b = parsedAddress.GetAddressBytes();

				if(!IPAddress.IsLoopback(parsedAddress) && b[0] != 10 && (b[0] != 192 || b[1] != 168) && (b[0] != 172 || (b[1] < 16 || b[1] > 31))) {
					Plugin.Log.Warn($"Tried to set public IP address ({value}) for camera {settings.cam.name} as the VMC destination. As this is almost certainly not intended it was prevented");
					return;
				}

				var parsedPort = stuff.Length == 2 ? ushort.Parse(stuff[1]) : 39540;
				address.Address = parsedAddress;
				address.Port = parsedPort;
				settings.cam.GetComponent<Middlewares.VMCAvatar>()?.ResetSender();
			}
		}

		[JsonIgnore]
		public IPEndPoint address = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 39540);
	}
}

namespace Camera2.Middlewares {
	class VMCAvatar : CamMiddleware, IMHandler {
		OscClient sender;
		string senderDestination;

		float prevFov;
		Vector3 prevPos;
		Quaternion prevRot;
		bool hasPrevious;

		internal void ResetSender() {
			sender?.Dispose();
			sender = null;
			senderDestination = null;
			hasPrevious = false;
		}

		public void OnDisable() => ResetSender();
		public void OnDestroy() => ResetSender();
		new public void CamConfigReloaded() => ResetSender();

		new public void Post() {
			if(cam.settings.VMCProtocol.mode == Configuration.VMCMode.Disabled) {
				ResetSender();
				return;
			}

			var destination = cam.settings.VMCProtocol.address.ToString();
			if(sender != null && senderDestination != destination) ResetSender();
			var fov = cam.settings.FOV;
			var position = cam.transformchain.position;
			var rotation = cam.transformchain.rotation;
			if(hasPrevious && prevFov == fov && prevPos == position && prevRot == rotation)
				return;

			try {
				sender ??= new OscClient(cam.settings.VMCProtocol.address.Address.GetAddressBytes(), cam.settings.VMCProtocol.address.Port);
				senderDestination = destination;

				sender.QueuePose(position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w, fov);
			} catch { } finally {
				prevFov = fov;
				prevPos = position;
				prevRot = rotation;
				hasPrevious = true;
			}
		}
	}
}
