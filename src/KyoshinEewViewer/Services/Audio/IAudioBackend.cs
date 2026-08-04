using System;

namespace KyoshinEewViewer.Services.Audio;

/// <summary>
/// 音声再生のバックエンド。
///
/// 既定は BASS を使う <see cref="BassAudioBackend"/> だが、BASS のネイティブが存在しない
/// プラットフォーム (iOS) ではヘッド側で実装を Splat に登録して差し替える。
/// </summary>
public interface IAudioBackend : IDisposable
{
	/// <summary>
	/// 直前に失敗した処理の理由。ログ出力にのみ使う
	/// </summary>
	string LastError { get; }

	/// <summary>
	/// 初期化する。失敗した場合は false
	/// </summary>
	bool Initialize();

	/// <summary>
	/// すべてのチャンネルに掛かる音量を設定する (0-1)
	/// </summary>
	void SetGlobalVolume(double volume);

	/// <summary>
	/// ファイルを読み込んで再生用のチャンネルを作成する。失敗した場合は null
	/// </summary>
	IAudioChannel? CreateChannel(string filePath);
}

/// <summary>
/// 1 つの音声ファイルに対応する再生チャンネル。
/// 破棄するまで再生を繰り返せる
/// </summary>
public interface IAudioChannel : IDisposable
{
	/// <summary>
	/// このチャンネル単体の音量。1 を超える増幅も許容する
	/// (BASS の ChannelAttribute.Volume と同じ扱い)
	/// </summary>
	double Volume { get; set; }

	/// <summary>
	/// 停止しているか。再生中・一時停止中はいずれも false
	/// </summary>
	bool IsStopped { get; }

	/// <summary>
	/// 再生を開始する。停止状態なら先頭から、それ以外は現在位置から続ける。
	/// 失敗した場合は false
	/// </summary>
	bool Play();

	/// <summary>
	/// 再生位置を先頭に戻す
	/// </summary>
	void Rewind();

	/// <summary>
	/// 指定時間をかけて音量を 0 までスライドさせる (中断時のフェードアウト用)
	/// </summary>
	void SlideVolumeToZero(TimeSpan duration);

	/// <summary>
	/// 終端まで再生されたときに一度だけ呼ばれるコールバックを登録する。
	/// UI スレッドとは別のスレッドから呼ばれる
	/// </summary>
	void SetEndCallback(Action callback);
}
