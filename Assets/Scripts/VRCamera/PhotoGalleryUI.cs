using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PhotoGalleryUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject rawImagePrefab;
    [SerializeField] private Transform galleryParent;

    // We will dynamically create these if they don't exist
    private RawImage bestShotImage;
    private Text bestShotScoreText;
    private Text bestShotDetailsText;
    private Text stampText;
    private GameObject bestShotContainer;

    private void Start()
    {
        SetupBestShotUI();
        PopulateGallery();
    }

    private void SetupBestShotUI()
    {
        // 1. Create a Best Shot Container next to the gallery parent
        bestShotContainer = new GameObject("BestShotContainer", typeof(RectTransform));
        bestShotContainer.transform.SetParent(transform, false);
        
        RectTransform rt = bestShotContainer.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(50, 50);
        rt.offsetMax = new Vector2(-50, -50);

        // Adjust galleryParent to take the right half
        if (galleryParent != null)
        {
            RectTransform grt = galleryParent.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(0.5f, 0);
            grt.anchorMax = new Vector2(1, 1);
            grt.offsetMin = new Vector2(50, 50);
            grt.offsetMax = new Vector2(-50, -50);
        }

        // 2. Best Shot Image
        GameObject imgObj = new GameObject("BestShotImage", typeof(RectTransform), typeof(RawImage));
        imgObj.transform.SetParent(bestShotContainer.transform, false);
        bestShotImage = imgObj.GetComponent<RawImage>();
        RectTransform imgRt = imgObj.GetComponent<RectTransform>();
        imgRt.anchorMin = new Vector2(0, 0.3f);
        imgRt.anchorMax = new Vector2(1, 1);
        imgRt.offsetMin = Vector2.zero;
        imgRt.offsetMax = Vector2.zero;

        // 3. Stamp Text (over the image)
        GameObject stampObj = new GameObject("StampText", typeof(RectTransform), typeof(Text), typeof(Outline));
        stampObj.transform.SetParent(imgObj.transform, false);
        stampText = stampObj.GetComponent<Text>();
        stampText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        stampText.fontSize = 120;
        stampText.fontStyle = FontStyle.Bold;
        stampText.alignment = TextAnchor.MiddleCenter;
        stampText.color = Color.red;
        
        Outline outline = stampObj.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(3, -3);

        RectTransform stampRt = stampObj.GetComponent<RectTransform>();
        stampRt.anchoredPosition = Vector2.zero;
        stampRt.sizeDelta = new Vector2(400, 200);
        stampRt.localRotation = Quaternion.Euler(0, 0, 15f); // Tilted stamp
        stampObj.SetActive(false); // Hide initially

        // 4. Details Text
        GameObject detailsObj = new GameObject("DetailsText", typeof(RectTransform), typeof(Text));
        detailsObj.transform.SetParent(bestShotContainer.transform, false);
        bestShotDetailsText = detailsObj.GetComponent<Text>();
        bestShotDetailsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bestShotDetailsText.fontSize = 24;
        bestShotDetailsText.color = Color.white;
        bestShotDetailsText.alignment = TextAnchor.UpperLeft;

        RectTransform detRt = detailsObj.GetComponent<RectTransform>();
        detRt.anchorMin = new Vector2(0, 0);
        detRt.anchorMax = new Vector2(1, 0.3f);
        detRt.offsetMin = new Vector2(10, 10);
        detRt.offsetMax = new Vector2(-10, -10);
    }

    [ContextMenu("Populate Gallery")]
    public void PopulateGallery()
    {
        // Clear existing children in container
        foreach (Transform child in galleryParent)
        {
            Destroy(child.gameObject);
        }

        List<PhotoData> photos = PhotoGalleryManager.GetPhotos();
        if (photos.Count == 0)
        {
            Debug.Log("PhotoGalleryUI: No photos captured yet.");
            bestShotContainer.SetActive(false);
            return;
        }

        bestShotContainer.SetActive(true);

        PhotoData bestShot = photos[0];

        foreach (PhotoData data in photos)
        {
            if (data == null || data.Texture == null) continue;

            if (data.TotalScore > bestShot.TotalScore)
            {
                bestShot = data;
            }

            GameObject newImgObj = Instantiate(rawImagePrefab, galleryParent);
            RawImage rawImage = newImgObj.GetComponent<RawImage>();
            if (rawImage != null)
            {
                rawImage.texture = data.Texture;
            }

            // Add simple score text under the thumbnail
            GameObject scoreTextObj = new GameObject("ScoreText", typeof(RectTransform), typeof(Text));
            scoreTextObj.transform.SetParent(newImgObj.transform, false);
            Text t = scoreTextObj.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = $"Score: {data.TotalScore}";
            t.color = Color.yellow;
            t.alignment = TextAnchor.LowerCenter;
            t.fontSize = 20;
            RectTransform trt = scoreTextObj.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.offsetMin = new Vector2(0, 0);
            trt.offsetMax = new Vector2(0, 30);
        }

        // Setup Best Shot Display
        bestShotImage.texture = bestShot.Texture;
        bestShotDetailsText.text = $"<color=yellow><size=32>BEST SHOT SCORE: {bestShot.TotalScore} / 100</size></color>\n\n" +
                                   $"Center Bonus: +{bestShot.CenterBonus}\n" +
                                   $"Gaze Bonus: +{bestShot.GazeBonus}\n" +
                                   $"Action Bonus: +{bestShot.PoseBonus}\n\n" +
                                   $"Evaluation: <b>{bestShot.Rank} RANK</b>";

        // Play Stamp Animation
        stampText.text = bestShot.Rank;
        if (bestShot.Rank == "S") stampText.color = new Color(1f, 0.8f, 0f); // Gold
        else if (bestShot.Rank == "A") stampText.color = Color.red;
        else if (bestShot.Rank == "B") stampText.color = Color.green;
        else stampText.color = Color.blue;

        StartCoroutine(StampAnimationRoutine());
        
        Debug.Log($"PhotoGalleryUI: Rendered {photos.Count} photos in result UI.");
    }

    private IEnumerator StampAnimationRoutine()
    {
        stampText.gameObject.SetActive(false);
        yield return new WaitForSeconds(0.5f); // Wait a bit before stamping

        stampText.gameObject.SetActive(true);
        RectTransform rt = stampText.GetComponent<RectTransform>();
        
        float duration = 0.3f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Scale from 3 down to 1 (ease in)
            float scale = Mathf.Lerp(3f, 1f, t * t);
            rt.localScale = new Vector3(scale, scale, 1f);
            
            // Fade in
            Color c = stampText.color;
            c.a = t;
            stampText.color = c;
            
            yield return null;
        }
        
        rt.localScale = Vector3.one;
    }

    public void ClearAllStoredPhotos()
    {
        PhotoGalleryManager.ClearPhotos();
        PopulateGallery();
    }
}
