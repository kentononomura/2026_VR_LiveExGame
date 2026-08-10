using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PhotoGalleryUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject rawImagePrefab;
    [SerializeField] private Transform galleryParent;

    private void Start()
    {
        PopulateGallery();
    }

    [ContextMenu("Populate Gallery")]
    public void PopulateGallery()
    {
        // Clear existing children in container
        foreach (Transform child in galleryParent)
        {
            Destroy(child.gameObject);
        }

        List<Texture2D> photos = PhotoGalleryManager.GetPhotos();
        if (photos.Count == 0)
        {
            Debug.Log("PhotoGalleryUI: No photos captured yet.");
            return;
        }

        foreach (Texture2D photo in photos)
        {
            if (photo == null) continue;

            GameObject newImgObj = Instantiate(rawImagePrefab, galleryParent);
            RawImage rawImage = newImgObj.GetComponent<RawImage>();
            if (rawImage != null)
            {
                rawImage.texture = photo;
            }
        }
        Debug.Log($"PhotoGalleryUI: Rendered {photos.Count} photos in result UI.");
    }

    public void ClearAllStoredPhotos()
    {
        PhotoGalleryManager.ClearPhotos();
        PopulateGallery();
    }
}
