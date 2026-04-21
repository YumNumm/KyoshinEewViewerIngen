using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace ReplayGenerator.Infrastructure;

public class ObjectStorageClient
{
	private readonly AmazonS3Client _client;
	private readonly string _bucket;
	private readonly ILogger<ObjectStorageClient> _logger;

	public ObjectStorageClient(string endpoint, string accessKey, string secretKey, string bucket, ILogger<ObjectStorageClient> logger)
	{
		_bucket = bucket;
		_logger = logger;

		var config = new AmazonS3Config
		{
			ServiceURL = endpoint,
			ForcePathStyle = true,
		};
		_client = new AmazonS3Client(accessKey, secretKey, config);
	}

	public async Task<long> UploadAsync(string key, Stream stream)
	{
		// PutObjectAsync はデフォルトで AutoCloseStream=true のためアップロード後に Stream を閉じる。
		// 閉じた後に Length へアクセスすると ObjectDisposedException になるため先に取得する。
		var length = stream.Length;
		var request = new PutObjectRequest
		{
			BucketName = _bucket,
			Key = key,
			InputStream = stream,
			ContentType = "application/octet-stream",
		};
		await _client.PutObjectAsync(request);

		_logger.LogInformation($"オブジェクトストレージにアップロード完了: {key} ({length} bytes)");
		return length;
	}
}
