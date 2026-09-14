using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns the shared bevel mesh for one monitor, including edit-time previews.</summary>
[DisallowMultipleComponent]
public sealed class VoiceBillboardGeometry : MonoBehaviour
{
    private Mesh beveledBox;

    public Mesh GetBeveledBox()
    {
        if (beveledBox != null) return beveledBox;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        const float outer = 0.5f;
        const float inner = 0.46f;
        // Six flat faces, twelve chamfers and eight corner triangles.
        for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var face = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    face[i][axis] = sign * outer;
                    face[i][(axis + 1) % 3] = (i < 2 ? -1 : 1) * inner;
                    face[i][(axis + 2) % 3] = (i == 0 || i == 3 ? -1 : 1) * inner;
                }
                AddFace(face, vertices, normals, triangles);
            }
        for (int axis = 0; axis < 3; axis++)
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    var face = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        face[i][axis] = (i < 2 ? -1 : 1) * inner;
                        face[i][(axis + 1) % 3] = a * (i == 0 || i == 3 ? outer : inner);
                        face[i][(axis + 2) % 3] = b * (i == 0 || i == 3 ? inner : outer);
                    }
                    AddFace(face, vertices, normals, triangles);
                }
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    AddFace(new[] { new Vector3(x * outer, y * inner, z * inner),
                        new Vector3(x * inner, y * outer, z * inner),
                        new Vector3(x * inner, y * inner, z * outer) }, vertices, normals, triangles);
        beveledBox = new Mesh { name = "Monitor Beveled Box", hideFlags = HideFlags.HideAndDontSave };
        beveledBox.SetVertices(vertices);
        beveledBox.SetNormals(normals);
        beveledBox.SetTriangles(triangles, 0);
        beveledBox.RecalculateBounds();
        return beveledBox;
    }

    private static void AddFace(Vector3[] face, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
    {
        Vector3 center = Vector3.zero;
        foreach (Vector3 vertex in face) center += vertex;
        Vector3 normal = Vector3.Cross(face[1] - face[0], face[2] - face[0]).normalized;
        if (Vector3.Dot(normal, center) < 0f) { System.Array.Reverse(face); normal = -normal; }
        int start = vertices.Count;
        foreach (Vector3 vertex in face) { vertices.Add(vertex); normals.Add(normal); }
        for (int i = 1; i < face.Length - 1; i++)
        { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
    }

    private void OnDestroy()
    {
        if (beveledBox == null) return;
        if (Application.isPlaying) Destroy(beveledBox);
        else DestroyImmediate(beveledBox);
    }
}
