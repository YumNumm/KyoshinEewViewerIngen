using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Generated = KyoshinEewViewer.EqMonitorApi.Generated;

namespace KyoshinEewViewer.Services.EqMonitor;

/// <summary>
/// EQMonitor WebSocket の JSON をアプリ内の型付きメッセージへ変換する
/// </summary>
public static class EqMonitorRealtimeMessageParser
{
	public static EqMonitorRealtimeMessage Parse(string json)
	{
		var root = JObject.Parse(json);
		var type = GetRequiredString(root, "type");
		return type switch
		{
			"ready" => new EqMonitorReadyMessage(),
			"ping" => new EqMonitorPingMessage(),
			"pong" => new EqMonitorPongMessage(root.Value<string>("pingId")),
			"realtime" => ParseRealtime(GetRequiredObject(root, "data")),
			_ => new EqMonitorUnsupportedMessage(type, null),
		};
	}

	private static EqMonitorRealtimeMessage ParseRealtime(JObject data)
	{
		var type = GetRequiredString(data, "type");
		if (type == "earthquake")
		{
			var operation = GetRequiredString(data, "operation");
			return operation switch
			{
				"upsert" => new EqMonitorEarthquakeUpsertMessage(
					GetRequiredRecord<Generated.Earthquake>(data)),
				"delete" => new EqMonitorEarthquakeDeleteMessage(
					GetRequiredString(data, "event_id")),
				_ => new EqMonitorUnsupportedMessage(type, operation),
			};
		}

		if (type == "eew")
		{
			var operation = GetRequiredString(data, "operation");
			return operation == "upsert"
				? new EqMonitorEewUpsertMessage(
					GetRequiredRecord<Generated.EewItemWithRelations>(data))
				: new EqMonitorUnsupportedMessage(type, operation);
		}

		return new EqMonitorUnsupportedMessage(type, data.Value<string>("operation"));
	}

	private static JObject GetRequiredObject(JObject source, string propertyName)
		=> source[propertyName] as JObject
			?? throw new JsonSerializationException(
				$"EQMonitor WebSocket メッセージの {propertyName} がオブジェクトではありません");

	private static string GetRequiredString(JObject source, string propertyName)
	{
		var value = source.Value<string>(propertyName);
		if (string.IsNullOrEmpty(value))
			throw new JsonSerializationException(
				$"EQMonitor WebSocket メッセージに {propertyName} がありません");
		return value;
	}

	private static T GetRequiredRecord<T>(JObject data)
	{
		// canonical envelope では upsert の event_id と record が共に必須
		_ = GetRequiredString(data, "event_id");
		var token = data["record"]
			?? throw new JsonSerializationException(
				"EQMonitor WebSocket の upsert メッセージに record がありません");
		return token.ToObject<T>()
			?? throw new JsonSerializationException(
				"EQMonitor WebSocket の record を解析できませんでした");
	}
}
