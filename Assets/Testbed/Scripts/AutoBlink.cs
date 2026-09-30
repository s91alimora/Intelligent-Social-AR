using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoBlink : MonoBehaviour
{
    public string leftEyeBlendshapeName = "blendShape1.AK_09_EyeBlinkLeft";
    public string rightEyeBlendshapeName = "blendShape1.AK_10_EyeBlinkRight";
    public SkinnedMeshRenderer skinnedMesh;

    public float blinkSpeed = 8f;
    public float minBlinkInterval = 2f;
    public float maxBlinkInterval = 6f;
    public int leftEyeIndex = 0;
    public int rightEyeIndex = 0;

    private float blinkTimer;
    private bool isBlinking;
    private float blinkPhase;

    void Start()
    {
        skinnedMesh = GetComponent<SkinnedMeshRenderer>();

        Mesh mesh = skinnedMesh.sharedMesh;

        leftEyeIndex = mesh.GetBlendShapeIndex(leftEyeBlendshapeName);
        rightEyeIndex = mesh.GetBlendShapeIndex(rightEyeBlendshapeName);

        if (leftEyeIndex == -1 || rightEyeIndex == -1)
            Debug.LogWarning("Blink blendshape not found. Check names on " + gameObject.name);

        ResetTimer();
    }

    void Update()
    {
        if (leftEyeIndex == -1 || rightEyeIndex == -1)
            return;

        blinkTimer -= Time.deltaTime;

        if (blinkTimer <= 0f && !isBlinking)
        {
            isBlinking = true;
            blinkPhase = 0f;
        }

        if (isBlinking)
        {
            blinkPhase += Time.deltaTime * blinkSpeed;

            // 0 → 1 → 0 curve
            float t = Mathf.Sin(blinkPhase);
            if (t <= 0f)
            {
                t = 0f;
                isBlinking = false;
                ResetTimer();
            }

            float weight = Mathf.Clamp01(t) * 100f;

            skinnedMesh.SetBlendShapeWeight(leftEyeIndex, weight);
            skinnedMesh.SetBlendShapeWeight(rightEyeIndex, weight);
        }
    }
    void ResetTimer()
    {
        blinkTimer = Random.Range(minBlinkInterval, maxBlinkInterval);
    }
}
