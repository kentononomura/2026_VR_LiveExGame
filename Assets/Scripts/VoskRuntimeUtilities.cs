using System;
using System.Buffers;
using System.Threading;
using UnityEngine;
using Vosk;

/// <summary>
/// Vosk の大きなモデルをシーン間で共有し、Title から Test への遷移時に
/// 同じモデルを読み直さないためのプロセス内キャッシュです。
/// </summary>
public static class VoskModelCache
{
    private static readonly object SyncRoot = new object();
    private static Model cachedModel;
    private static string cachedPath;
    private static bool isLoading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterShutdownHandler()
    {
        Application.quitting -= DisposeCachedModel;
        Application.quitting += DisposeCachedModel;
    }

    /// <summary>
    /// バックグラウンドスレッドから呼び出してください。
    /// 同じモデルを別スレッドがロード中なら完了まで待機します。
    /// </summary>
    public static Model GetOrLoad(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            throw new ArgumentException("Vosk model path is empty.", nameof(modelPath));
        }

        lock (SyncRoot)
        {
            while (isLoading)
            {
                Monitor.Wait(SyncRoot);
            }

            if (cachedModel != null)
            {
                if (!string.Equals(cachedPath, modelPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"A different Vosk model is already cached. Cached: {cachedPath}, Requested: {modelPath}");
                }

                return cachedModel;
            }

            isLoading = true;
        }

        Model loadedModel = null;
        try
        {
            loadedModel = new Model(modelPath);
            lock (SyncRoot)
            {
                cachedModel = loadedModel;
                cachedPath = modelPath;
                return cachedModel;
            }
        }
        finally
        {
            lock (SyncRoot)
            {
                isLoading = false;
                Monitor.PulseAll(SyncRoot);
            }
        }
    }

    private static void DisposeCachedModel()
    {
        lock (SyncRoot)
        {
            cachedModel?.Dispose();
            cachedModel = null;
            cachedPath = null;
            isLoading = false;
            Monitor.PulseAll(SyncRoot);
        }
    }
}

/// <summary>
/// Unity の float 波形を Vosk 用 PCM16 に変換します。
/// バイト配列は ArrayPool から借り、ワーカースレッドで処理後に返却します。
/// </summary>
public static class VoskPcmUtility
{
    public const int MicrophoneBufferSeconds = 3;

    public static byte[] RentAndConvert(ReadOnlySpan<float> samples, out int byteCount)
    {
        byteCount = samples.Length * sizeof(short);
        byte[] pcmBytes = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));

        int byteIndex = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Math.Max(-1f, Math.Min(1f, samples[i]));
            short pcm = clamped <= -1f
                ? short.MinValue
                : (short)(clamped * short.MaxValue);

            pcmBytes[byteIndex++] = (byte)(pcm & 0xff);
            pcmBytes[byteIndex++] = (byte)((pcm >> 8) & 0xff);
        }

        return pcmBytes;
    }

    public static void Return(byte[] pcmBytes)
    {
        if (pcmBytes != null)
        {
            ArrayPool<byte>.Shared.Return(pcmBytes);
        }
    }
}
