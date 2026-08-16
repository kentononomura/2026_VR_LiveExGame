using UnityEngine;

public static class PhotoEvaluator
{
    public static PhotoData EvaluateScene(Camera viewfinderCamera, Texture2D photoTexture)
    {
        PhotoData data = new PhotoData();
        data.Texture = photoTexture;
        data.CenterBonus = 0;
        data.GazeBonus = 0;
        data.PoseBonus = 0;
        data.TotalScore = 0;
        data.Rank = "C";

        // Find the target (UnityChan)
        Animator targetAnim = null;
        var animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var a in animators)
        {
            if (a.isHuman && a.gameObject.name.ToLower().Contains("unitychan"))
            {
                targetAnim = a;
                break;
            }
        }

        if (targetAnim == null)
        {
            Debug.LogWarning("PhotoEvaluator: Could not find human target (UnityChan).");
            return data;
        }

        Transform headTransform = targetAnim.GetBoneTransform(HumanBodyBones.Head);
        if (headTransform == null) headTransform = targetAnim.transform;

        // 1. Center Bonus (Max 40)
        Vector3 viewportPos = viewfinderCamera.WorldToViewportPoint(headTransform.position);
        
        bool isOnScreen = viewportPos.z > 0 && viewportPos.x >= 0f && viewportPos.x <= 1f && viewportPos.y >= 0f && viewportPos.y <= 1f;
        
        if (isOnScreen)
        {
            float distFromCenter = Vector2.Distance(new Vector2(viewportPos.x, viewportPos.y), new Vector2(0.5f, 0.5f));
            float centerScoreRaw = 1.0f - Mathf.Clamp01(distFromCenter / 0.5f); // 0 at edges, 1 at center
            data.CenterBonus = Mathf.RoundToInt(centerScoreRaw * 40f);
        }

        // 2. Gaze Bonus (Max 30)
        if (isOnScreen)
        {
            // UnityChan's head bone local Y-axis (up) actually points forward out of her face
            Vector3 faceForward = headTransform.up;
            Vector3 camToHead = (viewfinderCamera.transform.position - headTransform.position).normalized;
            float dot = Vector3.Dot(faceForward, camToHead);
            
            // dot is 1 if her face is perfectly looking at the camera
            // We clamp it so that if she looks away by more than 60 degrees (dot < 0.5), she gets 0.
            float strictGaze = Mathf.Clamp01((dot - 0.5f) * 2f); 
            data.GazeBonus = Mathf.RoundToInt(strictGaze * 30f);
        }

        // 3. Pose Bonus (Max 30)
        if (isOnScreen)
        {
            bool hasSpecialPose = false;
            
            // Check Reaction Layer (Layer 1)
            if (targetAnim.layerCount > 1)
            {
                float weight = targetAnim.GetLayerWeight(1);
                AnimatorStateInfo reactionState = targetAnim.GetCurrentAnimatorStateInfo(1);
                // Assume reaction is active if weight is high and it's not the default empty state
                if (weight > 0.1f && !reactionState.IsName("Empty") && reactionState.normalizedTime < 0.95f)
                {
                    hasSpecialPose = true;
                }
            }
            
            // Check Base Layer for Jumps or special tags
            AnimatorStateInfo baseState = targetAnim.GetCurrentAnimatorStateInfo(0);
            if (baseState.IsTag("Jump") || baseState.IsName("Jump") || baseState.IsName("Win"))
            {
                hasSpecialPose = true;
            }
            
            if (hasSpecialPose)
            {
                data.PoseBonus = 30;
            }
        }

        // 4. Calculate Rank
        if (isOnScreen)
        {
            int baseScore = 10; // Give some points just for capturing her
            data.TotalScore = Mathf.Clamp(baseScore + data.CenterBonus + data.GazeBonus + data.PoseBonus, 0, 100);
        }
        else
        {
            data.TotalScore = 0;
        }

        if (data.TotalScore >= 90) data.Rank = "S";
        else if (data.TotalScore >= 75) data.Rank = "A";
        else if (data.TotalScore >= 60) data.Rank = "B";
        else data.Rank = "C";

        return data;
    }
}
