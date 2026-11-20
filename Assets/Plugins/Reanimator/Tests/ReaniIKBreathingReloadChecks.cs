#if UNITY_EDITOR
using System;
using UnityEngine;

public static class ReaniIKBreathingReloadChecks
{
    public static string Run()
    {
        var root = new GameObject("Breathing reload regression");
        try
        {
            var nose = new GameObject("nose").transform; nose.SetParent(root.transform, false);
            var mouth = new GameObject("mouth").transform; mouth.SetParent(root.transform, false);
            var baseScale = new Vector3(1.2f,.8f,1);
            var basePosition = new Vector3(.3f,-.2f,0);
            var baseRotation = Quaternion.Euler(0,0,13);
            nose.localScale = baseScale; nose.localPosition = basePosition; nose.localRotation = baseRotation;
            var breath = new RigBreathing2D { nose = nose, mouth = mouth, noseRotationDegrees = 12 };
            for (int i=0;i<20;i++)
            {
                breath.Evaluate(root.transform,.17f,1,1);
                if (nose.localScale.x <= baseScale.x) throw new Exception("Regression did not exercise expanded nose.");
                // Assembly reload keeps serialized state and discards nonserialized fields.
                breath = JsonUtility.FromJson<RigBreathing2D>(JsonUtility.ToJson(breath));
                breath.Restore();
                if (Vector3.Distance(nose.localScale,baseScale) > .00001f || Vector3.Distance(nose.localPosition,basePosition) > .00001f || Quaternion.Angle(nose.localRotation,baseRotation) > .01f)
                    throw new Exception("Breathing pose accumulated across serialization/reload.");
                if (Vector3.Distance(mouth.localScale,Vector3.one) > .00001f) throw new Exception("Mouth compression accumulated across reload.");
            }
            breath.Evaluate(root.transform,.1f,1,1);
            nose.localScale = Vector3.one * 2;
            breath.Restore();
            if (nose.localScale != Vector3.one * 2) throw new Exception("Restore overwrote an authored scale change.");
            return "PASS: 20 reload round trips preserve nose scale/position/rotation, mouth scale and manual edits.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
#endif
