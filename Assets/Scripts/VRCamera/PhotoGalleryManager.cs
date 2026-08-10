using System.Collections.Generic;
using UnityEngine;

public static class PhotoGalleryManager
{
    private static List<Texture2D> capturedPhotos = new List<Texture2D>();

    public static List<Texture2D> GetPhotos()
    {
        return capturedPhotos;
    }

    public static void AddPhoto(Texture2D photo)
    {
        capturedPhotos.Add(photo);
    }

    public static void ClearPhotos()
    {
        foreach (var photo in capturedPhotos)
        {
            if (photo != null)
                Object.Destroy(photo);
        }
        capturedPhotos.Clear();
    }
}
