using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VRPhoneCamera))]
public sealed class VRPhoneCameraInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8f);

        if (GUILayout.Button("縦・横UIをビジュアル調整", GUILayout.Height(32f)))
        {
            VRPhoneCameraLayoutWindow.Open((VRPhoneCamera)target);
        }
    }
}

public sealed class VRPhoneCameraLayoutWindow : EditorWindow
{
    private const float CanvasSize = 1000f;
    private const float SliderBottom = -300f;
    private const float SliderTop = 300f;
    private const float PreviewPadding = 18f;

    private VRPhoneCamera phoneCamera;
    private SerializedObject serializedCamera;
    private SerializedProperty portraitPosition;
    private SerializedProperty landscapePosition;
    private SerializedProperty sliderWidth;
    private SerializedProperty sliderXOffset;
    private SerializedProperty zoomTextFontSize;

    [MenuItem("Tools/VR Camera/Phone UI Layout Editor")]
    public static void OpenFromMenu()
    {
        VRPhoneCamera selected = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<VRPhoneCamera>()
            : null;
        Open(selected);
    }

    public static void Open(VRPhoneCamera targetCamera)
    {
        VRPhoneCameraLayoutWindow window = GetWindow<VRPhoneCameraLayoutWindow>();
        window.titleContent = new GUIContent("Phone UI Layout");
        window.minSize = new Vector2(760f, 560f);
        window.AssignTarget(targetCamera);
        window.Show();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
    }

    private void HandleUndoRedo()
    {
        if (serializedCamera != null) serializedCamera.Update();
        Repaint();
    }

    private void AssignTarget(VRPhoneCamera targetCamera)
    {
        phoneCamera = targetCamera;
        if (phoneCamera == null)
        {
            serializedCamera = null;
            return;
        }

        serializedCamera = new SerializedObject(phoneCamera);
        portraitPosition = serializedCamera.FindProperty("portraitZoomTextPosition");
        landscapePosition = serializedCamera.FindProperty("landscapeZoomTextPosition");
        sliderWidth = serializedCamera.FindProperty("sliderWidth");
        sliderXOffset = serializedCamera.FindProperty("sliderXOffset");
        zoomTextFontSize = serializedCamera.FindProperty("zoomTextFontSize");
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        VRPhoneCamera selected = (VRPhoneCamera)EditorGUILayout.ObjectField(
            "VR Phone Camera", phoneCamera, typeof(VRPhoneCamera), true);
        if (EditorGUI.EndChangeCheck())
        {
            AssignTarget(selected);
        }

        if (phoneCamera == null || serializedCamera == null)
        {
            EditorGUILayout.HelpBox(
                "HierarchyまたはPrefab ModeでVRPhoneCameraを選択してから開いてください。",
                MessageType.Info);
            return;
        }

        serializedCamera.UpdateIfRequiredOrScript();

        EditorGUILayout.HelpBox(
            "各プレビュー内の『1.0x』をドラッグしてください。" +
            "縦向きと横向きの座標は個別に保存されます。横向きは右へ90度傾けた状態のプレビューです。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(portraitPosition, new GUIContent("縦向き座標"));
            if (GUILayout.Button("リセット", GUILayout.Width(70f)))
            {
                SetPosition(portraitPosition, new Vector2(0f, -20f), "縦向きUI位置をリセット");
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(landscapePosition, new GUIContent("横向き座標"));
            if (GUILayout.Button("リセット", GUILayout.Width(70f)))
            {
                SetPosition(landscapePosition, new Vector2(-100f, -20f), "横向きUI位置をリセット");
            }
        }

        serializedCamera.ApplyModifiedProperties();

        Rect available = GUILayoutUtility.GetRect(
            720f, 400f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        float halfWidth = (available.width - PreviewPadding) * 0.5f;
        Rect portraitArea = new Rect(available.x, available.y, halfWidth, available.height);
        Rect landscapeArea = new Rect(
            available.x + halfWidth + PreviewPadding, available.y, halfWidth, available.height);

        DrawPreview(portraitArea, false, portraitPosition, "縦向き");
        DrawPreview(landscapeArea, true, landscapePosition, "横向き（右回転）");
    }

    private void DrawPreview(
        Rect area,
        bool landscape,
        SerializedProperty positionProperty,
        string label)
    {
        GUI.Label(new Rect(area.x, area.y, area.width, 20f), label, EditorStyles.boldLabel);

        Rect usable = new Rect(area.x, area.y + 24f, area.width, area.height - 28f);
        float targetAspect = landscape ? 17f / 9f : 9f / 17f;
        Rect screen = FitAspect(usable, targetAspect);

        EditorGUI.DrawRect(Expand(screen, 6f), new Color(0.05f, 0.05f, 0.06f, 1f));
        EditorGUI.DrawRect(screen, new Color(0.12f, 0.17f, 0.22f, 1f));

        float width = sliderWidth != null ? sliderWidth.floatValue : 20f;
        float xOffset = sliderXOffset != null ? sliderXOffset.floatValue : -80f;
        float sliderCenterX = CanvasSize * 0.5f + xOffset - width * 0.5f;
        Vector2 sliderStart = LocalToPreview(
            new Vector2(sliderCenterX, SliderBottom), screen, landscape);
        Vector2 sliderEnd = LocalToPreview(
            new Vector2(sliderCenterX, SliderTop), screen, landscape);

        Handles.BeginGUI();
        Handles.color = new Color(0f, 0f, 0f, 0.7f);
        Handles.DrawAAPolyLine(8f, sliderStart, sliderEnd);
        Handles.color = Color.white;
        Handles.DrawAAPolyLine(3f, sliderStart, sliderEnd);
        Handles.EndGUI();

        Vector2 baseAnchor = new Vector2(sliderCenterX, SliderBottom);
        Vector2 anchor = LocalToPreview(
            baseAnchor + positionProperty.vector2Value, screen, landscape);
        float fontScale = zoomTextFontSize != null
            ? Mathf.Clamp(zoomTextFontSize.intValue / 60f, 0.6f, 1.8f)
            : 1f;
        Rect textRect = new Rect(
            anchor.x - 34f * fontScale,
            anchor.y - 14f * fontScale,
            68f * fontScale,
            28f * fontScale);

        EditorGUI.DrawRect(textRect, new Color(0f, 0f, 0f, 0.65f));
        GUI.Label(textRect, "1.0x", CenteredWhiteLabel());

        int controlId = GUIUtility.GetControlID(
            landscape ? "LandscapeZoomText".GetHashCode() : "PortraitZoomText".GetHashCode(),
            FocusType.Passive,
            textRect);
        Event current = Event.current;

        if (current.type == EventType.MouseDown && current.button == 0 && textRect.Contains(current.mousePosition))
        {
            GUIUtility.hotControl = controlId;
            current.Use();
        }
        else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
        {
            Vector2 localPoint = PreviewToLocal(current.mousePosition, screen, landscape);
            Vector2 newPosition = localPoint - baseAnchor;
            newPosition.x = Mathf.Clamp(newPosition.x, -1000f, 1000f);
            newPosition.y = Mathf.Clamp(newPosition.y, -1000f, 1000f);
            SetPosition(positionProperty, newPosition, landscape
                ? "横向きズーム表示を移動"
                : "縦向きズーム表示を移動");
            current.Use();
            Repaint();
        }
        else if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            current.Use();
        }

        EditorGUIUtility.AddCursorRect(textRect, MouseCursor.MoveArrow);
    }

    private void SetPosition(SerializedProperty property, Vector2 value, string undoName)
    {
        if (phoneCamera == null || property == null) return;

        Undo.RecordObject(phoneCamera, undoName);
        property.vector2Value = value;
        serializedCamera.ApplyModifiedProperties();
        EditorUtility.SetDirty(phoneCamera);
    }

    private static Vector2 LocalToPreview(Vector2 local, Rect screen, bool landscape)
    {
        Vector2 viewer = landscape
            ? new Vector2(-local.y, local.x)
            : local;
        return new Vector2(
            screen.center.x + viewer.x / CanvasSize * screen.width,
            screen.center.y - viewer.y / CanvasSize * screen.height);
    }

    private static Vector2 PreviewToLocal(Vector2 preview, Rect screen, bool landscape)
    {
        Vector2 viewer = new Vector2(
            (preview.x - screen.center.x) / screen.width * CanvasSize,
            -(preview.y - screen.center.y) / screen.height * CanvasSize);
        return landscape
            ? new Vector2(viewer.y, -viewer.x)
            : viewer;
    }

    private static Rect FitAspect(Rect area, float aspect)
    {
        float width = area.width;
        float height = width / aspect;
        if (height > area.height)
        {
            height = area.height;
            width = height * aspect;
        }
        return new Rect(
            area.center.x - width * 0.5f,
            area.center.y - height * 0.5f,
            width,
            height);
    }

    private static Rect Expand(Rect rect, float amount)
    {
        return new Rect(
            rect.x - amount,
            rect.y - amount,
            rect.width + amount * 2f,
            rect.height + amount * 2f);
    }

    private static GUIStyle CenteredWhiteLabel()
    {
        return new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }
}
