using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

using UnityRandom = UnityEngine.Random;

namespace ShirayuriMeshibe.ParticleEmitterMeshGenerator
{
    public static class ParticleEmitterMeshGenerator
    {
        static public async Task<Mesh[]> Generate(Action<string, float> progress,
                                                  string outputPath,
                                                  MeshBuildDirectionType meshBuildDirectionType,
                                                  SortType sortType,
                                                  float audienceDistanceX,
                                                  float audienceDistanceZ,
                                                  float audienceRandomDistance,
                                                  int audienceCountX,
                                                  int audienceCountZ,
                                                  AreaSettings[] areaSettings,
                                                  int randomSeed,
                                                  CancellationToken cancellationToken)

        {
            var vertexCount = audienceCountX * audienceCountZ;
            var directionX = meshBuildDirectionType switch
            {
                MeshBuildDirectionType.RightForward => 1f,
                MeshBuildDirectionType.RightBackward => 1f,
                _ => -1f,
            };

            var directionZ = meshBuildDirectionType switch
            {
                MeshBuildDirectionType.RightForward => 1f,
                MeshBuildDirectionType.LeftForward => 1f,
                _ => -1f,
            };

            double time = 0.0;
            double preTime = EditorApplication.timeSinceStartup;
            float percentage = 0f;

            //---------------------------------
            // 1. Calcurate random area index
            //---------------------------------

            UnityRandom.InitState(randomSeed);

            // 頂点がどのエリアに所属しているかを事前計算する
            var indexies = Array.Empty<int>();
            {
                var indexList = new List<int>(vertexCount);
                for (int i = 0; i < areaSettings.Length; ++i)
                {
                    Debug.Log($"areaSettings[{i}]:{areaSettings[i].Headcount}");
                    indexList.AddRange(Enumerable.Repeat(i, areaSettings[i].Headcount));
                }
                indexies = indexList.ToArray();

                // シャッフルする
                var n = indexies.Length;
                while (1 < n)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning("Canceled...");
                        return null;
                    }

                    n--;
                    var k = UnityRandom.Range(0, n);
                    var temp = indexies[k];
                    indexies[k] = indexies[n];
                    indexies[n] = temp;

                    time += EditorApplication.timeSinceStartup - preTime;
                    preTime = EditorApplication.timeSinceStartup;
                    if (0.16 < time)
                    {
                        time -= 0.16;
                        await Task.Yield();
                    }
                }
            }
            if(indexies.Length==0)
            {
                Debug.LogError("Failed calculate index list.");
                return null;
            }

            //-----------------------
            // 2. Generate Positions
            //-----------------------

            var vertexListArray = new List<Vector3>[areaSettings.Length];
            for (var i = 0; i < areaSettings.Length; ++i)
                vertexListArray[i] = new(vertexCount);

            for (int z = 0; z < audienceCountZ; ++z)
            {
                var positionZ = directionZ * z * audienceDistanceZ;

                for (int x = 0; x < audienceCountX; ++x)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning("Canceled...");
                        return null;
                    }

                    var positionX = directionX * x * audienceDistanceX;
                    var i = audienceCountX * z + x;
                    var randomPosition = UnityRandom.insideUnitCircle;
                    var position = new Vector3(positionX + randomPosition.x * audienceRandomDistance, 0f, positionZ + randomPosition.y * audienceRandomDistance);

                    var currentAreaIndex = indexies[i];
                    var vertexList = vertexListArray[currentAreaIndex];
                    vertexList.Add(position);

                    percentage = (float)i / vertexCount;
                    progress("Generate positions", percentage);

                    time += EditorApplication.timeSinceStartup - preTime;
                    preTime = EditorApplication.timeSinceStartup;
                    if (0.16 < time)
                    {
                        time -= 0.16;
                        await Task.Yield();
                    }
                }
            }

            //--------------------
            // 3. Apply SortType
            //--------------------

            if(sortType==SortType.Random)
            {
                for(int i=0; i<vertexListArray.Length; ++i)
                {
                    var vertices = vertexListArray[i].ToArray();
                    var n = vertices.Length;
                    while (1 < n)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Debug.LogWarning("Canceled...");
                            return null;
                        }

                        n--;
                        var k = UnityRandom.Range(0, n);
                        var temp = vertices[k];
                        vertices[k] = vertices[n];
                        vertices[n] = temp;

                        time += EditorApplication.timeSinceStartup - preTime;
                        preTime = EditorApplication.timeSinceStartup;
                        if (0.16 < time)
                        {
                            time -= 0.16;
                            await Task.Yield();
                        }
                    }

                    percentage = (float)i / vertexListArray.Length;
                    progress("Randomize...", percentage);

                    vertexListArray[i] = new (vertices);
                }
            }

            //---------------------
            // 4. Generate Meshes
            //---------------------

            var meshes = new Mesh[vertexListArray.Length];

            for (int i = 0; i < meshes.Length; ++i)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.LogWarning("Canceled...");
                    return null;
                }

                var vertexList = vertexListArray[i];
                if (vertexList.Count == 0)
                    continue;

                var mesh = new Mesh();
                mesh.name = areaSettings[i].Name;
                mesh.vertices = vertexList.ToArray();

                var normals = new Vector3[vertexList.Count];
                for(int j = 0; j < normals.Length; ++j)
                    normals[j] = Vector3.up;
                mesh.normals = normals;
                mesh.triangles = await CalculateTriangls(progress, mesh.vertices, cancellationToken);
                mesh.RecalculateBounds();

                var bounds = mesh.bounds;
                var boundsMax = bounds.max;
                boundsMax.y = 2f;
                bounds.max = boundsMax;
                mesh.bounds = bounds;

                meshes[i] = mesh;

                percentage = (float)i / meshes.Length;
                progress("Generate mesh", percentage);
            }

            return meshes;
        }

        /// <summary>
        /// 最近傍の2頂点から三角形を作る。
        /// </summary>
        /// <param name="vertices"></param>
        /// <returns></returns>
        static async Task<int[]> CalculateTriangls(Action<string, float> progress, Vector3[] vertices, CancellationToken cancellationToken)
        {
            if (vertices.Length < 3)
            {
                Debug.LogError($"CalculateTriangls() vertices:{vertices.Length}");
                return Array.Empty<int>();
            }

            var triangles = new List<int>();
            var triangleSet = new HashSet<Vector3Int>();

            double time = 0.0;
            double preTime = EditorApplication.timeSinceStartup;

            for (int i = 0; i < vertices.Length; i++)
            {
                List<(int index, float distance)> distances = new ();
                for (int j = 0; j < vertices.Length; j++)
                {
                    if(cancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning("Canceled...");
                        return Array.Empty<int>();
                    }

                    if (i != j)
                    {
                        distances.Add((j, Vector3.Distance(vertices[i], vertices[j])));
                    }

                    time += EditorApplication.timeSinceStartup - preTime;
                    preTime = EditorApplication.timeSinceStartup;
                    if (0.16 < time)
                    {
                        time -= 0.16;
                        await Task.Yield();
                    }
                }

                // 距離をソートして最も近いものを2点取得する
                distances.Sort((a, b) => a.distance.CompareTo(b.distance));
                var nearest1 = distances[0].index;
                var nearest2 = distances[1].index;

                // 三角形の頂点インデックスを昇順に並べて文字列化 (重複チェック用)
                int[] indices = { i, nearest1, nearest2 };
                Array.Sort(indices);
                var triangleKey = new Vector3Int() { x=indices[0], y=indices[1], z=indices[2] };

                // 重複していない場合のみ triangles リストに追加
                if (!triangleSet.Contains(triangleKey))
                {
                    triangleSet.Add(triangleKey);

                    // winding orderをClockwise orderにする
                    var p0 = vertices[i];
                    var p1 = vertices[nearest1];
                    var p2 = vertices[nearest2];
                    var v01 = p1 - p0;
                    var v02 = p2 - p0;

                    var normal = Vector3.Cross(v01, v02);
                    if(0 <= Vector3.Dot(normal, Vector3.up))
                    {
                        triangles.Add(i);
                        triangles.Add(nearest1);
                        triangles.Add(nearest2);
                    }
                    else
                    {
                        triangles.Add(i);
                        triangles.Add(nearest2);
                        triangles.Add(nearest1);
                    }
                }

                var percentage = (float)i / vertices.Length;
                progress("Calcurate triangles...", percentage);
            }

            return triangles.ToArray();
        }
    }
}
