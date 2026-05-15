namespace ReplayGenerator.Domain;

/// <summary>
/// 強震モニタ画像取得時のリトライ設定
/// </summary>
public class KyoshinImageRetryOptions
{
	/// <summary>
	/// 最大試行回数（初回含む）。1 以上を指定する。1 の場合はリトライ無し。
	/// </summary>
	public int MaxAttempts { get; init; } = 3;

	/// <summary>
	/// 初回リトライ前の待機時間
	/// </summary>
	public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// 指数バックオフ倍率。1.0 で固定間隔、2.0 で倍々
	/// </summary>
	public double BackoffFactor { get; init; } = 2.0;

	/// <summary>
	/// 各リトライ間の最大待機時間。指数バックオフがこの値を超えた場合はこの値でクランプする。
	/// </summary>
	public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// 環境変数から設定を読み込む
	/// </summary>
	public static KyoshinImageRetryOptions FromEnvironment()
	{
		return new KyoshinImageRetryOptions
		{
			MaxAttempts = ParseInt("KYOSHIN_IMAGE_RETRY_MAX_ATTEMPTS", 3, minValue: 1),
			InitialDelay = TimeSpan.FromMilliseconds(ParseInt("KYOSHIN_IMAGE_RETRY_INITIAL_DELAY_MS", 500, minValue: 0)),
			BackoffFactor = ParseDouble("KYOSHIN_IMAGE_RETRY_BACKOFF_FACTOR", 2.0, minValue: 1.0),
			MaxDelay = TimeSpan.FromMilliseconds(ParseInt("KYOSHIN_IMAGE_RETRY_MAX_DELAY_MS", 5000, minValue: 0)),
		};
	}

	/// <summary>
	/// 指定試行回（0 始まり、0 は初回）の前に挟む待機時間を計算する
	/// </summary>
	public TimeSpan GetDelayForAttempt(int attemptIndex)
	{
		if (attemptIndex <= 0)
			return TimeSpan.Zero;

		var ms = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attemptIndex - 1);
		if (double.IsInfinity(ms) || ms > MaxDelay.TotalMilliseconds)
			return MaxDelay;
		return TimeSpan.FromMilliseconds(ms);
	}

	private static int ParseInt(string key, int defaultValue, int minValue)
	{
		var raw = Environment.GetEnvironmentVariable(key);
		if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out var value))
			return defaultValue;
		return value < minValue ? minValue : value;
	}

	private static double ParseDouble(string key, double defaultValue, double minValue)
	{
		var raw = Environment.GetEnvironmentVariable(key);
		if (string.IsNullOrEmpty(raw) || !double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
			return defaultValue;
		return value < minValue ? minValue : value;
	}
}
