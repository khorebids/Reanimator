#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public partial class PsdLayerPreviewRenderer
{
    public string ApplyAutoSkeletonPoseJson(
        string poseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls)
    {
        var created = BuildAutoSkeletonBoneSpecs(
            poseJson,
            namingReferenceAssetPath,
            includeFingers,
            includeBreastControls,
            includeEyeControls);
        return ApplyAutoSkeletonBoneSpecs(created, "Auto Skeleton", 0);
    }

    public string ApplyAutoSkeletonPoseJsonWithSecondaryArms(
        string primaryPoseJson,
        string secondaryArmsPoseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls)
    {
        var primary = BuildAutoSkeletonBoneSpecs(
            primaryPoseJson,
            namingReferenceAssetPath,
            includeFingers,
            includeBreastControls,
            includeEyeControls);
        var secondary = BuildAutoSkeletonBoneSpecs(
            secondaryArmsPoseJson,
            namingReferenceAssetPath,
            includeFingers,
            false,
            false);
        var addedArmBones = AppendSecondaryArmChains(primary, secondary);
        if (addedArmBones <= 0)
            throw new InvalidOperationException("Second Auto Skeleton pass produced no arm bones to merge.");

        return ApplyAutoSkeletonBoneSpecs(primary, "Auto Skeleton (two arm pairs)", addedArmBones);
    }

    private List<AutoSkeletonBoneSpec> BuildAutoSkeletonBoneSpecs(
        string poseJson,
        string namingReferenceAssetPath,
        bool includeFingers,
        bool includeBreastControls,
        bool includeEyeControls)
    {
        if (string.IsNullOrWhiteSpace(poseJson))
            throw new InvalidOperationException("Auto Skeleton pose JSON is empty.");

        var docSize = DocumentSize;
        if (docSize.x <= 0 || docSize.y <= 0)
            throw new InvalidOperationException("Auto Skeleton needs a ready PSD preview cache.");

        var frame = ParseFirstPoseFrame(poseJson);
        if (frame == null || frame.people == null || frame.people.Length == 0)
            throw new InvalidOperationException("Auto Skeleton pose JSON contains no detected people.");

        var person = SelectBestPerson(frame.people);
        if (person == null || person.pose_keypoints_2d == null || person.pose_keypoints_2d.Length < 18)
            throw new InvalidOperationException("Auto Skeleton pose JSON contains no body keypoints.");

        var naming = AutoSkeletonNaming.FromReferenceAsset(namingReferenceAssetPath);
        var builder = new AutoSkeletonBuilder(
            this,
            frame,
            person,
            naming,
            docSize,
            includeFingers,
            includeBreastControls,
            includeEyeControls);
        var created = builder.Build();
        if (created.Count == 0)
            throw new InvalidOperationException("Auto Skeleton did not find enough keypoints to create bones.");

        return created;
    }

    private string ApplyAutoSkeletonBoneSpecs(
        List<AutoSkeletonBoneSpec> created,
        string undoLabel,
        int secondaryArmBoneCount)
    {
        if (created == null || created.Count == 0)
            throw new InvalidOperationException("Auto Skeleton produced no bones to apply.");

        RegisterUndo(undoLabel);
        bones.Clear();
        selectedBoneIndex = -1;

        for (var i = 0; i < created.Count; i++)
        {
            var src = created[i];
            var parentIndex = src.parentIndex;
            var direction = src.end - src.start;
            var length = Mathf.Max(1f, direction.magnitude);
            var worldRotation = Quaternion.AngleAxis(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, Vector3.forward);
            var localPosition = src.start;
            var localRotation = worldRotation;

            if (!bonesTreatAsWorldSpace && parentIndex >= 0)
            {
                RecomputeBoneWorldTransforms();
                localPosition = ConvertBoneWorldPositionToLocal(parentIndex, src.start);
                localRotation = ConvertBoneWorldRotationToLocal(parentIndex, worldRotation);
            }

            bones.Add(new BoneInfo
            {
                index = i,
                name = src.name,
                parentId = parentIndex,
                position = localPosition,
                rotation = localRotation,
                initialPosition = localPosition,
                initialRotation = localRotation,
                length = length,
                depth = i,
                color = CreateDefaultBoneColor(i),
                visible = true
            });
        }

        // Nipple controls must never be drawn below the neck control. They are
        // created earlier in the hierarchy, so their default index-based depth
        // would otherwise be smaller. Preserve a greater depth when present,
        // or raise both nipple bones to the neck depth.
        var neckDepth = -1;
        for (var i = 0; i < bones.Count; i++)
        {
            if (bones[i] != null && NormalizeAutoWeightName(bones[i].name) == "neck")
            {
                neckDepth = bones[i].depth;
                break;
            }
        }
        if (neckDepth >= 0)
        {
            for (var i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (bone == null)
                    continue;
                var normalizedName = NormalizeAutoWeightName(bone.name);
                if (normalizedName == "lnipple" || normalizedName == "nipplel" || normalizedName == "leftnipple" ||
                    normalizedName == "rnipple" || normalizedName == "nippler" || normalizedName == "rightnipple")
                {
                    bone.depth = Mathf.Max(bone.depth, neckDepth);
                }
            }
        }

        RecomputeBoneWorldTransforms();
        CaptureInitialBonePoseFromCurrent();
        BuildBoneDrawOrder();
        var clearedWeightCount = ClearAllSpriteWeightsForAutoSkeleton();
        bonePoseAtRest = true;
        MarkBoneStateChanged();

        var secondaryStatus = secondaryArmBoneCount > 0
            ? ", including " + secondaryArmBoneCount + " bones from the second arm pass"
            : string.Empty;
        return "created " + bones.Count + " bones from pose JSON" + secondaryStatus +
               ", cleared " + clearedWeightCount + " sprite weights";
    }

    private static int AppendSecondaryArmChains(
        List<AutoSkeletonBoneSpec> primary,
        List<AutoSkeletonBoneSpec> secondary)
    {
        if (primary == null || secondary == null)
            return 0;

        var primarySpineIndex = primary.FindIndex(spec => spec != null &&
            NormalizeAutoWeightName(spec.name) == "spine");
        if (primarySpineIndex < 0)
            throw new InvalidOperationException("Primary Auto Skeleton pass has no spine bone for the second arms.");

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < primary.Count; i++)
        {
            if (primary[i] != null && !string.IsNullOrWhiteSpace(primary[i].name))
                usedNames.Add(primary[i].name);
        }

        var added = 0;
        added += AppendSecondaryArmChain(primary, secondary, "l", primarySpineIndex, usedNames);
        added += AppendSecondaryArmChain(primary, secondary, "r", primarySpineIndex, usedNames);
        return added;
    }

    private static int AppendSecondaryArmChain(
        List<AutoSkeletonBoneSpec> primary,
        List<AutoSkeletonBoneSpec> secondary,
        string side,
        int primarySpineIndex,
        HashSet<string> usedNames)
    {
        var rootNormalizedName = side + "arm";
        var rootIndex = secondary.FindIndex(spec => spec != null &&
            NormalizeAutoWeightName(spec.name) == rootNormalizedName);
        if (rootIndex < 0)
            return 0;

        var chain = new HashSet<int> { rootIndex };
        for (var pass = 0; pass < secondary.Count; pass++)
        {
            var changed = false;
            for (var i = 0; i < secondary.Count; i++)
            {
                var spec = secondary[i];
                if (spec == null || chain.Contains(i) || !chain.Contains(spec.parentIndex))
                    continue;
                chain.Add(i);
                changed = true;
            }
            if (!changed)
                break;
        }

        var indexMap = new Dictionary<int, int>();
        var added = 0;
        for (var i = 0; i < secondary.Count; i++)
        {
            if (!chain.Contains(i))
                continue;

            var source = secondary[i];
            var parentIndex = i == rootIndex
                ? primarySpineIndex
                : indexMap.TryGetValue(source.parentIndex, out var mappedParent)
                    ? mappedParent
                    : primarySpineIndex;
            var name = MakeSecondaryArmBoneName(source.name, side, usedNames);
            var targetIndex = primary.Count;
            primary.Add(new AutoSkeletonBoneSpec
            {
                name = name,
                parentIndex = parentIndex,
                start = source.start,
                end = source.end
            });
            indexMap[i] = targetIndex;
            added++;
        }

        return added;
    }

    private static string MakeSecondaryArmBoneName(string sourceName, string side, HashSet<string> usedNames)
    {
        var normalized = NormalizeAutoWeightName(sourceName);
        string baseName;
        if (normalized == side + "arm")
            baseName = side + "_arm_2";
        else if (normalized == side + "arm1")
            baseName = side + "_arm_3";
        else if (normalized == side + "hand")
            baseName = side + "_hand_2";
        else
            baseName = (string.IsNullOrWhiteSpace(sourceName) ? side + "_arm_bone" : sourceName.Trim()) + "_2";

        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
            candidate = baseName + "_" + suffix++;
        return candidate;
    }

    private static AutoSkeletonPoseFrame ParseFirstPoseFrame(string poseJson)
    {
        var json = (poseJson ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(json))
            return null;

        AutoSkeletonPoseFrameList wrapped;
        if (json.StartsWith("[", StringComparison.Ordinal))
        {
            wrapped = JsonUtility.FromJson<AutoSkeletonPoseFrameList>("{\"frames\":" + json + "}");
            return wrapped != null && wrapped.frames != null && wrapped.frames.Length > 0 ? wrapped.frames[0] : null;
        }

        if (json.StartsWith("{", StringComparison.Ordinal))
        {
            var frame = JsonUtility.FromJson<AutoSkeletonPoseFrame>(json);
            if (frame != null && frame.people != null)
                return frame;

            wrapped = JsonUtility.FromJson<AutoSkeletonPoseFrameList>(json);
            return wrapped != null && wrapped.frames != null && wrapped.frames.Length > 0 ? wrapped.frames[0] : null;
        }

        return null;
    }

    private static AutoSkeletonPerson SelectBestPerson(AutoSkeletonPerson[] people)
    {
        AutoSkeletonPerson best = null;
        var bestScore = float.MinValue;
        for (var i = 0; i < people.Length; i++)
        {
            var person = people[i];
            var score = SumConfidence(person != null ? person.pose_keypoints_2d : null);
            if (score <= bestScore)
                continue;

            best = person;
            bestScore = score;
        }

        return best;
    }

    private static float SumConfidence(float[] keypoints)
    {
        if (keypoints == null)
            return 0f;

        var sum = 0f;
        for (var i = 2; i < keypoints.Length; i += 3)
            sum += keypoints[i];
        return sum;
    }

    [Serializable]
    private sealed class AutoSkeletonPoseFrameList
    {
        public AutoSkeletonPoseFrame[] frames = Array.Empty<AutoSkeletonPoseFrame>();
    }

    [Serializable]
    private sealed class AutoSkeletonPoseFrame
    {
        public int canvas_width = 0;
        public int canvas_height = 0;
        public AutoSkeletonPerson[] people = Array.Empty<AutoSkeletonPerson>();
    }

    [Serializable]
    private sealed class AutoSkeletonPerson
    {
        public float[] pose_keypoints_2d = Array.Empty<float>();
        public float[] hand_left_keypoints_2d = Array.Empty<float>();
        public float[] hand_right_keypoints_2d = Array.Empty<float>();
        public float[] face_keypoints_2d = Array.Empty<float>();
    }

    private sealed class AutoSkeletonNaming
    {
        private readonly HashSet<string> referenceNames;

        private AutoSkeletonNaming(HashSet<string> referenceNames)
        {
            this.referenceNames = referenceNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static AutoSkeletonNaming FromReferenceAsset(string assetPath)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                var metaPath = assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? assetPath : assetPath + ".meta";
                if (File.Exists(metaPath))
                {
                    var text = File.ReadAllText(metaPath);
                    foreach (Match match in Regex.Matches(text, "^\\s*-\\s*name:\\s*(.+?)\\s*$", RegexOptions.Multiline))
                    {
                        var value = match.Groups[1].Value.Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(value))
                            names.Add(value);
                    }
                }
            }

            return new AutoSkeletonNaming(names);
        }

        public string Pick(string fallback, params string[] candidates)
        {
            if (candidates != null)
            {
                for (var i = 0; i < candidates.Length; i++)
                {
                    var candidate = candidates[i];
                    if (!string.IsNullOrWhiteSpace(candidate) && referenceNames.Contains(candidate))
                        return candidate;
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? "bone" : fallback;
        }
    }

    private sealed class AutoSkeletonBoneSpec
    {
        public string name;
        public int parentIndex;
        public Vector2 start;
        public Vector2 end;
    }

    private sealed class AutoSkeletonBuilder
    {
        private const float MinConfidence = 0.05f;

        private readonly PsdLayerPreviewRenderer owner;
        private readonly AutoSkeletonPoseFrame frame;
        private readonly AutoSkeletonPerson person;
        private readonly AutoSkeletonNaming naming;
        private readonly Vector2 docSize;
        private readonly bool includeFingers;
        private readonly bool includeBreastControls;
        private readonly bool includeEyeControls;
        private readonly List<AutoSkeletonBoneSpec> bones = new List<AutoSkeletonBoneSpec>();
        private readonly Dictionary<string, int> indicesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public AutoSkeletonBuilder(
            PsdLayerPreviewRenderer owner,
            AutoSkeletonPoseFrame frame,
            AutoSkeletonPerson person,
            AutoSkeletonNaming naming,
            Vector2Int docSize,
            bool includeFingers,
            bool includeBreastControls,
            bool includeEyeControls)
        {
            this.owner = owner;
            this.frame = frame;
            this.person = person;
            this.naming = naming;
            this.docSize = docSize;
            this.includeFingers = includeFingers;
            this.includeBreastControls = includeBreastControls;
            this.includeEyeControls = includeEyeControls;
        }

        public List<AutoSkeletonBoneSpec> Build()
        {
            var body = person.pose_keypoints_2d;
            var bodyPointCount = body != null ? body.Length / 3 : 0;
            var body25 = bodyPointCount >= 25;

            TryGetBodyPoint(1, out var neck);
            TryGetBodyPoint(body25 ? 8 : -1, out var midHip);
            TryGetBodyPoint(body25 ? 12 : 11, out var leftHip);
            TryGetBodyPoint(body25 ? 9 : 8, out var rightHip);
            TryGetBodyPoint(5, out var leftShoulder);
            TryGetBodyPoint(2, out var rightShoulder);
            TryGetBodyPoint(0, out var nose);

            if (!IsValid(midHip) && IsValid(leftHip) && IsValid(rightHip))
                midHip = (leftHip + rightHip) * 0.5f;
            if (!IsValid(neck) && IsValid(leftShoulder) && IsValid(rightShoulder))
                neck = (leftShoulder + rightShoulder) * 0.5f;
            if (!IsValid(midHip) || !IsValid(neck))
                return bones;

            var belly = Vector2.Lerp(midHip, neck, 0.32f);
            var spine = Vector2.Lerp(midHip, neck, 0.56f);
            var chest = Vector2.Lerp(midHip, neck, 0.78f);
            var headEnd = IsValid(nose) ? nose : neck + (neck - midHip).normalized * Mathf.Max(20f, Vector2.Distance(midHip, neck) * 0.22f);
            var rootLength = Mathf.Max(1f, Vector2.Distance(neck, headEnd));

            AddSegment(naming.Pick("root", "root"), null, Vector2.zero, Vector2.right * rootLength);
            AddSegment(naming.Pick("hips", "hips"), "root", midHip, belly);
            AddSegment(naming.Pick("belly", "belly"), "hips", belly, spine);
            AddSegment(naming.Pick("spine", "spine"), "belly", spine, chest);
            if (includeBreastControls)
                AddBoobControlBones(chest, neck, midHip, leftShoulder, rightShoulder);
            var neckName = naming.Pick("neck", "neck");
            var headName = naming.Pick("head", "head");
            var headStart = Vector2.Lerp(neck, headEnd, 0.35f);
            AddSegment(neckName, "spine", neck, headStart);
            AddSegment(headName, neckName, headStart, headEnd);
            if (includeEyeControls)
                AddEyesControlBone(headName, neck, headStart, headEnd);
            AddMouthControlBone(headName, neck, headStart, headEnd);
            AddFrontHairControlBone(headName, headStart, headEnd);
            AddBackHairControlBone(headName, headStart, headEnd);

            var openPoseLeftIsViewerLeft = IsOpenPoseLeftSideViewerLeft(leftShoulder, rightShoulder, leftHip, rightHip);
            var openPoseLeftSideName = openPoseLeftIsViewerLeft ? "l" : "r";
            var openPoseRightSideName = openPoseLeftIsViewerLeft ? "r" : "l";

            AddLimb(openPoseLeftSideName, 5, 6, 7, body25 ? 12 : 11, body25 ? 13 : 12, body25 ? 14 : 13, person.hand_left_keypoints_2d);
            AddLimb(openPoseRightSideName, 2, 3, 4, body25 ? 9 : 8, body25 ? 10 : 9, body25 ? 11 : 10, person.hand_right_keypoints_2d);
            if (includeFingers)
            {
                AddHand(openPoseLeftSideName, person.hand_left_keypoints_2d);
                AddHand(openPoseRightSideName, person.hand_right_keypoints_2d);
            }

            return bones;
        }

        private static bool IsOpenPoseLeftSideViewerLeft(
            Vector2 leftShoulder,
            Vector2 rightShoulder,
            Vector2 leftHip,
            Vector2 rightHip)
        {
            if (IsValid(leftShoulder) && IsValid(rightShoulder) && Mathf.Abs(leftShoulder.x - rightShoulder.x) > 0.001f)
                return leftShoulder.x < rightShoulder.x;
            if (IsValid(leftHip) && IsValid(rightHip) && Mathf.Abs(leftHip.x - rightHip.x) > 0.001f)
                return leftHip.x < rightHip.x;
            return true;
        }

        private void AddBoobControlBones(
            Vector2 chest,
            Vector2 neck,
            Vector2 midHip,
            Vector2 leftShoulder,
            Vector2 rightShoulder)
        {
            var torso = neck - midHip;
            var torsoLength = Mathf.Max(1f, torso.magnitude);
            var torsoDir = torso.sqrMagnitude > 0.0001f ? torso.normalized : Vector2.up;
            var rootLength = Mathf.Max(6f, torsoLength * 0.08f);
            var boobsRootName = naming.Pick("boobs_root", "boobs_root", "chest");
            AddSegment(boobsRootName, "spine", chest, chest + torsoDir * rootLength);

            var hasShoulders = IsValid(leftShoulder) && IsValid(rightShoulder);
            if (!hasShoulders)
                return;

            var shoulderSpan = Mathf.Max(1f, Vector2.Distance(leftShoulder, rightShoulder));
            var nippleLength = Mathf.Max(5f, shoulderSpan * 0.08f);
            var downOffset = -torsoDir * Mathf.Max(3f, torsoLength * 0.035f);
            var leftStart = Vector2.Lerp(chest, leftShoulder, 0.34f) + downOffset;
            var rightStart = Vector2.Lerp(chest, rightShoulder, 0.34f) + downOffset;
            AddNippleBone(naming.Pick("nipple_l", "nipple_l", "l_nipple"), boobsRootName, chest, leftStart, nippleLength);
            AddNippleBone(naming.Pick("nipple_r", "nipple_r", "r_nipple"), boobsRootName, chest, rightStart, nippleLength);
        }

        private void AddNippleBone(string name, string parentName, Vector2 chest, Vector2 start, float length)
        {
            var direction = start - chest;
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector2.right;
            AddSegment(name, parentName, start, start + direction.normalized * Mathf.Max(1f, length));
        }

        private void AddEyesControlBone(string headName, Vector2 neck, Vector2 headStart, Vector2 headEnd)
        {
            var headVector = headEnd - headStart;
            var headLength = Mathf.Max(1f, headVector.magnitude);
            var start = TryGetFaceEyeCenter(out var eyeCenter)
                ? eyeCenter
                : Vector2.Lerp(headStart, headEnd, 0.62f);
            start += Vector2.left * Mathf.Max(8f, headLength * 0.24f);

            var up = headEnd - neck;
            if (up.sqrMagnitude <= 0.0001f)
                up = Vector2.up;
            var end = start + up.normalized * Mathf.Max(4f, headLength * 0.14f);
            if ((end - start).sqrMagnitude < 1f)
                end = start + (headEnd - neck).normalized * Mathf.Max(4f, headLength * 0.14f);

            AddSegment(naming.Pick("eyes", "eyes"), headName, start, end);
        }

        private void AddMouthControlBone(string headName, Vector2 neck, Vector2 headStart, Vector2 headEnd)
        {
            if (!TryGetMouthSpriteCenter(out var center))
                return;

            var headLength = Mathf.Max(1f, Vector2.Distance(headStart, headEnd));
            var length = Mathf.Max(4f, headLength * 0.14f);
            var up = headEnd - neck;
            if (up.sqrMagnitude <= 0.0001f)
                up = Vector2.up;

            AddSegment(naming.Pick("mouth", "mouth"), headName, center, center + up.normalized * length);
        }

        private void AddFrontHairControlBone(string headName, Vector2 headStart, Vector2 headEnd)
        {
            if (!TryGetFrontHairSpriteCenter(out var center))
                return;

            var headLength = Mathf.Max(1f, Vector2.Distance(headStart, headEnd));
            AddSegment(naming.Pick("front_hair", "front_hair", "fronthair"), headName, center, center + Vector2.down * headLength);
        }

        private void AddBackHairControlBone(string headName, Vector2 headStart, Vector2 headEnd)
        {
            if (!TryGetBackHairSpriteCenter(out var center))
                return;

            var headLength = Mathf.Max(1f, Vector2.Distance(headStart, headEnd));
            center += Vector2.right * Mathf.Max(4f, headLength * 0.08f);
            AddSegment(naming.Pick("back_hair", "back_hair", "backhair"), headName, center, center + Vector2.down * headLength);
        }

        private bool TryGetFrontHairSpriteCenter(out Vector2 center)
        {
            center = default;
            var layers = owner != null ? owner.Layers : null;
            if (layers == null)
                return false;

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null || !IsFrontHairLayerName(layer.name))
                    continue;

                var rect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                center = rect.center;
                return true;
            }

            return false;
        }

        private bool TryGetBackHairSpriteCenter(out Vector2 center)
        {
            center = default;
            var layers = owner != null ? owner.Layers : null;
            if (layers == null)
                return false;

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null || !IsBackHairLayerName(layer.name))
                    continue;

                var rect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                center = rect.center;
                return true;
            }

            return false;
        }

        private bool TryGetMouthSpriteCenter(out Vector2 center)
        {
            center = default;
            var layers = owner != null ? owner.Layers : null;
            if (layers == null)
                return false;

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer == null || !IsMouthLayerName(layer.name))
                    continue;

                var rect = PsdCoordinateSpace.GetLayerDocumentRect(layer);
                if (rect.width <= 0f || rect.height <= 0f)
                    continue;

                center = rect.center;
                return true;
            }

            return false;
        }

        private static bool IsFrontHairLayerName(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            var builder = new System.Text.StringBuilder(layerName.Length);
            for (var i = 0; i < layerName.Length; i++)
            {
                var ch = char.ToLowerInvariant(layerName[i]);
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            var normalized = builder.ToString();
            return normalized.IndexOf("fronthair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("hairfront", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBackHairLayerName(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            var builder = new System.Text.StringBuilder(layerName.Length);
            for (var i = 0; i < layerName.Length; i++)
            {
                var ch = char.ToLowerInvariant(layerName[i]);
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            var normalized = builder.ToString();
            return normalized.IndexOf("backhair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("hairback", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMouthLayerName(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            var builder = new System.Text.StringBuilder(layerName.Length);
            for (var i = 0; i < layerName.Length; i++)
            {
                var ch = char.ToLowerInvariant(layerName[i]);
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            return builder.ToString().IndexOf("mouth", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryGetFaceEyeCenter(out Vector2 center)
        {
            center = default;
            var sum = Vector2.zero;
            var count = 0;
            for (var i = 36; i <= 47; i++)
            {
                if (!TryGetKeypoint(person.face_keypoints_2d, i, out var point))
                    continue;

                sum += point;
                count++;
            }

            if (count <= 0)
                return false;

            center = sum / count;
            return true;
        }

        private void AddLimb(
            string side,
            int shoulderIndex,
            int elbowIndex,
            int wristIndex,
            int hipIndex,
            int kneeIndex,
            int ankleIndex,
            float[] handKeypoints)
        {
            var isLeft = string.Equals(side, "l", StringComparison.OrdinalIgnoreCase);
            var upperArmName = isLeft
                ? naming.Pick("l_arm", "l_arm", "l_upper_arm")
                : naming.Pick("r_arm", "r_arm", "r_arm_top", "r_upper_arm");
            var forearmName = isLeft
                ? naming.Pick("l_arm_1", "l_arm_1", "l_forearm")
                : naming.Pick("r_arm_1", "r_arm_1", "r_forearm");
            var handName = isLeft
                ? naming.Pick("l_hand", "l_hand")
                : naming.Pick("r_hand", "r_hand");
            var legName = isLeft
                ? naming.Pick("l_leg", "l_leg", "l_thigh")
                : naming.Pick("r_leg", "r_leg", "r_thigh");
            var shinName = isLeft
                ? naming.Pick("l_leg_1", "l_leg_1", "l_shin")
                : naming.Pick("r_leg_1", "r_leg_1", "r_shin");
            var footName = isLeft
                ? naming.Pick("l_foot", "l_foot")
                : naming.Pick("r_foot", "r_foot");

            if (TryGetBodyPoint(shoulderIndex, out var shoulder) &&
                TryGetBodyPoint(elbowIndex, out var elbow))
                AddSegment(upperArmName, "spine", shoulder, elbow);
            if (TryGetBodyPoint(elbowIndex, out elbow) &&
                TryGetBodyPoint(wristIndex, out var wrist))
                AddSegment(forearmName, upperArmName, elbow, wrist);
            if (TryGetBodyPoint(wristIndex, out wrist))
            {
                var handEnd = wrist;
                if (TryGetKeypoint(handKeypoints, 9, out var middleBase))
                    handEnd = middleBase;
                else if (TryGetBodyPoint(elbowIndex, out elbow))
                    handEnd = wrist + (wrist - elbow).normalized * Mathf.Max(8f, Vector2.Distance(wrist, elbow) * 0.18f);
                AddSegment(handName, forearmName, wrist, handEnd);
            }

            var hasHip = TryGetBodyPoint(hipIndex, out var hip);
            var hasKnee = TryGetBodyPoint(kneeIndex, out var knee);
            if (hasHip && hasKnee)
                AddSegment(legName, "hips", hip, knee);
            else if (TryGetCroppedLegBounds(side, out var legBounds))
            {
                // The silhouette still needs a hip even when the pose detector
                // cannot see the knee below the document edge.
                var start = hasHip && legBounds.Contains(hip)
                    ? hip
                    : new Vector2(legBounds.center.x, Mathf.Lerp(legBounds.center.y, legBounds.yMax, 0.75f));
                var end = hasKnee ? knee : new Vector2(legBounds.center.x, legBounds.yMin);
                AddSegment(legName, "hips", start, end);
            }
            if (TryGetBodyPoint(kneeIndex, out knee) &&
                TryGetBodyPoint(ankleIndex, out var ankle))
                AddSegment(shinName, legName, knee, ankle);
            if (TryGetBodyPoint(ankleIndex, out ankle) && TryGetBodyPoint(kneeIndex, out knee))
                AddSegment(footName, shinName, ankle, ankle + (ankle - knee).normalized * Mathf.Max(10f, Vector2.Distance(ankle, knee) * 0.2f));
        }

        private bool TryGetCroppedLegBounds(string side, out Rect bounds)
        {
            foreach (var layer in owner.Layers)
            {
                if (layer == null || !layer.visible)
                    continue;
                var name = NormalizeAutoWeightName(layer.name);
                if (name != side + "leg" && name != (side == "l" ? "leftleg" : "rightleg") &&
                    name != "leg" + side)
                    continue;
                bounds = PsdCoordinateSpace.GetLayerDocumentRect(layer);
                if (bounds.width > 0f && bounds.height > 0f && bounds.yMin <= 1f)
                    return true;
            }

            bounds = default;
            return false;
        }

        private void AddHand(string side, float[] hand)
        {
            if (hand == null || hand.Length < 63)
                return;

            var handName = string.Equals(side, "l", StringComparison.OrdinalIgnoreCase)
                ? naming.Pick("l_hand", "l_hand")
                : naming.Pick("r_hand", "r_hand");
            if (!indicesByName.ContainsKey(handName))
                return;

            AddFinger(side, "thumb", handName, hand, new[] { 0, 1, 2, 3, 4 });
            AddFinger(side, "index", handName, hand, new[] { 0, 5, 6, 7, 8 });
            AddFinger(side, "middle", handName, hand, new[] { 0, 9, 10, 11, 12 });
            AddFinger(side, "ring", handName, hand, new[] { 0, 13, 14, 15, 16 });
            AddFinger(side, "pinky", handName, hand, new[] { 0, 17, 18, 19, 20 });
        }

        private void AddFinger(string side, string finger, string handName, float[] hand, int[] points)
        {
            var parentName = handName;
            for (var i = 0; i < points.Length - 1; i++)
            {
                if (!TryGetKeypoint(hand, points[i], out var start) || !TryGetKeypoint(hand, points[i + 1], out var end))
                    continue;

                var name = side + "_" + finger + "_" + (i + 1);
                AddSegment(name, parentName, start, end);
                parentName = name;
            }
        }

        private bool TryGetBodyPoint(int index, out Vector2 point)
        {
            return TryGetKeypoint(person.pose_keypoints_2d, index, out point);
        }

        private bool TryGetKeypoint(float[] keypoints, int index, out Vector2 point)
        {
            point = default;
            if (keypoints == null || index < 0)
                return false;

            var offset = index * 3;
            if (offset + 2 >= keypoints.Length || keypoints[offset + 2] < MinConfidence)
                return false;

            var rawX = keypoints[offset];
            var rawY = keypoints[offset + 1];
            if (rawX <= 0f || rawY <= 0f)
                return false;

            var canvasWidth = frame.canvas_width > 0 ? frame.canvas_width : owner.DocumentSize.x;
            var canvasHeight = frame.canvas_height > 0 ? frame.canvas_height : owner.DocumentSize.y;
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return false;

            var coordinatesAreNormalized = rawX <= 1.5f && rawY <= 1.5f;
            var x = coordinatesAreNormalized ? rawX * docSize.x : rawX * docSize.x / canvasWidth;
            var y = coordinatesAreNormalized ? (1f - rawY) * docSize.y : (canvasHeight - rawY) * docSize.y / canvasHeight;
            point = new Vector2(x, y);
            return true;
        }

        private int AddSegment(string name, string parentName, Vector2 start, Vector2 end)
        {
            if ((end - start).sqrMagnitude < 1f)
                return -1;

            var uniqueName = MakeUniqueName(name);
            var parentIndex = !string.IsNullOrWhiteSpace(parentName) && indicesByName.TryGetValue(parentName, out var index)
                ? index
                : -1;

            var bone = new AutoSkeletonBoneSpec
            {
                name = uniqueName,
                parentIndex = parentIndex,
                start = start,
                end = end
            };
            var boneIndex = bones.Count;
            bones.Add(bone);
            indicesByName[uniqueName] = boneIndex;
            if (!string.IsNullOrWhiteSpace(name) && !indicesByName.ContainsKey(name))
                indicesByName[name] = boneIndex;
            return boneIndex;
        }

        private string MakeUniqueName(string name)
        {
            var baseName = string.IsNullOrWhiteSpace(name) ? "bone" : name.Trim();
            var candidate = baseName;
            var suffix = 1;
            while (usedNames.Contains(candidate))
            {
                candidate = baseName + "_" + suffix;
                suffix++;
            }

            usedNames.Add(candidate);
            return candidate;
        }

        private static bool IsValid(Vector2 point)
        {
            return point.sqrMagnitude > 0.0001f;
        }
    }
}
#endif
