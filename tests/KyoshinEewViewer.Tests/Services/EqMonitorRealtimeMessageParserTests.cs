using KyoshinEewViewer.Services.EqMonitor;
using Newtonsoft.Json;

namespace KyoshinEewViewer.Tests.Services;

public class EqMonitorRealtimeMessageParserTests
{
	[Theory(DisplayName = "WebSocket制御メッセージを判別できる")]
	[InlineData("""{"type":"ready"}""", typeof(EqMonitorReadyMessage))]
	[InlineData("""{"type":"ping"}""", typeof(EqMonitorPingMessage))]
	[InlineData("""{"type":"pong","pingId":"p1"}""", typeof(EqMonitorPongMessage))]
	public void 制御メッセージの判別(string json, Type expected)
		=> Assert.IsType(expected, EqMonitorRealtimeMessageParser.Parse(json));

	[Fact(DisplayName = "pongのpingIdを保持する")]
	public void PongのPingId()
	{
		var message = Assert.IsType<EqMonitorPongMessage>(
			EqMonitorRealtimeMessageParser.Parse("""{"type":"pong","pingId":"p1"}"""));

		Assert.Equal("p1", message.PingId);
	}

	[Fact(DisplayName = "EEW upsertを生成型へ変換する")]
	public void EewUpsert()
	{
		const string Json = """
		{"type":"realtime","data":{"type":"eew","operation":"upsert",
		"event_id":"20251212191438","record":{
		"event_id":"20251212191438","type":"VXSE45","status":"NORMAL",
		"info_type":"PUBLICATION","serial_no":3,"headline":null,
		"is_canceled":false,"is_warning":true,"is_last_info":false,
		"origin_time":"2025-12-12T19:14:38+09:00","arrival_time":null,
		"accuracy":null,"is_plum":false,"editorial_office":"仙台管区気象台",
		"report_time":"2025-12-12T19:14:50+09:00"}}}
		""";

		var message = Assert.IsType<EqMonitorEewUpsertMessage>(
			EqMonitorRealtimeMessageParser.Parse(Json));

		Assert.Equal("20251212191438", message.Record.Event_id);
		Assert.Equal(3, message.Record.Serial_no);
	}

	[Fact(DisplayName = "地震upsertを完全な生成型へ変換する")]
	public void 地震Upsert()
	{
		const string Json = """
		{"type":"realtime","data":{"type":"earthquake","operation":"upsert",
		"event_id":"20251212191438","record":{
		"event_id":"20251212191438","status":"NORMAL","earthquake_type":"NORMAL",
		"origin_time":"2025-12-12T19:14:38+09:00","origin_time_precision":"SECOND",
		"datasources":["JMA_DISASTER_INFORMATION_XML"],"telegrams":[]}}}
		""";

		var message = Assert.IsType<EqMonitorEarthquakeUpsertMessage>(
			EqMonitorRealtimeMessageParser.Parse(Json));

		Assert.Equal("20251212191438", message.Record.Event_id);
		Assert.Empty(message.Record.Telegrams);
	}

	[Fact(DisplayName = "地震deleteのイベントIDを取り出す")]
	public void 地震Delete()
	{
		var message = Assert.IsType<EqMonitorEarthquakeDeleteMessage>(
			EqMonitorRealtimeMessageParser.Parse(
				"""{"type":"realtime","data":{"type":"earthquake","operation":"delete","event_id":"20251212191438"}}"""));

		Assert.Equal("20251212191438", message.EventId);
	}

	[Fact(DisplayName = "未対応の業務イベントは接続を壊さないメッセージにする")]
	public void 未対応イベント()
	{
		var message = Assert.IsType<EqMonitorUnsupportedMessage>(
			EqMonitorRealtimeMessageParser.Parse(
				"""{"type":"realtime","data":{"type":"tsunami","operation":"upsert","event_id":"test"}}"""));

		Assert.Equal("tsunami", message.Type);
		Assert.Equal("upsert", message.Operation);
	}

	[Theory(DisplayName = "不正JSONまたは既知イベントの必須項目欠落は解析エラーにする")]
	[InlineData("{")]
	[InlineData("""{"type":"realtime","data":{"type":"earthquake","operation":"delete"}}""")]
	[InlineData("""{"type":"realtime","data":{"type":"eew","operation":"upsert","event_id":"test"}}""")]
	public void 不正メッセージ(string json)
		=> Assert.ThrowsAny<JsonException>(() => EqMonitorRealtimeMessageParser.Parse(json));
}
