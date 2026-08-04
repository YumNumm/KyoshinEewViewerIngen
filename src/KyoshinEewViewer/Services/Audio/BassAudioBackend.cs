using ManagedBass;
using System;

namespace KyoshinEewViewer.Services.Audio;

/// <summary>
/// BASS を使った既定の音声再生バックエンド
/// </summary>
public class BassAudioBackend : IAudioBackend
{
	private bool _initialized;

	public string LastError => Bass.LastError.ToString();

	public bool Initialize() => _initialized = Bass.Init();

	public void SetGlobalVolume(double volume)
		=> Bass.GlobalStreamVolume = (int)(Math.Clamp(volume, 0, 1) * 10000);

	public IAudioChannel? CreateChannel(string filePath)
	{
		var handle = Bass.CreateStream(filePath);
		return handle == 0 ? null : new BassAudioChannel(handle);
	}

	public void Dispose()
	{
		if (_initialized)
			Bass.Free();
		_initialized = false;
		GC.SuppressFinalize(this);
	}

	private sealed class BassAudioChannel(int handle) : IAudioChannel
	{
		// BASS はコールバックのデリゲートを参照しないため、GC されないようこちらで保持する
		private SyncProcedure? _endProcedure;
		private double _volume = 1;

		public double Volume
		{
			get => _volume;
			set {
				_volume = value;
				Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, (float)value);
			}
		}

		public bool IsStopped => Bass.ChannelIsActive(handle) == PlaybackState.Stopped;

		public bool Play() => Bass.ChannelPlay(handle);

		public void Rewind() => Bass.ChannelSetPosition(handle, 0);

		public void SlideVolumeToZero(TimeSpan duration)
			=> Bass.ChannelSlideAttribute(handle, ChannelAttribute.Volume, 0, (int)duration.TotalMilliseconds);

		public void SetEndCallback(Action callback)
		{
			_endProcedure = (_, _, _, _) => callback();
			Bass.ChannelSetSync(handle, SyncFlags.Onetime | SyncFlags.End, 0, _endProcedure);
		}

		public void Dispose()
		{
			Bass.StreamFree(handle);
			_endProcedure = null;
			GC.SuppressFinalize(this);
		}
	}
}
