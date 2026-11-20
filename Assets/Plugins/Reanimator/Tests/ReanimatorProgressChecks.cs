#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;

public partial class Reanimator
{
    public async Task<string> VerifyFullReanimateProgress()
    {
        if(reanimateRunning)throw new InvalidOperationException("A run is already active.");
        var timer=System.Diagnostics.Stopwatch.StartNew();
        double previous=EditorApplication.timeSinceStartup,maxGap=0;
        float lastProgress=0;bool regressed=false;int ticks=0;
        void Tick()
        {
            double now=EditorApplication.timeSinceStartup;maxGap=Math.Max(maxGap,now-previous);previous=now;ticks++;
            if(reanimateProgress<lastProgress)regressed=true;
            lastProgress=reanimateProgress;
        }
        EditorApplication.update+=Tick;
        try
        {
            RunReanimate(activeAssetPath);
            while(reanimateRunning)await Task.Delay(50);
            if(reanimateResultError || reanimateProgress!=1f || regressed || ticks<2)
                throw new Exception("Progress/run validation failed: "+reanimateResult);
            return $"PASS: full Reanimate completed in {timer.Elapsed.TotalSeconds:F2}s, {ticks} editor updates, max editor gap {maxGap:F3}s, monotonic progress reached 100%. {reanimateResult}";
        }
        finally { EditorApplication.update-=Tick; }
    }
    public static string VerifyLegacyMetadataBatchCopy()
    {
        // Work only on an unapplied serialized buffer of the triggering asset.
        using var serialized=new SerializedObject(AssetImporter.GetAtPath("Assets/Reanimatior Testing/1.psd"));
        var source=serialized.FindProperty("m_RigSpriteImportData");
        var target=serialized.FindProperty("m_SpriteImportData");
        if(source==null || target==null || source.arraySize==0)throw new Exception("PSD metadata fixture missing.");
        source.GetArrayElementAtIndex(0).FindPropertyRelative("m_Name").stringValue="__batch_copy_regression__";
        var copy=typeof(ReanimatorOldEditorMetadataStager).GetMethod("CopySpriteImportListIfDifferent",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        if(!(bool)copy.Invoke(null,new object[]{target,source}))throw new Exception("Changed metadata was not copied.");
        if(!SerializedProperty.DataEquals(source,target))throw new Exception("Bulk copy lost nested metadata.");
        if((bool)copy.Invoke(null,new object[]{target,source}))throw new Exception("Repeated metadata staging was not a no-op.");
        return "PASS: complete PSD metadata copied including nested arrays; repeated staging is a no-op; asset untouched.";
    }
    public async Task<string> VerifyCooperativeReanimateProgress()
    {
        if(reanimateRunning)throw new InvalidOperationException("Wait for the active run.");
        var oldPath=reanimateRunPath;
        var oldProgress=reanimateProgress;
        var oldTitle=reanimateProgressTitle;
        int ticks=0,actions=0;
        void Tick(){ticks++;}
        EditorApplication.update+=Tick;
        try
        {
            reanimateRunPath=GetSelectedAssetPath();
            reanimateProgress=0f;
            var first=RunReanimateStageAsync("Progress regression",.4f,()=>{actions++;return "Complete";});
            if(first.IsCompleted || actions!=0)throw new Exception("Work executed before yielding to the editor.");
            await first;
            await RunReanimateStageAsync("Progress regression",.2f,()=>{actions++;return "Complete";});
            if(ticks==0 || actions!=2 || reanimateProgress!=.4f)throw new Exception("Editor did not tick or progress moved backwards.");
            reanimateRunPath="__invalid_progress_test__";
            bool rejected=false;
            try { await RunReanimateStageAsync("Wrong asset",.5f,()=>{actions++;return "Unexpected";}); }
            catch(InvalidOperationException){rejected=true;}
            if(!rejected || actions!=2)throw new Exception("Changed asset executed a queued operation.");
            return "PASS: editor updates between stages, work waits for a frame, progress never regresses, changed assets reject queued work.";
        }
        finally
        {
            EditorApplication.update-=Tick;
            reanimateRunPath=oldPath;reanimateProgress=oldProgress;reanimateProgressTitle=oldTitle;
            Repaint();
        }
    }
}
#endif
