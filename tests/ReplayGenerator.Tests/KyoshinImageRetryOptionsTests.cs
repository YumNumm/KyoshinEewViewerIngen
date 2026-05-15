using ReplayGenerator.Domain;
using Xunit;

namespace ReplayGenerator.Tests;

public class KyoshinImageRetryOptionsTests
{
	[Fact(DisplayName = "GetDelayForAttempt: 0 回目は待機なし")]
	public void GetDelayForAttempt_初回は待機なし()
	{
		var options = new KyoshinImageRetryOptions
		{
			InitialDelay = TimeSpan.FromMilliseconds(500),
			BackoffFactor = 2.0,
			MaxDelay = TimeSpan.FromSeconds(5),
		};

		Assert.Equal(TimeSpan.Zero, options.GetDelayForAttempt(0));
	}

	[Fact(DisplayName = "GetDelayForAttempt: 指数バックオフで待機時間が増える")]
	public void GetDelayForAttempt_指数バックオフ()
	{
		var options = new KyoshinImageRetryOptions
		{
			InitialDelay = TimeSpan.FromMilliseconds(500),
			BackoffFactor = 2.0,
			MaxDelay = TimeSpan.FromSeconds(10),
		};

		Assert.Equal(TimeSpan.FromMilliseconds(500), options.GetDelayForAttempt(1));
		Assert.Equal(TimeSpan.FromMilliseconds(1000), options.GetDelayForAttempt(2));
		Assert.Equal(TimeSpan.FromMilliseconds(2000), options.GetDelayForAttempt(3));
	}

	[Fact(DisplayName = "GetDelayForAttempt: MaxDelay でクランプされる")]
	public void GetDelayForAttempt_MaxDelayでクランプ()
	{
		var options = new KyoshinImageRetryOptions
		{
			InitialDelay = TimeSpan.FromMilliseconds(500),
			BackoffFactor = 10.0,
			MaxDelay = TimeSpan.FromSeconds(2),
		};

		Assert.Equal(TimeSpan.FromSeconds(2), options.GetDelayForAttempt(5));
	}
}
