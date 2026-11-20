#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class Reanimator : EditorWindow, IReanimatorAsyncProgressHost
{
    private float reanimateProgress;
    private float reanimateDisplayedProgress;
    private double reanimateProgressLastDraw;
    private string reanimateProgressTitle;
    private string reanimateRunPath;

    public async System.Threading.Tasks.Task<string> RunReanimateStageAsync(string title, float progress, Func<string> apply)
    {
        RequireReanimateAsset(reanimateRunPath);
        reanimateProgress=Mathf.Max(reanimateProgress,Mathf.Clamp01(progress));
        reanimateProgressTitle=title;
        Repaint();
        // Return to the editor loop before each unit of work, allowing input
        // and repaint events to run. Task.Yield alone can drain in one update.
        var completion=new System.Threading.Tasks.TaskCompletionSource<string>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var due=EditorApplication.timeSinceStartup+.025;
        EditorApplication.update+=Execute;
        return await completion.Task;

        void Execute()
        {
            if(EditorApplication.timeSinceStartup<due)return;
            EditorApplication.update-=Execute;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RequireReanimateAsset(reanimateRunPath);
                completion.TrySetResult(RunNativeUndoGroup("Reanimate: "+title,apply));
            }
            catch(Exception exception){completion.TrySetException(exception);}
            finally { Debug.Log($"[Reanimator][Stage] {title}: {timer.ElapsedMilliseconds} ms"); }
        }
    }
    private static readonly string[] ReanimateLimbRootBones = { "l_arm", "r_arm", "l_leg", "r_leg" };
    private static readonly string[][] ReanimateLowerLimbBones =
    {
        new[] { "l_arm_1", "l_forearm", "l_elbow" },
        new[] { "r_arm_1", "r_forearm", "r_elbow" },
        new[] { "l_leg_1", "l_shin", "l_knee" },
        new[] { "r_leg_1", "r_shin", "r_knee" }
    };

    [NonSerialized] private bool reanimateRunning;
    private string reanimateResult;
    private bool reanimateResultError;

    private void DrawReanimateFinalStep(string path, bool ready)
    {
        var validation = ready ? ValidateReanimateDrafts(path) : "Load a layered PSD to start.";
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(validation != null || reanimateRunning || IsReanimateRestartPending))
        {
            if (DrawReanimateIconButton("Reanimate", "Reanimate Run", orange: true, height: 30f))
            {
                if (reanimateChallengeStart < 0 || reanimateChallengeFinish >= 0 ||
                    !string.Equals(reanimateChallengePath, path, StringComparison.OrdinalIgnoreCase))
                    StartReanimateChallenge(path, EditorApplication.timeSinceStartup);
                reanimateRunning = true;
                reanimateResult = null;
                RunReanimate(path);
            }
        }
        if (validation != null)
            EditorGUILayout.HelpBox(validation, MessageType.Info);
        if (!string.IsNullOrEmpty(reanimateResult))
            EditorGUILayout.HelpBox(reanimateResult, reanimateResultError ? MessageType.Error : MessageType.Info);
    }

    private string ValidateReanimateDrafts(string path)
    {
        var renderer = layerPreviewRenderer;
        if (!ReanimatorSkeletronModule.HasPlacement(Api))
            return "Place the skeleton on the Full PSD step.";
        var shoulderError = renderer.GetShoulderDraftError();
        if (shoulderError != null) return shoulderError;
        var face = FindReanimateStepSprite(renderer.Layers, ReanimateFaceStep);
        if (face == null || renderer.CaptureReanimateFaceDraft(face) == null)
            return "Draw the mouth, both lips and nose on the Face step.";
        var error = renderer.GetFaceMouthContourError();
        if (error != null)
            return error;
        if (FindReanimateStepSprite(renderer.Layers, ReanimateChestStep) == null ||
            renderer.CaptureReanimateChestDraft() == null)
            return "Draw both breast contours and place each nipple inside its contour.";
        if (reanimateOtherDrafts.TryGetValue(path, out var others))
        {
            foreach (var sprite in FindReanimateOtherSprites())
            {
                if (!others.TryGetValue(ReanimateOtherSpriteKey(sprite), out var regions))
                    continue;
                for (var i = 0; i < regions.Count; i++)
                {
                    var draft = regions[i];
                    if (draft.mode == ReanimateOtherMode.Skip)
                        continue;
                    var label = $"'{sprite.name}', region {i + 1}";
                    if (draft.contour.Count < 3)
                        return $"Draw a closed outline for {label}.";
                    if (draft.mode == ReanimateOtherMode.SR && draft.points.Count < 2)
                        return $"Draw an SR bone for {label}.";
                    if (draft.mode == ReanimateOtherMode.SRN && draft.points.Count < 3)
                        return $"Draw at least two SRN bones for {label}.";
                    if (draft.mode == ReanimateOtherMode.Projector && draft.projectorSources.Count == 0)
                        return $"Choose Projector sources for {label}.";
                }
            }
        }
        return null;
    }

    private async void RunReanimate(string path)
    {
        Vector2[][] faceDraft = null;
        PsdLayerPreviewRenderer.ReanimateChestDraft chestDraft = null;
        try
        {
            if (this == null)
                return;
            RequireReanimateAsset(path);
            SaveReanimateDraftCache(beforeRun: true);
            reanimateRunning = true;
            reanimateRunPath = path;
            reanimateProgress = 0f;
            reanimateDisplayedProgress = 0f;
            reanimateProgressLastDraw = EditorApplication.timeSinceStartup;
            reanimateProgressTitle = "Preparing PSD";
            Repaint();
            await System.Threading.Tasks.Task.Delay(25);
            RequireReanimateAsset(path);
            var renderer = layerPreviewRenderer;
            // A retry immediately after reload/rollback can still be rebuilding
            // its preview. Finish loading before any automation mutates the mesh.
            faceDraft = renderer.CaptureReanimateFaceDraft(FindReanimateStepSprite(renderer.Layers, ReanimateFaceStep));
            chestDraft = renderer.CaptureReanimateChestDraft();
            if (!renderer.IsPreviewCacheReady(path))
            {
                renderer.RequestPreviewCacheRefresh(path);
                while (renderer.IsPreviewCacheRefreshInProgress)
                {
                    renderer.TickPreviewCacheRefresh();
                    await System.Threading.Tasks.Task.Delay(16);
                    RequireReanimateAsset(path);
                }
                if (faceDraft != null && chestDraft != null)
                    RestoreReanimateDrafts(faceDraft, chestDraft);
            }
            TickReanimateDraftCacheForRun(path);
            var validation = ValidateReanimateDrafts(path);
            if (validation != null)
                throw new InvalidOperationException(validation);
            if (!ReanimatorExternalPanelRegistry.TryGet("Comfimator", out var panel) || panel is not IReanimatorReanimatePanel automation)
                throw new InvalidOperationException("Comfimator is unavailable.");

            var skeleton = ReanimatorSkeletronModule.CapturePlacement(Api);
            var bellyParentRoot = renderer.SkeletronBellyParentRoot;
            var shoulderGuide = renderer.SkeletronShoulderGuideOffset;
            var belly = renderer.CaptureSoftContourBellyDraft();
            Func<string> applyBelly = null;
            if (belly.Length >= 3)
                applyBelly = () =>
                {
                    var body = FindReanimateStepSprite(layerPreviewRenderer.Layers, ReanimateChestStep);
                    if (body == null)
                        throw new InvalidOperationException("Body sprite is missing for the belly contour.");
                    return layerPreviewRenderer.ApplyReanimateBelly(body, belly);
                };
            faceDraft = renderer.CaptureReanimateFaceDraft(FindReanimateStepSprite(renderer.Layers, ReanimateFaceStep));
            chestDraft = renderer.CaptureReanimateChestDraft();
            var limbs = reanimateLimbDrafts.TryGetValue(path, out var limbDrafts)
                ? CaptureReanimateLimbMasks(limbDrafts)
                : Array.Empty<Vector2[][]>();
            var others = CaptureReanimateOthers(path);
            await automation.RunReanimateAsync(this, () =>
            {
                RequireReanimateAsset(path);
                layerPreviewRenderer.SkeletronBellyParentRoot = bellyParentRoot;
                layerPreviewRenderer.SkeletronShoulderGuideOffset = shoulderGuide;
                if (!layerPreviewRenderer.TryApplySkeletron(skeleton, out var error, includeHeadControls: true))
                    throw new InvalidOperationException(error);
                return $"Applied Skeletron: {layerPreviewRenderer.Bones.Count} bones";
            }, async () =>
            {
                RequireReanimateAsset(path);
                var skinnedSprites = await ApplyReanimateDraftGeometryAsync(faceDraft,chestDraft,others,belly,limbs);
                await RunReanimateStageAsync("Apply PSD",.90f,()=>{ApplyBeforeCookPrefab(path);return "Complete";});
                RestoreReanimateDrafts(faceDraft, chestDraft);
                await RunReanimateStageAsync("Build prefab",.96f,()=>{
                    var cooked = ReanimatorPrefabCooker.CookNextToPsd(path, skinnedSprites, layerPreviewRenderer);
                    if (!string.IsNullOrEmpty(cooked.Error) || string.IsNullOrEmpty(cooked.PrefabAssetPath))
                        throw new InvalidOperationException(cooked.Error ?? "Prefab cook returned no output.");
                    reanimateResult = cooked.ToStatus();return reanimateResult;
                });
                reanimateResultError = false;
                return reanimateResult;
            }, applyBelly);
            FinishReanimateChallenge(path, EditorApplication.timeSinceStartup, true);
            reanimateProgress=1f;
            reanimateProgressTitle="Complete";
            Repaint();
            await System.Threading.Tasks.Task.Delay(250);
            var seconds = Mathf.Max(0, (int)(reanimateChallengeFinish - reanimateChallengeStart));
            ShowReanimateSuccessNotification(seconds);
        }
        catch (Exception ex)
        {
            if (this == null)
                return;
            FinishReanimateChallenge(path, EditorApplication.timeSinceStartup, false);
            reanimateResult = ex.Message;
            reanimateResultError = true;
            if (faceDraft != null && chestDraft != null && string.Equals(path, activeAssetPath, StringComparison.OrdinalIgnoreCase))
                RestoreReanimateDrafts(faceDraft, chestDraft);
        }
        finally
        {
            reanimateRunning = false;
            reanimateShowIdleMascot = true;
            reanimateIdleMascotError = reanimateResultError;
            reanimateIdleMascotSuccess = !reanimateResultError;
            if(this!=null && string.Equals(activeAssetPath,path,StringComparison.OrdinalIgnoreCase) &&
                layerPreviewRenderer.IsPreviewCacheReady(path))RestoreReanimateDraftCache(path);
            if (this != null)
                Repaint();
        }
    }

    private void RequireReanimateAsset(string path)
    {
        if (this == null || !string.Equals(GetSelectedAssetPath(), path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected PSD changed during Reanimate.");
    }

    private async System.Threading.Tasks.Task<List<string>> ApplyReanimateDraftGeometryAsync(Vector2[][] faceDraft,
        PsdLayerPreviewRenderer.ReanimateChestDraft chestDraft,List<OtherSpriteOperation> others,Vector2[] belly,Vector2[][][] limbs)
    {
        var sprites=await ApplyReanimateFaceAndChestAsync(faceDraft,chestDraft);
        await ApplyReanimateOthersAsync(others,sprites,chestDraft,belly);
        await ApplyReanimateSoftContoursAsync(limbs,sprites);
        IReadOnlyList<string> sideAss=null;
        await RunReanimateStageAsync("Side Ass",.86f,()=>{sideAss=layerPreviewRenderer.ApplySelectedSideAss();return "Complete";});
        foreach(var leg in sideAss)if(!sprites.Contains(leg))sprites.Add(leg);
        return sprites;
    }

    private void RestoreReanimateDrafts(Vector2[][] faceDraft, PsdLayerPreviewRenderer.ReanimateChestDraft chestDraft)
    {
        var renderer = layerPreviewRenderer;
        var face = FindReanimateStepSprite(renderer.Layers, ReanimateFaceStep);
        var chest = FindReanimateStepSprite(renderer.Layers, ReanimateChestStep);
        if (face != null)
            renderer.RestoreReanimateFaceDraft(face, faceDraft);
        if (chest != null)
            renderer.RestoreReanimateChestDraft(chest, chestDraft);
    }

    private async System.Threading.Tasks.Task<List<string>> ApplyReanimateFaceAndChestAsync(Vector2[][] faceDraft,
        PsdLayerPreviewRenderer.ReanimateChestDraft chestDraft)
    {
        var renderer = layerPreviewRenderer;
        var face = FindReanimateStepSprite(renderer.Layers, ReanimateFaceStep);
        var chest = FindReanimateStepSprite(renderer.Layers, ReanimateChestStep);
        if (face == null || chest == null)
            throw new InvalidOperationException("Face or body sprite is missing after ALLIN.");
        var skinnedSprites = new List<string> { face.name, chest.name };
        RestoreReanimateDrafts(faceDraft, chestDraft);
        var previous = renderer.TryGetSelectedLayer(out var layer) ? layer : null;
        try
        {
            await RunReanimateStageAsync("Face",.65f,()=>{
                renderer.SelectLayer(face);
                if (!renderer.TryApplyFaceMouth(lowerLipMaxHeadWeight, upperLipMaxHeadWeight, out var error))
                    throw new InvalidOperationException("Face: " + error);
                return "Complete";
            });
            await RunReanimateStageAsync("Chest",.69f,()=>{
                if (!renderer.TryApplyReanimateChest(chest, out var error))
                    throw new InvalidOperationException("Chest: " + error);
                return "Complete";
            });
        }
        finally
        {
            renderer.SelectLayer(previous);
        }
        return skinnedSprites;
    }

    private async System.Threading.Tasks.Task ApplyReanimateSoftContoursAsync(Vector2[][][] limbs, List<string> skinnedSprites)
    {
        var renderer = layerPreviewRenderer;
        for (var limb = 0; limb < limbs.Length; limb++)
        {
            var step = FirstReanimateLimbStep + limb;
            var sprite = FindReanimateStepSprite(renderer.Layers, step);
            if (sprite == null)
                continue;
            var hips = step == ReanimateHipsStep;
            var root = hips ? "hips" : ReanimateLimbRootBones[limb];
            var lower = hips ? Array.Empty<string>() : ReanimateLowerLimbBones[limb];
            var contours=limbs[limb];
            await RunReanimateStageAsync("Soft contour: "+sprite.name,.82f+.04f*limb/Math.Max(1,limbs.Length),()=>{
                if (!renderer.TryApplyReanimateLimbSc(sprite, root, lower, contours, out var applied, out var error))
                    throw new InvalidOperationException(ReanimateStepNames[step] + ": " + error);
                if (applied && !skinnedSprites.Contains(sprite.name))skinnedSprites.Add(sprite.name);
                return "Complete";
            });
        }
    }

    private static Vector2[][][] CaptureReanimateLimbMasks(List<Vector2>[][] drafts)
    {
        var result=new Vector2[drafts.Length][][];
        for(int limb=0;limb<drafts.Length;limb++)
        {
            if(drafts[limb]==null)continue;
            int count=Math.Min(drafts[limb].Length,ReanimateContourCount(FirstReanimateLimbStep+limb));
            result[limb]=new Vector2[count][];
            for(int i=0;i<count;i++)result[limb][i]=drafts[limb][i]?.ToArray()??Array.Empty<Vector2>();
        }
        return result;
    }
}
#endif
