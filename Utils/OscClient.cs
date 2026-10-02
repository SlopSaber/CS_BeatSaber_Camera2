using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Camera2.VMC {
	sealed class OscClient : IDisposable {
		sealed class Pose {
			internal OscClient client;
			internal float[] values;
			internal long sequence;
		}

		static readonly object gate = new object();
		static readonly HashSet<OscClient> clients = new HashSet<OscClient>();
		static readonly Dictionary<OscClient, Pose> pending = new Dictionary<OscClient, Pose>();
		static readonly HashSet<Task> jobs = new HashSet<Task>();
		static readonly byte[] template = Encoding.ASCII.GetBytes(
			"/VMC/Ext/Cam\0\0\0\0" + ",sffffffff\0\0" + "Camera\0\0" +
			"XPOSYPOSZPOSXROTYROTZROTWROT_FOV"
		);
		static Task sendingTask;
		static bool sending;
		static int generation;
		static long sequence;
		readonly byte[] address;
		readonly int port;
		bool disposed;

		public OscClient(byte[] address, int port) {
			this.address = (byte[])address.Clone();
			this.port = port;
			lock(gate) clients.Add(this);
		}

		public void QueuePose(float px, float py, float pz, float rx, float ry, float rz, float rw, float fov) {
			lock(gate) {
				if(disposed) return;
				// Retain one pending pose per camera; emit surviving requests in submission order.
				pending[this] = new Pose { client = this, values = new[] { px, py, pz, rx, ry, rz, rw, fov }, sequence = ++sequence };
				if(sending) return;
				sending = true;
				var version = ++generation;
				sendingTask = Task.Run(() => SendPending(version));
				jobs.Add(sendingTask);
				_ = sendingTask.ContinueWith(completed => {
					_ = completed.Exception;
					lock(gate) jobs.Remove(completed);
				}, TaskScheduler.Default);
			}
		}

		static void SendPending(int version) {
			try {
				using(var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) {
					socket.SendTimeout = 250;
					socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
					for(; ;) {
						Pose pose;
						lock(gate) {
							if(pending.Count == 0) {
								sending = false;
								return;
							}
							pose = pending.Values.OrderBy(x => x.sequence).First();
							pending.Remove(pose.client);
							if(pose.client.disposed) continue;
						}
						var packet = Encode(pose.values);
						var destination = new IPEndPoint(new IPAddress(pose.client.address), pose.client.port);
						lock(gate) {
							if(pose.client.disposed) continue;
						}
						try { socket.SendTo(packet, destination); } catch { }
					}
				}
			} catch { }
			finally {
				lock(gate) {
					if(version == generation && sending) {
						pending.Clear();
						sending = false;
					}
				}
			}
		}

		static unsafe byte[] Encode(float[] pose) {
			var packet = (byte[])template.Clone();
			fixed(byte* ptr = packet) {
				var output = (uint*)(ptr + packet.Length - 4 * pose.Length);
				for(var i = 0; i < pose.Length; i++) {
					var value = pose[i];
					var bytes = (byte*)&value;
					output[i] = (uint)(bytes[0] << 24) | (uint)(bytes[1] << 16) | (uint)(bytes[2] << 8) | bytes[3];
				}
			}
			return packet;
		}

		public void Dispose() {
			lock(gate) {
				disposed = true;
				pending.Remove(this);
				clients.Remove(this);
			}
		}

		internal static void StopAll() {
			lock(gate) {
				foreach(var client in clients) client.disposed = true;
				clients.Clear();
				pending.Clear();
			}
		}
	}
}
