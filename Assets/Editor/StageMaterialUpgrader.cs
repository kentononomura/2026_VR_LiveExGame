using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections;

public class StageMaterialUpgrader : EditorWindow
{
    [MenuItem("Tools/Upgrade Stage Materials to URP")]
    public static void UpgradeStageMaterials()
    {
        string[] searchPaths = new string[] {
            "Assets/UnityChan/unitychan_hw/UnityChanShader/Textures_hw/UnityChanStage",
            "Assets/UnityChan/Stage",
            "Assets/UnityChan/unitychan_hw/UnityChanShader/Materials_hw",
            "Assets/UnityChan/unitychan_hw/Materials"
        };

        foreach (string path in searchPaths)
        {
            string fullPath = Path.Combine(Application.dataPath, path.Substring("Assets".Length + 1));
            fullPath = Path.GetFullPath(fullPath); // Normalize slashes for Windows
            if (!Directory.Exists(fullPath))
            {
                Debug.LogWarning($"Search path not found: {fullPath}");
                continue;
            }

            Debug.Log($"Processing Path: {fullPath}");

            // 1. Fix FBX Import Settings (Obsolete Material Location Warnings)
            FixFBXImportSettings(fullPath);

            // 2. Upgrade all Materials inside the stage directory to URP shaders
            UpgradeMaterials(fullPath);

            // 3. Clean missing script components in prefabs (removes obsolete legacy image effect scripts)
            CleanMissingScriptsInPrefabs(fullPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Stage materials upgrade and FBX settings fix completed successfully!");
    }

    private static void FixFBXImportSettings(string rootPath)
    {
        string[] fbxFiles = Directory.GetFiles(rootPath, "*.fbx", SearchOption.AllDirectories);
        Debug.Log($"Found {fbxFiles.Length} FBX files in {rootPath}");
        int count = 0;

        foreach (string file in fbxFiles)
        {
            string fbxPath = file.Replace('\\', '/');
            int assetsIndex = fbxPath.IndexOf("Assets/");
            if (assetsIndex >= 0) fbxPath = fbxPath.Substring(assetsIndex);

            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer != null)
            {
                // In modern Unity, change the material import mode to standard description-based
                // which resolves the "MaterialLocation.External is obsolete" warning.
                if (importer.materialImportMode == ModelImporterMaterialImportMode.None || 
                    importer.materialLocation == ModelImporterMaterialLocation.External)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    importer.SaveAndReimport();
                    count++;
                }
            }
        }
        Debug.Log($"Fixed FBX import settings for {count} models.");
    }

    private static void UpgradeMaterials(string rootPath)
    {
        string[] matFiles = Directory.GetFiles(rootPath, "*.mat", SearchOption.AllDirectories);
        Debug.Log($"Found {matFiles.Length} Material files in {rootPath}");
        int count = 0;

        foreach (string file in matFiles)
        {
            string matPath = file.Replace('\\', '/');
            int assetsIndex = matPath.IndexOf("Assets/");
            if (assetsIndex >= 0) matPath = matPath.Substring(assetsIndex);

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            Shader currentShader = mat.shader;
            string shaderName = currentShader != null ? currentShader.name : "NULL";
            Debug.Log($"Checking Material: {mat.name} | Shader: {shaderName}");

            if (currentShader == null) continue;

            string lowerShaderName = currentShader.name.ToLower();
            string lowerMatName = mat.name.ToLower();

            // Skip shaders that are already URP or URP-compatible custom shaders
            if (currentShader.name == "Custom/Visualizer" || currentShader.name.Contains("Universal Render Pipeline"))
            {
                // Ensure Base Map has the texture if the legacy Main Tex is set
                if (mat.HasProperty("_MainTex") && mat.HasProperty("_BaseMap"))
                {
                    Texture existingMainTex = mat.GetTexture("_MainTex");
                    Texture baseMap = mat.GetTexture("_BaseMap");
                    if (existingMainTex != null && baseMap == null)
                    {
                        mat.SetTexture("_BaseMap", existingMainTex);
                        EditorUtility.SetDirty(mat);
                        count++;
                    }
                }

                // If it is a cheek material that was already converted to URP but is not transparent, fix it here!
                if (lowerMatName.Contains("cheek"))
                {
                    mat.SetFloat("_Surface", 1); // 1 = Transparent
                    mat.SetFloat("_Blend", 0); // 0 = Alpha
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                    EditorUtility.SetDirty(mat);
                    count++;
                }
                continue;
            }

            Debug.Log($"Converting Material to URP: {mat.name} (Original Shader: {currentShader.name})");

            // Extract textures and color properties from legacy slots
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            // Choose target URP Shader
            string targetShaderName = "Universal Render Pipeline/Simple Lit";

            // Self-illuminated, glow, emission, or light materials should use URP Unlit to look glowing/bright
            if (lowerShaderName.Contains("self-illumin") || 
                lowerShaderName.Contains("illum") || 
                lowerShaderName.Contains("glow") || 
                lowerShaderName.Contains("light") || 
                lowerMatName.Contains("light") || 
                lowerMatName.Contains("glow") || 
                lowerMatName.Contains("eq") || 
                lowerMatName.Contains("sign") ||
                lowerMatName.Contains("screen") ||
                lowerMatName.Contains("cheek"))
            {
                targetShaderName = "Universal Render Pipeline/Unlit";
            }

            bool isCheek = lowerMatName.Contains("cheek");

            Shader targetShader = Shader.Find(targetShaderName);
            if (targetShader != null)
            {
                mat.shader = targetShader;

                // Map legacy properties to new URP properties (resolves "white material" issues)
                if (mat.HasProperty("_BaseMap") && mainTex != null)
                {
                    mat.SetTexture("_BaseMap", mainTex);
                }
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", color);
                }

                // If it is a cheek material, set it to URP Transparent
                if (isCheek)
                {
                    mat.SetFloat("_Surface", 1); // 1 = Transparent
                    mat.SetFloat("_Blend", 0); // 0 = Alpha
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                }

                EditorUtility.SetDirty(mat);
                count++;
            }
            else
            {
                Debug.LogWarning($"Could not find target shader: {targetShaderName} for material {mat.name}");
            }
        }
        Debug.Log($"Upgraded {count} materials to URP-compatible shaders.");
    }

    private static void CleanMissingScriptsInPrefabs(string rootPath)
    {
        string[] prefabFiles = Directory.GetFiles(rootPath, "*.prefab", SearchOption.AllDirectories);
        int totalRemoved = 0;

        foreach (string file in prefabFiles)
        {
            string prefabPath = file.Replace('\\', '/');
            int assetsIndex = prefabPath.IndexOf("Assets/");
            if (assetsIndex >= 0) prefabPath = prefabPath.Substring(assetsIndex);

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) continue;

            int removed = CleanMissingScriptsRecursively(root);
            if (removed > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                totalRemoved += removed;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (totalRemoved > 0)
        {
            Debug.Log($"Removed {totalRemoved} missing script components from prefabs under {rootPath}.");
        }
    }

    private static int CleanMissingScriptsRecursively(GameObject go)
    {
        int count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        
        foreach (Transform child in go.transform)
        {
            count += CleanMissingScriptsRecursively(child.gameObject);
        }
        
        return count;
    }
}
