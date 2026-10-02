using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Camera2.Utils {
	sealed class ConfigFile {
		internal string path;
		internal bool exists;
		internal bool legacyMovement;
		internal JToken data;
		internal Exception error;

		internal void Populate(object target) {
			if(error != null) throw error;
			using(var reader = data.CreateReader())
				JsonSerializer.Create(JsonHelpers.leanDeserializeSettings).Populate(reader, target);
		}
	}

	sealed class ConfigInputs {
		internal ConfigFile[] cameras;
		internal ConfigFile[] movements;
		internal ConfigFile scenes;
	}

	sealed class MovementMigration {
		internal string path;
		internal string text;
	}

	static class ConfigFiles {
		static readonly object gate = new object();
		static Task tail = Task.CompletedTask;
		static readonly HashSet<Task> active = new HashSet<Task>();
		static readonly CancellationTokenSource lifetime = new CancellationTokenSource();
		internal static CancellationToken LifetimeToken => lifetime.Token;
		internal static bool stopped { get; private set; }

		internal static void Retain(Task task) {
			lock(gate) active.Add(task);
			_ = task.ContinueWith(completed => {
				_ = completed.Exception;
				lock(gate) active.Remove(completed);
			}, TaskScheduler.Default);
		}

		static Task<T> Queue<T>(Func<T> operation) {
			lock(gate) {
				var previous = tail;
				var task = Task.Run(async () => {
					try { await previous.ConfigureAwait(false); } catch { }
					return operation();
				});
				tail = task;
				return task;
			}
		}

		internal static Task<ConfigInputs> ReadInputsAsync(string camerasDir, string movementsDir, string scenesPath, CancellationToken token) => Queue(() => {
			var inputs = new ConfigInputs {
				cameras = ReadCatalog(camerasDir, false, token),
				movements = ReadCatalog(movementsDir, true, token),
				scenes = Read(scenesPath, false, token)
			};
			token.ThrowIfCancellationRequested();
			return inputs;
		});

		static ConfigFile[] ReadCatalog(string directory, bool movement, CancellationToken token) {
			token.ThrowIfCancellationRequested();
			Directory.CreateDirectory(directory);
			var paths = Directory.GetFiles(directory, "*.json");
			var files = new ConfigFile[paths.Length];
			for(var i = 0; i < paths.Length; i++) files[i] = Read(paths[i], movement, token);
			return files;
		}

		internal static ConfigFile Read(string path, bool movement, CancellationToken token) {
			token.ThrowIfCancellationRequested();
			var file = new ConfigFile { path = path };
			try {
				if(!(file.exists = File.Exists(path))) return file;
				var text = File.ReadAllText(path);
				token.ThrowIfCancellationRequested();
				file.legacyMovement = movement && text.Contains("Movements");
				using(var source = new StringReader(file.legacyMovement ? text.ToLowerInvariant() : text))
				using(var reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None }) {
					file.data = JToken.ReadFrom(reader);
					while(reader.Read())
						if(reader.TokenType != JsonToken.Comment) throw new JsonReaderException("Additional text after the JSON value.");
				}
			} catch(OperationCanceledException) { throw; }
			catch(Exception ex) { file.error = ex; }
			return file;
		}

		internal static Task WriteAsync(string path, string text) => Queue(() => {
			File.WriteAllText(path, text);
			return true;
		});

		internal static Task<Exception[]> MigrateAsync(MovementMigration[] migrations, CancellationToken token) => Queue(() => {
			var errors = new Exception[migrations.Length];
			for(var i = 0; i < migrations.Length; i++) {
				token.ThrowIfCancellationRequested();
				var migration = migrations[i];
				if(migration == null) continue;
				try {
					File.Move(migration.path, migration.path + ".cameraPlusFormat");
					// Finish the accepted replacement even if cancellation arrives after the move.
					File.WriteAllText(migration.path, migration.text);
				} catch(Exception ex) { errors[i] = ex; }
			}
			return errors;
		});

		internal static void Flush() {
			Task pending;
			lock(gate) pending = tail;
			try { pending.GetAwaiter().GetResult(); } catch { }
		}

		internal static void Stop() {
			stopped = true;
			lifetime.Cancel();
			Flush();
		}

		internal static async Task ObserveWriteAsync(Task task, string description) {
			try { await task.ConfigureAwait(false); }
			catch(Exception ex) {
				await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					if(stopped) return;
					Plugin.Log.Error(description);
					Plugin.Log.Error(ex);
				});
			}
		}
	}
}
