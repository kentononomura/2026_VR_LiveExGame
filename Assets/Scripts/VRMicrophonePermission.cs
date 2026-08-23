using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Android/Questの録音権限を、実際にMicrophoneを開始する前に確認する。
/// </summary>
public static class VRMicrophonePermission
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool requestAttempted;
    private static PermissionCallbacks permissionCallbacks;
#endif

    public static bool EnsureGranted()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            return true;
        }

        if (!requestAttempted)
        {
            requestAttempted = true;
            permissionCallbacks = new PermissionCallbacks();
            permissionCallbacks.PermissionGranted += OnPermissionGranted;
            permissionCallbacks.PermissionDenied += OnPermissionDenied;
            permissionCallbacks.PermissionRequestDismissed += OnPermissionRequestDismissed;

            Debug.Log("[Microphone] Questのマイク使用許可を要求します。");
            Permission.RequestUserPermission(Permission.Microphone, permissionCallbacks);
        }

        return false;
#else
        return true;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void OnPermissionGranted(string permissionName)
    {
        Debug.Log("[Microphone] マイク使用が許可されました。音声入力を開始します。");
        permissionCallbacks = null;
    }

    private static void OnPermissionDenied(string permissionName)
    {
        bool canExplainAndRequestAgain =
            Permission.ShouldShowRequestPermissionRationale(permissionName);
        if (canExplainAndRequestAgain)
        {
            Debug.LogError(
                "[Microphone] マイク使用が拒否されました。" +
                "音声入力を利用するには、次回の権限確認でマイクを許可してください。");
        }
        else
        {
            Debug.LogError(
                "[Microphone] マイク使用を再要求できません。" +
                "Questの設定 > アプリ > 権限からマイクを許可してください。");
        }

        permissionCallbacks = null;
    }

    private static void OnPermissionRequestDismissed(string permissionName)
    {
        Debug.LogError("[Microphone] マイク使用許可のダイアログが閉じられました。Questの設定からこのアプリのマイク権限を許可してください。");
        permissionCallbacks = null;
    }
#endif
}
