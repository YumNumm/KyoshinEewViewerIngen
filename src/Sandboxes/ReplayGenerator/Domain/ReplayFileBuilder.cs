using KyoshinEewViewer.Core.Models.EarthquakeReplay;
using KyoshinMonitorLib;
using KyoshinMonitorLib.UrlGenerator;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ReplayGenerator.Domain;

public class ReplayFileBuilder
{
	private readonly HttpClient _httpClient;
	private readonly ILogger<ReplayFileBuilder> _logger;
	private readonly string? _internalApiUrl;
	private readonly KyoshinImageRetryOptions _imageRetryOptions;

	public ReplayFileBuilder(HttpClient httpClient, ILogger<ReplayFileBuilder> logger, string? internalApiUrl = null, KyoshinImageRetryOptions? imageRetryOptions = null)
	{
		_httpClient = httpClient;
		_logger = logger;
		_internalApiUrl = internalApiUrl;
		_imageRetryOptions = imageRetryOptions ?? new KyoshinImageRetryOptions();
	}

	/// <summary>
	/// 指定期間の強震モニタ画像と EEW JSON を取得してリプレイファイルを組み立てる
	/// </summary>
	public async Task<(byte[] FileBytes, string FileName)> BuildAsync(DateTime startTime, DateTime endTime, string? snapshotJson = null)
	{
		var data = new List<ReplayData>();
		var current = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, startTime.Minute, startTime.Second, DateTimeKind.Utc);

		while (current <= endTime)
		{
			try
			{
				var imageData = await FetchKyoshinImageAsync(current);
				if (imageData != null)
				{
					data.Add(new KyoshinMonitorImageReplayData
					{
						Time = current,
						Images = new Dictionary<KyoshinMonitorImageReplayData.ImageType, byte[]>
						{
							[KyoshinMonitorImageReplayData.ImageType.Shindo] = imageData,
						},
					});
				}

				var eewJsonResult = await FetchKyoshinEewJsonAsync(current);
				if (eewJsonResult != null)
				{
					data.Add(new KyoshinMonitorEewJsonReplayData
					{
						Time = current,
						Json = eewJsonResult,
					});
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"強震モニタデータの取得に失敗しました: {current:HH:mm:ss} - {ex.Message}");
			}

			current = current.AddSeconds(1);
		}

		if (snapshotJson != null)
			InjectEewsFromSnapshot(data, snapshotJson, startTime, endTime);

		data.Sort((a, b) => a.Time.CompareTo(b.Time));

		var fileName = $"{startTime:yyyyMMdd_HHmmss}_{endTime:HHmmss}.eqrp";
		var fileBytes = await PackReplayFile(data, startTime, endTime);

		_logger.LogInformation($"リプレイファイルを生成しました: {fileName} ({data.Count} データ, {fileBytes.Length} bytes)");
		return (fileBytes, fileName);
	}

	private void InjectEewsFromSnapshot(List<ReplayData> data, string snapshotJson, DateTime startTime, DateTime endTime)
	{
		try
		{
			using var doc = JsonDocument.Parse(snapshotJson);
			if (!doc.RootElement.TryGetProperty("eews", out var eews))
				return;

			foreach (var eew in eews.EnumerateArray())
			{
				if (!eew.TryGetProperty("reportTime", out var reportTimeProp))
					continue;
				if (!DateTime.TryParse(reportTimeProp.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var reportTime))
					continue;
				if (reportTime < startTime || reportTime > endTime)
					continue;

				data.Add(new EqMonitorEewReplayData
				{
					Time = reportTime,
					Json = eew.GetRawText(),
				});
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning($"EEWスナップショットの注入に失敗しました: {ex.Message}");
		}
	}

	internal async Task<byte[]?> FetchKyoshinImageAsync(DateTime time)
	{
		var url = WebApiUrlGenerator.Generate(WebApiUrlType.RealtimeImg, time, RealtimeDataType.Shindo, false);
		var maxAttempts = Math.Max(1, _imageRetryOptions.MaxAttempts);

		for (var attempt = 0; attempt < maxAttempts; attempt++)
		{
			var delay = _imageRetryOptions.GetDelayForAttempt(attempt);
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay);

			try
			{
				var response = await _httpClient.GetAsync(url);
				if (response.IsSuccessStatusCode)
					return await response.Content.ReadAsByteArrayAsync();

				if (attempt + 1 >= maxAttempts)
				{
					_logger.LogWarning($"強震モニタ画像の取得に失敗しました（リトライ上限到達）: {time:HH:mm:ss} status={(int)response.StatusCode} attempts={attempt + 1}");
					return null;
				}

				_logger.LogDebug($"強震モニタ画像の取得に失敗しリトライします: {time:HH:mm:ss} status={(int)response.StatusCode} attempt={attempt + 1}/{maxAttempts}");
			}
			catch (Exception ex)
			{
				if (attempt + 1 >= maxAttempts)
				{
					_logger.LogWarning(ex, $"強震モニタ画像の取得に失敗しました（リトライ上限到達・例外）: {time:HH:mm:ss} attempts={attempt + 1}");
					return null;
				}

				_logger.LogDebug(ex, $"強震モニタ画像の取得で例外が発生しリトライします: {time:HH:mm:ss} attempt={attempt + 1}/{maxAttempts}");
			}
		}

		return null;
	}

	private async Task<string?> FetchKyoshinEewJsonAsync(DateTime time)
	{
		var url = WebApiUrlGenerator.Generate(WebApiUrlType.EewJson, time);
		try
		{
			var response = await _httpClient.GetAsync(url);
			if (!response.IsSuccessStatusCode) return null;
			return await response.Content.ReadAsStringAsync();
		}
		catch
		{
			return null;
		}
	}

	private static async Task<byte[]> PackReplayFile(List<ReplayData> data, DateTime startTime, DateTime endTime)
	{
		using var ms = new MemoryStream();
		var reader = new KyoshinReplayFileReader(ms);

		var header = new ReplayFileHeader
		{
			SoftwareName = "EQMonitor-ReplayGenerator",
			StartTime = startTime,
			EndTime = endTime,
			CompressionMode = ReplayFileCompressionMode.GZip,
		};

		await reader.WriteHeader(header);
		await reader.WriteData(data.ToArray(), ReplayFileCompressionMode.GZip);

		return ms.ToArray();
	}
}
