using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Edit-time visual references for objects spawned by StageDirector.</summary>
[InitializeOnLoad]
public sealed class TestSceneLayoutWindow : EditorWindow
{
    private const string PreviewName = "TestScene Layout Preview (not saved)";
    private const string AudienceName = "Audience Layout";
    private const string AudiencePrefabPath = "Assets/ShirayuriMeshibe/Scenes/ParticleAudience/URP/Models/AudienceB/Prefabs/ParticleSystem(AudienceB(LOD0)) use MeshEmitter.prefab";
    private static GameObject previewRoot;
    [SerializeField] private Vector3 circleCenter = new Vector3(0f, 0f, 2f);
    [SerializeField] private float circleRadius = 6f;
    [SerializeField] private int circleRows = 3;
    [SerializeField] private float rowSpacing = 0.8f;
    [SerializeField] private int peoplePerRow = 32;
    [SerializeField] private float audienceArcDegrees = 135f;
    [SerializeField] private float positionRandomness = 0.6f;
    [SerializeField] private int layoutSeed = 12345;
    private Vector2 scrollPosition;

    static TestSceneLayoutWindow()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreview;
        EditorApplication.quitting += ClearPreview;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) ClearPreview();
        };
        EditorSceneManager.sceneClosing += (scene, removing) => ClearPreview();
    }

    [MenuItem("Tools/TestScene/観客配置・ステージプレビュー")]
    public static void Open()
    {
        GetWindow<TestSceneLayoutWindow>("TestScene 配置");
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.HelpBox(
            "TestSceneを開き、ステージプレビューを表示してください。再生時に生成されるステージとUnityちゃんの初期位置を表示します。", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("1. ステージプレビューを表示・更新")) ShowPreview();
            if (GUILayout.Button("2. 観客を追加／選択")) AddAudience();
            if (GUILayout.Button("プレビューを消す")) ClearPreview();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ステージ正面の扇形配置", EditorStyles.boldLabel);
            circleCenter = EditorGUILayout.Vector3Field("円の中心（ワールド座標）", circleCenter);
            if (GUILayout.Button("中心をUnityちゃんの初期位置に合わせる"))
            {
                StageDirector director = FindDirector();
                if (director != null && director.prefabsOnTimeline != null)
                    foreach (GameObject prefab in director.prefabsOnTimeline)
                        if (prefab != null && prefab.GetComponentInChildren<UnityChan.FaceUpdate>(true) != null)
                        {
                            circleCenter = prefab.transform.position;
                            break;
                        }
            }
            circleRadius = Mathf.Max(0.5f, EditorGUILayout.FloatField("最前列の半径 (m)", circleRadius));
            audienceArcDegrees = EditorGUILayout.Slider("配置範囲（度）", audienceArcDegrees, 1f, 180f);
            EditorGUILayout.LabelField($"正面（+Z）から左右 {audienceArcDegrees * 0.5f:0.#} 度");
            circleRows = Mathf.Clamp(EditorGUILayout.IntField("列数", circleRows), 1, 20);
            rowSpacing = Mathf.Max(0.1f, EditorGUILayout.FloatField("列の間隔 (m)", rowSpacing));
            peoplePerRow = Mathf.Clamp(EditorGUILayout.IntField("1列の人数", peoplePerRow), 3, 500);
            positionRandomness = EditorGUILayout.Slider("位置のランダム性", positionRandomness, 0f, 1f);
            layoutSeed = EditorGUILayout.IntField("配置パターン（シード）", layoutSeed);
            EditorGUILayout.HelpBox("ランダム性が0なら整列します。シードを変えると別の配置になり、同じ設定では同じ配置を再現します。", MessageType.None);
            EditorGUILayout.LabelField($"合計 {circleRows * peoplePerRow} 人／全員が円の中心を向きます");
            if (GUILayout.Button("扇形配置を適用／更新"))
                ApplyCircle(circleCenter, circleRadius, circleRows, rowSpacing, peoplePerRow, audienceArcDegrees, positionRandomness, layoutSeed);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("配置の調整", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Audience Layoutを選択し、SceneビューでW（移動）・E（回転）を使って調整し、Ctrl+Sで保存します。観客はシーンに保存され、再生時もその位置を使用します。\n\n" +
            "位置を変えた後はParticle SystemのプレビューをRestartしてください。人数・配置メッシュ・マテリアルは子のParticle Systemで調整します。\n\n" +
            "ステージプレビューは表示専用です。保存・ビルドには含まれず、Play開始時に自動で消えます。ダンス移動後の位置を表すものではありません。", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    private static StageDirector FindDirector()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/TestScene.unity")
        {
            Debug.LogWarning("TestSceneを開いてから配置ツールを使用してください。");
            return null;
        }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            StageDirector director = root.GetComponentInChildren<StageDirector>(true);
            if (director != null) return director;
        }
        Debug.LogWarning("TestScene内にStageDirectorが見つかりません。");
        return null;
    }

    public static void ShowPreview()
    {
        StageDirector director = FindDirector();
        if (director == null) return;
        ClearPreview();
        previewRoot = new GameObject(PreviewName);
        previewRoot.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        previewRoot.tag = "EditorOnly";
        SceneManager.MoveGameObjectToScene(previewRoot, director.gameObject.scene);
        AddVisuals(director.prefabsNeedsActivation, 1f);
        AddVisuals(director.miscPrefabs, 1f);
        if (director.prefabsOnTimeline != null)
        {
            foreach (GameObject prefab in director.prefabsOnTimeline)
            {
                if (prefab == null) continue;
                float scale = prefab.GetComponentInChildren<UnityChan.FaceUpdate>(true) != null
                    ? director.unityChanScaleMultiplier : 1f;
                AddVisuals(new[] { prefab }, scale);
            }
        }
        SceneView.RepaintAll();
    }

    private static void AddVisuals(GameObject[] prefabs, float scale)
    {
        if (prefabs == null) return;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null) continue;
            var map = new Dictionary<Transform, Transform>();
            Transform root = CopyHierarchy(prefab.transform, previewRoot.transform, map);
            root.localScale *= scale;
            // Copy only rendering data. No scripts, audio, colliders, cameras,
            // animators or XR components are instantiated, even temporarily.
            foreach (var pair in map)
            {
                MeshFilter sourceFilter = pair.Key.GetComponent<MeshFilter>();
                MeshRenderer sourceRenderer = pair.Key.GetComponent<MeshRenderer>();
                if (sourceFilter != null && sourceRenderer != null)
                {
                    pair.Value.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                    var renderer = pair.Value.gameObject.AddComponent<MeshRenderer>();
                    EditorUtility.CopySerialized(sourceRenderer, renderer);
                }
                SkinnedMeshRenderer sourceSkin = pair.Key.GetComponent<SkinnedMeshRenderer>();
                if (sourceSkin != null)
                {
                    var skin = pair.Value.gameObject.AddComponent<SkinnedMeshRenderer>();
                    EditorUtility.CopySerialized(sourceSkin, skin);
                    Transform[] bones = sourceSkin.bones;
                    for (int i = 0; i < bones.Length; i++)
                        bones[i] = bones[i] != null && map.TryGetValue(bones[i], out Transform bone) ? bone : null;
                    skin.bones = bones;
                    skin.rootBone = sourceSkin.rootBone != null && map.TryGetValue(sourceSkin.rootBone, out Transform rootBone) ? rootBone : null;
                    skin.updateWhenOffscreen = true;
                }
                foreach (Component component in pair.Value.GetComponents<Component>())
                    component.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;
            }
        }
    }

    private static Transform CopyHierarchy(Transform source, Transform parent, Dictionary<Transform, Transform> map)
    {
        var obj = new GameObject(source.name);
        obj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;
        obj.layer = source.gameObject.layer;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = source.localPosition;
        obj.transform.localRotation = source.localRotation;
        obj.transform.localScale = source.localScale;
        map.Add(source, obj.transform);
        for (int i = 0; i < source.childCount; i++) CopyHierarchy(source.GetChild(i), obj.transform, map);
        obj.SetActive(source.gameObject.activeSelf);
        return obj.transform;
    }

    public static void ClearPreview()
    {
        if (previewRoot != null) DestroyImmediate(previewRoot);
        previewRoot = null;
        SceneView.RepaintAll();
    }

    public static void AddAudience()
    {
        StageDirector director = FindDirector();
        if (director == null) return;
        Scene scene = director.gameObject.scene;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != AudienceName) continue;
            Selection.activeGameObject = root;
            return;
        }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AudiencePrefabPath);
        if (prefab == null)
        {
            Debug.LogError("観客のMeshEmitter Prefabが見つかりません: " + AudiencePrefabPath);
            return;
        }
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add TestScene audience layout");
        var layout = new GameObject(AudienceName);
        SceneManager.MoveGameObjectToScene(layout, scene);
        // Starting point only; use the Scene handles to choose the seating area.
        layout.transform.position = new Vector3(0f, 0f, 6f);
        layout.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Undo.RegisterCreatedObjectUndo(layout, "Add audience layout");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.transform.SetParent(layout.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        ParticleSystem particles = instance.GetComponent<ParticleSystem>();
        if (particles != null)
        {
            var shape = particles.shape;
            int count = shape.mesh != null ? Mathf.Min(100, shape.mesh.vertexCount) : 100;
            var main = particles.main;
            main.maxParticles = count;
            var emission = particles.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            PrefabUtility.RecordPrefabInstancePropertyModifications(particles);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        Undo.RegisterCreatedObjectUndo(instance, "Add audience particles");
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = layout;
        SceneView.RepaintAll();
    }

    public static void ApplyCircle(Vector3 center, float radius, int rows, float spacing, int perRow, float arcDegrees = 135f, float randomness = 0.6f, int seed = 12345)
    {
        StageDirector director = FindDirector();
        if (director == null) return;
        if (rows < 1 || rows > 20 || perRow < 3 || perRow > 500 ||
            !Finite(arcDegrees) || arcDegrees < 1f || arcDegrees > 180f ||
            !Finite(randomness) || randomness < 0f || randomness > 1f ||
            !Finite(radius) || radius < 0.5f || !Finite(spacing) || spacing < 0.1f ||
            !Finite(center.x) || !Finite(center.y) || !Finite(center.z))
        {
            Debug.LogError("扇形配置の中心・半径・角度（1～180度）・ランダム性（0～1）・列数・間隔・人数を確認してください。");
            return;
        }
        AddAudience();
        GameObject layout = null;
        foreach (GameObject root in director.gameObject.scene.GetRootGameObjects())
            if (root.name == AudienceName) { layout = root; break; }
        if (layout == null) return;
        var particles = layout.GetComponentInChildren<ParticleSystem>(true);
        if (particles == null)
        {
            Debug.LogError("Audience Layout内にParticle Systemがありません。");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply circular audience layout");
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        Undo.RecordObjects(new Object[] { layout.transform, particles.transform, particles, renderer }, "Apply circular audience layout");
        var shape = particles.shape;
        Mesh mesh = shape.mesh;
        const string folder = "Assets/TestSceneAudienceLayouts";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "TestSceneAudienceLayouts");
        string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : "";
        // Never modify a vendor emitter mesh. Reuse only this tool's generated asset.
        if (!path.StartsWith(folder + "/", System.StringComparison.Ordinal))
        {
            mesh = new Mesh { name = "TestScene Audience Circle" };
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(folder + "/AudienceCircle.asset"));
        }
        else Undo.RegisterCompleteObjectUndo(mesh, "Update circular audience mesh");
        int count = rows * perRow;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var indices = new int[(count - 2) * 3];
        var random = new System.Random(seed);
        float angleStep = arcDegrees / (perRow - 1);
        for (int row = 0; row < rows; row++)
        for (int seat = 0; seat < perRow; seat++)
        {
            int index = row * perRow + seat;
            // Limit jitter to less than half a seat/row interval so neighboring
            // seats cannot exchange places. Keep every seat inside the front arc.
            float angleOffset = ((float)random.NextDouble() * 2f - 1f) * angleStep * 0.4f * randomness;
            float radiusOffset = ((float)random.NextDouble() * 2f - 1f) * spacing * 0.4f * randomness;
            float angle = Mathf.Clamp(-arcDegrees * 0.5f + seat * angleStep + angleOffset,
                -arcDegrees * 0.5f, arcDegrees * 0.5f) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            vertices[index] = radial * Mathf.Max(radius, radius + row * spacing + radiusOffset);
            normals[index] = -radial;
        }
        // Faces only make this a valid emitter mesh; spawning uses vertices.
        for (int i = 0; i < count - 2; i++)
        {
            indices[3 * i] = 0;
            indices[3 * i + 1] = i + 1;
            indices[3 * i + 2] = i + 2;
        }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        layout.transform.SetPositionAndRotation(center, Quaternion.identity);
        layout.transform.localScale = Vector3.one;
        particles.transform.localPosition = Vector3.zero;
        particles.transform.localRotation = Quaternion.identity;
        particles.transform.localScale = Vector3.one;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.mesh = mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
        shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
        shape.meshSpawnSpeed = 1f;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;
        shape.scale = Vector3.one;
        shape.normalOffset = 0f;
        shape.alignToDirection = false;
        shape.randomDirectionAmount = 0f;
        shape.sphericalDirectionAmount = 0f;
        shape.randomPositionAmount = 0f;
        shape.useMeshColors = false;
        var main = particles.main;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSpeed = 0f;
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = 0f;
        main.startRotationZ = 0f;
        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        renderer.alignment = ParticleSystemRenderSpace.World;
        var facing = particles.GetComponent<AudienceCircleFacing>();
        if (facing == null) facing = Undo.AddComponent<AudienceCircleFacing>(particles.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(particles.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(particles);
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        particles.Simulate(0.01f, true, true, false);
        facing.RefreshFacing();
        Selection.activeGameObject = layout;
        SceneView.RepaintAll();
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
