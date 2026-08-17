using UnityEngine;

public static class PhotoEvaluator
{
    private const int PoseActiveScore = 10;
    private const int PoseTimingMaxScore = 10;
    private const int PoseExpressionMaxScore = 10;

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
        var animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude);
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
        // 10 points each for an active special pose, good animation timing,
        // and how widely the arms are posed. This avoids the previous 0/30-only result.
        if (isOnScreen)
        {
            data.PoseBonus = CalculatePoseBonus(targetAnim);
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

    private static int CalculatePoseBonus(Animator animator)
    {
        bool hasSpecialPose = false;
        AnimatorStateInfo evaluatedState = default;

        int reactionLayer = animator.GetLayerIndex("ReactionLayer");
        if (reactionLayer < 0 && animator.layerCount > 1)
        {
            reactionLayer = 1;
        }

        if (reactionLayer >= 0 && animator.GetLayerWeight(reactionLayer) > 0.1f)
        {
            evaluatedState = animator.IsInTransition(reactionLayer)
                ? animator.GetNextAnimatorStateInfo(reactionLayer)
                : animator.GetCurrentAnimatorStateInfo(reactionLayer);

            hasSpecialPose = !evaluatedState.IsName("Empty");
        }

        if (!hasSpecialPose)
        {
            AnimatorStateInfo baseState = animator.GetCurrentAnimatorStateInfo(0);
            if (baseState.IsTag("Jump") || baseState.IsName("Jump") || baseState.IsName("Win"))
            {
                hasSpecialPose = true;
                evaluatedState = baseState;
            }
        }

        if (!hasSpecialPose)
        {
            return 0;
        }

        int timingScore = CalculatePoseTimingScore(evaluatedState);
        int expressionScore = CalculateArmExpressionScore(animator);
        return Mathf.Clamp(PoseActiveScore + timingScore + expressionScore, 0, 30);
    }

    private static int CalculatePoseTimingScore(AnimatorStateInfo state)
    {
        float progress = state.loop
            ? Mathf.Repeat(state.normalizedTime, 1f)
            : Mathf.Clamp01(state.normalizedTime);

        // The middle of the motion is treated as its photographic highlight.
        // SmoothStep keeps the score change gradual instead of introducing another threshold.
        float highlight = 1f - Mathf.Abs(progress - 0.5f) * 2f;
        highlight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(highlight));
        return Mathf.RoundToInt(highlight * PoseTimingMaxScore);
    }

    private static int CalculateArmExpressionScore(Animator animator)
    {
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        if (head == null || hips == null || leftShoulder == null || rightShoulder == null ||
            leftHand == null || rightHand == null)
        {
            return 0;
        }

        float upperBodyHeight = Vector3.Distance(head.position, hips.position);
        if (upperBodyHeight < 0.001f)
        {
            return 0;
        }

        float leftReach = Vector3.Distance(leftShoulder.position, leftHand.position);
        float rightReach = Vector3.Distance(rightShoulder.position, rightHand.position);
        float normalizedReach = (leftReach + rightReach) / (2f * upperBodyHeight);

        // Folded/close arms score low; arms extended away from the torso score high.
        float expression = Mathf.InverseLerp(0.35f, 1.1f, normalizedReach);
        return Mathf.RoundToInt(expression * PoseExpressionMaxScore);
    }
}
