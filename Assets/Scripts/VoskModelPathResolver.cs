using System;
using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

/// <summary>
/// Voskが要求する「実在するモデルフォルダー」をプラットフォームごとに用意する。
/// AndroidのStreamingAssetsはAPK内部にあるため、初回のみpersistentDataPathへ展開する。
/// </summary>
public static class VoskModelPathResolver
{
    private const string FileListName = "vosk-files.txt";
    private const string InstallMarkerName = ".vosk-model-ready";
    private const string InstallVersion = "2";

    public static IEnumerator Prepare(
        string modelFolderName,
        Action<string> onReady,
        Action<string> onError,
        Action<float, string> onProgress = null)
    {
        if (string.IsNullOrWhiteSpace(modelFolderName))
        {
            onError?.Invoke("Voskモデルフォルダー名が空です。");
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        yield return PrepareAndroidModel(modelFolderName, onReady, onError, onProgress);
#else
        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);
        if (!Directory.Exists(modelPath))
        {
            onError?.Invoke($"Voskモデルが見つかりません: {modelPath}");
            yield break;
        }

        onProgress?.Invoke(1f, modelFolderName);
        onReady?.Invoke(modelPath);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static IEnumerator PrepareAndroidModel(
        string modelFolderName,
        Action<string> onReady,
        Action<string> onError,
        Action<float, string> onProgress)
    {
        string destinationRoot = Path.Combine(
            Application.persistentDataPath,
            "VoskModels",
            modelFolderName);
        string markerPath = Path.Combine(destinationRoot, InstallMarkerName);

        if (IsInstalledModelValid(destinationRoot, markerPath))
        {
            Debug.Log($"[Vosk] 展開済みQuestモデルを使用します: {destinationRoot}");
            onReady?.Invoke(destinationRoot);
            yield break;
        }

        string sourceRoot = BuildStreamingAssetUrl(modelFolderName);
        string fileListUrl = sourceRoot + "/" + FileListName;
        string fileListText;

        using (UnityWebRequest request = UnityWebRequest.Get(fileListUrl))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(
                    $"Quest用Voskファイル一覧の読み込みに失敗しました: {request.error} / {fileListUrl}");
                yield break;
            }

            fileListText = request.downloadHandler.text;
        }

        string[] files = fileListText.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        if (files.Length == 0)
        {
            onError?.Invoke("Quest用Voskファイル一覧が空です。");
            yield break;
        }

        Directory.CreateDirectory(destinationRoot);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        Debug.Log($"[Vosk] Quest用日本語モデルを端末へ展開します（{files.Length}ファイル）。初回のみ時間がかかります。");

        int copiedCount = 0;
        int reusedCount = 0;
        int processedCount = 0;
        foreach (string rawEntry in files)
        {
            string[] entryParts = rawEntry.Trim().Split('|');
            string relativePath = entryParts[0].Trim().Replace('\\', '/');
            long expectedSize = -1;
            if (entryParts.Length >= 2)
            {
                if (!long.TryParse(entryParts[1].Trim(), out expectedSize))
                {
                    expectedSize = -1;
                }
            }

            if (string.IsNullOrEmpty(relativePath) || relativePath.StartsWith("#"))
            {
                continue;
            }

            if (relativePath.Contains(".."))
            {
                onError?.Invoke($"不正なVoskモデル相対パスです: {relativePath}");
                yield break;
            }

            string sourceUrl = sourceRoot + "/" + EscapeRelativeUrl(relativePath);
            string destinationPath = Path.Combine(
                destinationRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (expectedSize >= 0 &&
                File.Exists(destinationPath) &&
                new FileInfo(destinationPath).Length == expectedSize)
            {
                reusedCount++;
                processedCount++;
                onProgress?.Invoke((float)processedCount / files.Length, relativePath);
                continue;
            }

            // UnityWebRequestはAndroid APK内の0バイトAssetを正常取得できない場合がある。
            // Voskモデルの空設定ファイルは端末側で直接生成する。
            if (expectedSize == 0)
            {
                try
                {
                    File.WriteAllBytes(destinationPath, Array.Empty<byte>());
                }
                catch (Exception ex)
                {
                    onError?.Invoke(
                        $"Quest用Vosk空設定ファイルを書き込めませんでした: " +
                        $"{destinationPath} / {ex.Message}");
                    yield break;
                }

                copiedCount++;
                processedCount++;
                onProgress?.Invoke((float)processedCount / files.Length, relativePath);
                continue;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(sourceUrl))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(
                        $"Quest用Voskモデルの展開に失敗しました: {relativePath} / {request.error}");
                    yield break;
                }

                try
                {
                    byte[] downloadedData = request.downloadHandler.data;
                    if (expectedSize >= 0 && downloadedData.LongLength != expectedSize)
                    {
                        onError?.Invoke(
                            $"Quest用Voskモデルのサイズが一致しません: {relativePath} / " +
                            $"Expected={expectedSize}, Actual={downloadedData.LongLength}");
                        yield break;
                    }

                    File.WriteAllBytes(destinationPath, downloadedData);
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Quest用Voskモデルを書き込めませんでした: {destinationPath} / {ex.Message}");
                    yield break;
                }
            }

            copiedCount++;
            processedCount++;
            onProgress?.Invoke((float)processedCount / files.Length, relativePath);
        }

        try
        {
            File.WriteAllText(markerPath, InstallVersion);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Voskモデルの展開完了情報を書き込めませんでした: {ex.Message}");
            yield break;
        }

        if (!IsInstalledModelValid(destinationRoot, markerPath))
        {
            onError?.Invoke("Questへ展開したVoskモデルの検証に失敗しました。");
            yield break;
        }

        Debug.Log(
            $"[Vosk] Quest用日本語モデルの展開が完了しました: " +
            $"新規{copiedCount} / 再利用{reusedCount}ファイル / {destinationRoot}");
        onReady?.Invoke(destinationRoot);
    }

    private static bool IsInstalledModelValid(string modelRoot, string markerPath)
    {
        if (!File.Exists(markerPath) || File.ReadAllText(markerPath).Trim() != InstallVersion)
        {
            return false;
        }

        return IsNonEmptyFile(Path.Combine(modelRoot, "am", "final.mdl")) &&
               IsNonEmptyFile(Path.Combine(modelRoot, "conf", "model.conf")) &&
               IsNonEmptyFile(Path.Combine(modelRoot, "graph", "HCLr.fst")) &&
               IsNonEmptyFile(Path.Combine(modelRoot, "graph", "words.txt")) &&
               File.Exists(Path.Combine(modelRoot, "ivector", "online_cmvn.conf")) &&
               IsNonEmptyFile(Path.Combine(modelRoot, "ivector", "splice.conf"));
    }

    private static bool IsNonEmptyFile(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static string BuildStreamingAssetUrl(string modelFolderName)
    {
        return Application.streamingAssetsPath.TrimEnd('/') + "/" +
               Uri.EscapeDataString(modelFolderName);
    }

    private static string EscapeRelativeUrl(string relativePath)
    {
        string[] segments = relativePath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        return string.Join("/", segments);
    }
#endif
}
