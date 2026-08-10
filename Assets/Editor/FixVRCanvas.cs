using UnityEngine;
using UnityEditor;

public class FixVRCanvas
{
    [MenuItem("Tools/Fix VR Canvas")]
    public static void FixCanvas()
    {
        // シーン内のすべてのCanvasを取得
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        
        foreach (Canvas canvas in canvases)
        {
            // ScreenSpaceOverlay（PC画面へのベタ貼り）になっているものを探す
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // VR空間に配置できるようにWorldSpaceに変更
                canvas.renderMode = RenderMode.WorldSpace;
                
                RectTransform rt = canvas.GetComponent<RectTransform>();
                
                // サイズをVR向けの現実サイズに縮小
                rt.localScale = new Vector3(0.003f, 0.003f, 0.003f);
                
                // プレイヤーの少し上、少し前方に浮かせる
                rt.position = new Vector3(0f, 2.5f, 4f);
                
                EditorUtility.SetDirty(canvas);
            }
        }
        
        Debug.Log("<color=green>CanvasをVR向け(World Space)に変更し、空中に配置しました！</color>");
    }
}
