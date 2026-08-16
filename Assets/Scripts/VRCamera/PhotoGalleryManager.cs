using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PhotoData
{
    public Texture2D Texture;
    public int TotalScore;
    public int CenterBonus;
    public int GazeBonus;
    public int PoseBonus;
    public string Rank;
}

public static class PhotoGalleryManager
{
    private static List<PhotoData> capturedPhotos = new List<PhotoData>();

    public static List<PhotoData> GetPhotos()
    {
        // Sort photos so the highest score is always first
        if (capturedPhotos != null)
        {
            capturedPhotos.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));
        }
        return capturedPhotos;
    }

    public static void AddPhoto(PhotoData photoData)
    {
        capturedPhotos.Add(photoData);
    }

    public static void ClearPhotos()
    {
        foreach (var photoData in capturedPhotos)
        {
            if (photoData != null && photoData.Texture != null)
                Object.Destroy(photoData.Texture);
        }
        capturedPhotos.Clear();
    }
}
