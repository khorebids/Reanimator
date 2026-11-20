#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ReaniIKBodyScaleResetChecks
{
    public static string Run(ReaniIK prefab)
    {
        var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.SetActive(false);
        var rig = clone.GetComponent<ReaniIK>();
        try
        {
            var names = new[] { "belly", "spine", "neck", "head" };
            var chain = clone.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name) && !t.GetComponent<SpriteRenderer>()).ToArray();
            var arms = new[] { rig.leftArm.upper, rig.rightArm.upper, rig.leftArm2.upper, rig.rightArm2.upper }.Where(t => t).Distinct().ToArray();
            rig.bodyScale.size = 1.1f;
            rig.bodyScale.back_arms = .63f;
            rig.bodyScale.back_height = .37f;
            rig.bodyScale.distribution = AnimationCurve.Linear(0, .3f, 1, .9f);
            foreach (var bone in chain) { bone.localPosition += new Vector3(.1f, .2f, 0); bone.localScale *= 1.08f; }
            foreach (var arm in arms) arm.localScale *= 1.2f;
            rig.bodyScale.Apply(rig);
            // Model an edit after applying height compensation, which prevents
            // equality-based procedural restoration from recovering this point.
            chain.Last().localPosition += new Vector3(.01f, -.02f, 0);
            Undo.IncrementCurrentGroup();
            ReaniIKEditor.ResetBodySizeToPrefab(rig);
            Check();
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            if (Mathf.Abs(rig.bodyScale.size - 1.1f) > .00001f) throw new Exception("Body size reset Undo failed.");
            Undo.PerformRedo();
            Check();
            for (int repeat = 0; repeat < 20; repeat++) rig.bodyScale.Apply(rig);
            rig.bodyScale.Restore();
            Check();
            return "PASS: prefab size/chain positions/scales and arm scales restored despite stale procedural offsets; curve/compensation settings preserved; native Undo/Redo and 20 repeated updates.";

            void Check()
            {
                if (Mathf.Abs(rig.bodyScale.size - prefab.bodyScale.size) > .00001f) throw new Exception("Reset ignored prefab size.");
                if (Mathf.Abs(rig.bodyScale.back_arms - .63f) > .00001f || Mathf.Abs(rig.bodyScale.back_height - .37f) > .00001f || Mathf.Abs(rig.bodyScale.distribution.Evaluate(0) - .3f) > .00001f)
                    throw new Exception("Reset discarded authored distribution settings.");
                foreach (var bone in chain.Concat(arms).Distinct())
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(bone);
                    if (Vector3.Distance(bone.localScale, source.localScale) > .00001f ||
                        (chain.Contains(bone) && Vector3.Distance(bone.localPosition, source.localPosition) > .00001f))
                        throw new Exception("Prefab transform reset mismatch: " + bone.name);
                }
            }
        }
        finally
        {
            Undo.ClearUndo(rig);
            foreach (var bone in clone.GetComponentsInChildren<Transform>(true)) Undo.ClearUndo(bone);
            UnityEngine.Object.DestroyImmediate(clone);
        }
    }
}
#endif
