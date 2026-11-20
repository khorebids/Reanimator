#if UNITY_EDITOR
using System;
using UnityEngine;

public static class ReaniIKBodyScaleChecks
{
    public static string Run()
    {
        var root = new GameObject("Body size regression");
        root.SetActive(false);
        try
        {
            var rig = root.AddComponent<ReaniIK>();
            rig.enabled = false;
            rig.breathing.enabled = false;
            root.transform.localScale = new Vector3(-1.4f, .8f, 1);
            string[] names = { "hips", "belly", "spine", "neck", "head" };
            var chain = new Transform[5];
            var original = new Vector3[5];
            var parent = root.transform;
            for (int i = 0; i < chain.Length; i++)
            {
                chain[i] = new GameObject(names[i]).transform;
                chain[i].SetParent(parent, false);
                original[i] = chain[i].localScale = new Vector3(1f + i * .1f, 1, 1);
                parent = chain[i];
            }
            var arm = new GameObject("l_arm").transform;
            arm.SetParent(chain[2], false);
            arm.localScale = new Vector3(.9f, 1.1f, 1);
            var armBase = arm.localScale;
            var worldBase = arm.lossyScale;
            rig.leftArm.upper = arm;
            rig.bodyScale.distribution = AnimationCurve.Linear(0, 0, 1, 1);
            rig.bodyScale.size = 1.1f;
            for (int repeat = 0; repeat < 20; repeat++) rig.Evaluate(0);
            for (int i = 0; i < chain.Length; i++)
                Require(Vector3.Distance(chain[i].localScale, original[i] * (i == 0 ? 1f : 1 + .1f * (i - 1) / 3f)) < .00001f, "Curve order or repeated scale drift: " + names[i]);
            Require(Vector3.Distance(arm.lossyScale, worldBase) < .0001f, "Full arm compensation changed world size.");
            rig.bodyScale.back_arms = 0;
            rig.Evaluate(0);
            Require(Vector3.Distance(arm.localScale, armBase) < .00001f, "Zero compensation changed arm local size.");
            float inherited = 1f + .1f / 3f;
            Require(Vector3.Distance(arm.lossyScale, worldBase * inherited) < .0001f, "Arm inherited non-parent scale.");
            rig.bodyScale.back_arms = .5f;
            rig.Evaluate(0);
            Require(Vector3.Distance(arm.localScale, armBase * Mathf.Lerp(1, 1 / inherited, .5f)) < .00001f, "Partial compensation mismatch.");
            rig.bodyScale.size = 1;
            rig.Evaluate(0);
            for (int i = 0; i < chain.Length; i++)
                Require(Vector3.Distance(chain[i].localScale, original[i]) < .00001f, "Neutral size failed to restore " + names[i]);
            Require(Vector3.Distance(arm.localScale, armBase) < .00001f, "Neutral size failed to restore arms.");
            rig.bodyScale.size = .5f;
            rig.bodyScale.back_arms = 1;
            rig.Evaluate(0);
            Require(Vector3.Distance(arm.lossyScale, worldBase) < .0001f, "Compensation failed for shrink.");
            rig.bodyScale.Restore();
            Require(Vector3.Distance(arm.localScale, armBase) < .00001f, "Restoring pose retained compensation.");
            rig.bodyScale.distribution = AnimationCurve.Linear(0, 1, 1, 1);
            rig.bodyScale.size = 1.1f;
            rig.Evaluate(0);
            Require(Vector3.Distance(chain[0].localScale, original[0]) < .00001f, "Hips must not participate even with a flat full-strength curve.");
            Require(Vector3.Distance(chain[1].localScale, original[1] * 1.1f) < .00001f, "Belly must be the first scaled bone.");
            rig.bodyScale.Restore();
            root.transform.localRotation = Quaternion.Euler(0, 0, 37);
            for (int i = 0; i < chain.Length; i++) chain[i].localPosition = new Vector3(i * .13f, 1, 0);
            var restWorld = new Vector3[5]; var restLocal = new Vector3[5]; var raisedWorld = new Vector3[5];
            for (int i = 0; i < chain.Length; i++) { restWorld[i] = chain[i].position; restLocal[i] = chain[i].localPosition; }
            rig.bodyScale.back_height = 0;
            rig.Evaluate(0);
            for (int i = 0; i < chain.Length; i++) raisedWorld[i] = chain[i].position;
            Require(Vector3.Dot(raisedWorld[4] - restWorld[4], root.transform.up) > .1f, "Height fixture did not grow.");
            foreach (float compensation in new[] { .5f, 1f })
            {
                rig.bodyScale.back_height = compensation;
                for (int repeat = 0; repeat < 20; repeat++) rig.Evaluate(0);
                for (int i = 0; i < chain.Length; i++)
                {
                    float expectedHeight = Mathf.Lerp(Vector3.Dot(raisedWorld[i], root.transform.up), Vector3.Dot(restWorld[i], root.transform.up), compensation);
                    Require(Mathf.Abs(Vector3.Dot(chain[i].position, root.transform.up) - expectedHeight) < .0001f, "Height compensation drift or double-counted parent movement: " + names[i]);
                    Require(Mathf.Abs(Vector3.Dot(chain[i].position - raisedWorld[i], root.transform.right)) < .0001f, "Height compensation changed horizontal placement.");
                }
            }
            rig.bodyScale.size = 1;
            rig.Evaluate(0);
            for (int i = 0; i < chain.Length; i++) Require(Vector3.Distance(chain[i].localPosition, restLocal[i]) < .0001f, "Neutral size retained height offsets.");
            // Replacing serialized settings must not lose the inverse of the
            // currently displayed pose. Tiny external edits must not cause us
            // to silently retain the entire previous compensation either.
            rig.bodyScale.size = .83f;
            for (int repeat = 0; repeat < 20; repeat++)
            {
                rig.bodyScale.Apply(rig);
                rig.bodyScale = JsonUtility.FromJson<ReaniIKBodyScale>(JsonUtility.ToJson(rig.bodyScale));
                arm.localScale += Vector3.one * .00001f;
                chain[4].localPosition += Vector3.one * .00001f;
                rig.bodyScale.back_arms = repeat % 2 == 0 ? .2f : 1f;
                rig.bodyScale.back_height = repeat % 2 == 0 ? .3f : 1f;
            }
            rig.bodyScale.Restore();
            for (int i = 0; i < chain.Length; i++)
                Require(Vector3.Distance(chain[i].localPosition, restLocal[i]) < .001f, "Serialized compensation accumulated height.");
            Require(Vector3.Distance(arm.localScale, armBase) < .001f, "Serialized compensation accumulated arm scale.");
            return "PASS: four curve samples, hips excluded; arm/height compensation at 0/0.5/1; 20-frame stability, rotated/mirrored/nonuniform roots; serialized settings replacement and external edits do not accumulate offsets.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
#endif
