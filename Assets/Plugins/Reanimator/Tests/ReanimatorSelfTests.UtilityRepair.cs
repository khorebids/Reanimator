#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

internal static partial class ReanimatorSelfTests
{
    private static void PrefabCookerUsesCharacterDocumentCoordinatesInsteadOfPackingAtlas()
    {
        var part = new RectInt(860, 414, 560, 1014);
        var spriteRectInPackingAtlas = new Rect(1178f, 1772f, 560f, 1014f);
        var documentPivot = new Vector2(1783f * 0.5f, 0f);

        var definitionScale = ReanimatorPrefabCooker.ResolveCharacterPartDefinitionScale(
            spriteRectInPackingAtlas,
            part);
        var position = ReanimatorPrefabCooker.ResolveCharacterSpriteLocalPosition(
            part,
            new Vector2(280f, 507f),
            documentPivot,
            definitionScale,
            100f);

        AssertFloat(1f, definitionScale, 0.0001f, "Unscaled character part definition scale.");
        AssertVector(
            new Vector2(2.485f, 9.21f),
            position,
            0.0001f,
            "Cook Prefab should use the PSD document position and pivot, not the 4096 packing-atlas center.");

        var downscaledPosition = ReanimatorPrefabCooker.ResolveCharacterSpriteLocalPosition(
            part,
            new Vector2(140f, 253.5f),
            documentPivot,
            0.5f,
            100f);
        AssertVector(
            new Vector2(-3.215f, 4.605f),
            downscaledPosition,
            0.0001f,
            "Cook Prefab should retain Unity PSD Importer's definition scale for downscaled textures.");

        AssertFloat(
            0.01f,
            ReanimatorPrefabCooker.ResolveCharacterBonePositionScale(0.5f, 50f),
            0.0001f,
            "Downscaled PSD bones should use importer PPU instead of stretching with runtime Sprite PPU.");
        AssertFloat(
            0.01f,
            ReanimatorPrefabCooker.ResolveCharacterBonePositionScale(1f, 100f),
            0.0001f,
            "Ordinary PSD bone scale should remain unchanged.");

        var bonePosition = ReanimatorPrefabCooker.ResolveBoneLocalPosition(
            new Vector3(300f, 200f, ReanimatorBoneOrderAlgorithms.EncodeBoneOrder(17)),
            0.01f);
        AssertVector(new Vector2(3f, 2f), bonePosition, 0.0001f, "Cook Prefab should preserve bone X/Y scaling.");
        AssertFloat(0.0017f, bonePosition.z, 0.000001f, "Cook Prefab should preserve the scaled bone depth used by the sprite bind pose.");
    }

    private static void PrefabCookerAddsOptionalHandLimbSolvers()
    {
        var root = new GameObject("CookFixture");
        try
        {
            var skeleton = new GameObject("skeleton").transform;
            skeleton.SetParent(root.transform, false);
            var rigs = new GameObject("rigs").transform;
            rigs.SetParent(root.transform, false);

            var leftArm = CreateCookFixtureBone(skeleton, "l_arm", new Vector3(-1f, 0f, 0f));
            var leftElbow = CreateCookFixtureBone(leftArm, "l_elbow", new Vector3(-0.5f, 0f, 0f));
            var leftHand = CreateCookFixtureBone(leftElbow, "l_hand", new Vector3(-0.5f, 0f, 0f));
            var rightArm = CreateCookFixtureBone(skeleton, "r_arm", new Vector3(1f, 0f, 0f));
            var rightElbow = CreateCookFixtureBone(rightArm, "r_elbow", new Vector3(0.5f, 0f, 0f));
            var rightHand = CreateCookFixtureBone(rightElbow, "r_hand", new Vector3(0.5f, 0f, 0f));
            var leftArm2 = CreateCookFixtureBone(skeleton, "l_arm_2", new Vector3(-0.8f, 0.5f, 0f));
            var leftElbow2 = CreateCookFixtureBone(leftArm2, "l_arm_3", new Vector3(-0.4f, 0.4f, 0f));
            var leftHand2 = CreateCookFixtureBone(leftElbow2, "l_hand_2", new Vector3(-0.4f, 0.4f, 0f));
            var rightArm2 = CreateCookFixtureBone(skeleton, "r_arm_2", new Vector3(0.8f, 0.5f, 0f));
            var rightElbow2 = CreateCookFixtureBone(rightArm2, "r_arm_3", new Vector3(0.4f, 0.4f, 0f));
            var rightHand2 = CreateCookFixtureBone(rightElbow2, "r_hand_2", new Vector3(0.4f, 0.4f, 0f));
            var softLeft = CreateCookFixtureBone(leftArm, "l_arm_SC", Vector3.up);
            var softRight = CreateCookFixtureBone(rightArm, "r_arm_SC", Vector3.up);
            var bones = new[]
            {
                leftArm, leftElbow, leftHand, rightArm, rightElbow, rightHand,
                leftArm2, leftElbow2, leftHand2, rightArm2, rightElbow2, rightHand2, softLeft, softRight
            };

            var created = ReanimatorPrefabCooker.ConfigureReaniIK(rigs, bones);
            AssertEqual(4, created, "Cook Prefab should add independent hand solvers for both arm pairs and skip absent feet.");

            var leftTarget = rigs.Find("l_hand_target");
            var rightTarget = rigs.Find("r_hand_target");
            var leftTarget2 = rigs.Find("l_hand_2_target");
            var rightTarget2 = rigs.Find("r_hand_2_target");
            AssertTrue(leftTarget != null, "Cook Prefab should create an l_hand target.");
            AssertTrue(rightTarget != null, "Cook Prefab should create an r_hand target.");
            AssertTrue(leftTarget2 != null, "Cook Prefab should create an l_hand_2 target.");
            AssertTrue(rightTarget2 != null, "Cook Prefab should create an r_hand_2 target.");
            AssertVector((Vector2)leftHand.position, leftTarget.position, 0.0001f, "Left hand target should start at the hand without changing the cooked pose.");
            AssertVector((Vector2)rightHand.position, rightTarget.position, 0.0001f, "Right hand target should start at the hand without changing the cooked pose.");
            AssertVector((Vector2)leftHand2.position, leftTarget2.position, 0.0001f, "Second left hand target should start at the second hand.");
            AssertVector((Vector2)rightHand2.position, rightTarget2.position, 0.0001f, "Second right hand target should start at the second hand.");

            var rig = root.GetComponent<ReaniIK>();
            AssertTrue(rig != null, "Cook Prefab should install ReaniIK on the prefab root.");
            AssertEqual(2, rig.softContour.bones.Length, "Cook Prefab should bind all SC bones.");
            AssertTrue(rig.softContour.bones[0] == softLeft && rig.softContour.bones[1] == softRight,
                "Only bones ending in _SC should join the Soft Contour spring group.");
            AssertTrue(rig.leftArm.IsValid && rig.leftArm.tip == leftHand, "Left arm must bind to l_hand.");
            AssertTrue(rig.rightArm.IsValid && rig.rightArm.tip == rightHand, "Right arm must bind to r_hand.");
            AssertTrue(rig.leftArm2.IsValid && rig.leftArm2.tip == leftHand2, "Second left arm must bind to l_hand_2.");
            AssertTrue(rig.rightArm2.IsValid && rig.rightArm2.tip == rightHand2, "Second right arm must bind to r_hand_2.");
            AssertFalse(rig.leftLeg.IsValid || rig.rightLeg.IsValid, "Absent feet must stay unbound.");
            AssertEqual(4, ReanimatorPrefabCooker.ConfigureReaniIK(rigs, bones), "Repeated setup should resolve the same chains.");
            AssertEqual(4, rigs.childCount, "Repeated setup must reuse IK targets.");

        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Transform CreateCookFixtureBone(Transform parent, string name, Vector3 localPosition)
    {
        var bone = new GameObject(name).transform;
        bone.SetParent(parent, false);
        bone.localPosition = localPosition;
        return bone;
    }

    private static void SpriteSkinPrefabBoneAssignerResolvesSpriteBonesByName()
    {
        var root = new GameObject("PrefabRoot");
        try
        {
            var scopedRoot = new GameObject("bone_52").transform;
            scopedRoot.SetParent(root.transform, false);
            var child = new GameObject("bone_53").transform;
            child.SetParent(scopedRoot, false);
            var outsideDuplicate = new GameObject("bone_53").transform;
            outsideDuplicate.SetParent(root.transform, false);

            var scopedLookup = SpriteSkinPrefabBoneAssignerWindow.BuildLookup(scopedRoot.GetComponentsInChildren<Transform>(true));
            var prefabLookup = SpriteSkinPrefabBoneAssignerWindow.BuildLookup(root.GetComponentsInChildren<Transform>(true));
            var spriteBones = new[]
            {
                new SpriteBone { name = "bone_52", parentId = -1 },
                new SpriteBone { name = "bone_53", parentId = 0 },
                new SpriteBone { name = "missing", parentId = 0 }
            };

            var resolved = SpriteSkinPrefabBoneAssignerWindow.ResolveBoneTransforms(
                spriteBones,
                scopedLookup,
                prefabLookup,
                out var assigned,
                out var missing);

            AssertEqual(2, assigned, "Assigner should resolve matching sprite bones.");
            AssertEqual(1, missing, "Assigner should count missing sprite bones.");
            AssertTrue(ReferenceEquals(scopedRoot, resolved[0]), "Assigner should resolve root bone by name.");
            AssertTrue(ReferenceEquals(child, resolved[1]), "Assigner should prefer scoped child over duplicate outside root.");
            AssertTrue(resolved[2] == null, "Assigner should leave missing bones unassigned.");
            AssertTrue(
                ReferenceEquals(scopedRoot, SpriteSkinPrefabBoneAssignerWindow.ResolveRootBone(spriteBones, resolved)),
                "Assigner should use the sprite bone with parentId -1 as Root Bone.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerTargetsSelectedObjectOnly()
    {
        var root = new GameObject("PrefabRoot");
        try
        {
            var selected = new GameObject("SelectedWithoutSpriteSkin");
            selected.transform.SetParent(root.transform, false);
            var child = new GameObject("ChildWithSpriteSkin");
            child.transform.SetParent(selected.transform, false);
            child.AddComponent<SpriteSkin>();

            AssertFalse(
                SpriteSkinPrefabBoneAssignerWindow.TryGetTargetSpriteSkin(root, selected, out _, out var selectedError),
                "Assigner should not use SpriteSkin components from selected object's children.");
            AssertEqual("Selected GameObject has no SpriteSkin.", selectedError, "Selected parent error.");

            AssertTrue(
                SpriteSkinPrefabBoneAssignerWindow.TryGetTargetSpriteSkin(root, child, out var spriteSkin, out _),
                "Assigner should accept the exact selected GameObject when it has SpriteSkin.");
            AssertTrue(ReferenceEquals(child.GetComponent<SpriteSkin>(), spriteSkin), "Assigner should return selected object's SpriteSkin.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerAssignsMultiSelectionAndAddsMissingSkins()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            var bone = new GameObject("bone_0");
            bone.transform.SetParent(root.transform, false);

            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 1f);
            sprite.SetBones(new[]
            {
                new SpriteBone
                {
                    name = "bone_0",
                    parentId = -1,
                    position = Vector3.zero,
                    rotation = Quaternion.identity,
                    length = 1f
                }
            });

            var needsSkin = new GameObject("NeedsSkin");
            needsSkin.transform.SetParent(root.transform, false);
            needsSkin.AddComponent<SpriteRenderer>().sprite = sprite;

            var existingSkinObject = new GameObject("ExistingSkin");
            existingSkinObject.transform.SetParent(root.transform, false);
            existingSkinObject.AddComponent<SpriteRenderer>().sprite = sprite;
            var existingSkin = existingSkinObject.AddComponent<SpriteSkin>();

            var noRenderer = new GameObject("NoRenderer");
            noRenderer.transform.SetParent(root.transform, false);

            var stats = SpriteSkinPrefabBoneAssignerWindow.AssignBonesOnSelectedSpriteRenderers(
                root,
                new[] { needsSkin, existingSkinObject, noRenderer });

            AssertEqual(2, stats.SpriteSkins, "Multi-selection assign should process selected GameObjects with SpriteRenderer.");
            AssertEqual(1, stats.AddedSpriteSkins, "Multi-selection assign should add SpriteSkin to selected SpriteRenderers missing it.");
            AssertEqual(2, stats.ChangedSpriteSkins, "Multi-selection assign should apply bone references to each processed SpriteSkin.");
            AssertEqual(2, stats.AssignedBones, "Multi-selection assign should assign bones on every processed SpriteSkin.");
            AssertEqual(0, stats.MissingBones, "Multi-selection assign should not report missing bones when prefab bones exist.");
            AssertTrue(needsSkin.GetComponent<SpriteSkin>() != null, "Selected SpriteRenderer without SpriteSkin should receive one.");
            AssertTrue(ReferenceEquals(existingSkin, existingSkinObject.GetComponent<SpriteSkin>()), "Existing SpriteSkin should be reused.");
            AssertTrue(noRenderer.GetComponent<SpriteSkin>() == null, "Selected GameObject without SpriteRenderer should be ignored.");
        }
        finally
        {
            if (sprite != null)
                UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerFillsMissingSpritesBySelectionAndChildren()
    {
        var root = new GameObject("SelectedRoot");
        var selectedHead = new GameObject("Head");
        var selectedArm = new GameObject("Arm");
        var childLeg = new GameObject("Leg");
        var childFrontHair = new GameObject("front_hair");
        var childLeftLegBottom = new GameObject("Left Leg Bottom");
        var childFilled = new GameObject("Filled");
        Texture2D texture = null;
        Sprite head = null;
        Sprite arm = null;
        Sprite leg = null;
        Sprite frontHair = null;
        Sprite leftLegBottom = null;
        Sprite filled = null;
        try
        {
            childLeg.transform.SetParent(root.transform, false);
            childFrontHair.transform.SetParent(root.transform, false);
            childLeftLegBottom.transform.SetParent(root.transform, false);
            childFilled.transform.SetParent(root.transform, false);

            texture = new Texture2D(8, 8);
            head = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.zero);
            head.name = "Head";
            arm = Sprite.Create(texture, new Rect(2f, 0f, 2f, 2f), Vector2.zero);
            arm.name = "Arm";
            leg = Sprite.Create(texture, new Rect(4f, 0f, 2f, 2f), Vector2.zero);
            leg.name = "Leg";
            frontHair = Sprite.Create(texture, new Rect(0f, 2f, 2f, 2f), Vector2.zero);
            frontHair.name = "front hair";
            leftLegBottom = Sprite.Create(texture, new Rect(2f, 2f, 2f, 2f), Vector2.zero);
            leftLegBottom.name = "l_leg_bottom";
            filled = Sprite.Create(texture, new Rect(6f, 0f, 2f, 2f), Vector2.zero);
            filled.name = "Filled";

            var headRenderer = selectedHead.AddComponent<SpriteRenderer>();
            var armRenderer = selectedArm.AddComponent<SpriteRenderer>();
            var legRenderer = childLeg.AddComponent<SpriteRenderer>();
            var frontHairRenderer = childFrontHair.AddComponent<SpriteRenderer>();
            var leftLegBottomRenderer = childLeftLegBottom.AddComponent<SpriteRenderer>();
            var filledRenderer = childFilled.AddComponent<SpriteRenderer>();
            filledRenderer.sprite = filled;

            var stats = SpriteSkinPrefabBoneAssignerWindow.FillMissingSpritesFromSprites(
                "Assets/test.psd",
                new[] { head, arm, leg, frontHair, leftLegBottom, filled },
                new[] { selectedHead, selectedArm, root },
                includeChildren: true);

            AssertEqual(6, stats.SpriteRenderers, "Missing sprite fill should inspect selected renderers and children.");
            AssertEqual(5, stats.MissingSprites, "Missing sprite fill should count only empty SpriteRenderers.");
            AssertEqual(5, stats.FilledSprites, "Missing sprite fill should assign all name-matched missing sprites.");
            AssertTrue(ReferenceEquals(head, headRenderer.sprite), "Selected SpriteRenderer should receive matching sprite.");
            AssertTrue(ReferenceEquals(arm, armRenderer.sprite), "Multi-selected SpriteRenderer should receive matching sprite.");
            AssertTrue(ReferenceEquals(leg, legRenderer.sprite), "Child SpriteRenderer should receive matching sprite.");
            AssertTrue(ReferenceEquals(frontHair, frontHairRenderer.sprite), "Normalized underscore name should match sprite name with spaces.");
            AssertTrue(ReferenceEquals(leftLegBottom, leftLegBottomRenderer.sprite), "Left/right prefix should match l_/r_ sprite names.");
            AssertTrue(ReferenceEquals(filled, filledRenderer.sprite), "Existing SpriteRenderer sprite should not be replaced.");
        }
        finally
        {
            if (head != null)
                UnityEngine.Object.DestroyImmediate(head);
            if (arm != null)
                UnityEngine.Object.DestroyImmediate(arm);
            if (leg != null)
                UnityEngine.Object.DestroyImmediate(leg);
            if (frontHair != null)
                UnityEngine.Object.DestroyImmediate(frontHair);
            if (leftLegBottom != null)
                UnityEngine.Object.DestroyImmediate(leftLegBottom);
            if (filled != null)
                UnityEngine.Object.DestroyImmediate(filled);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(selectedHead);
            UnityEngine.Object.DestroyImmediate(selectedArm);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerRefreshPreservesEnabledState()
    {
        var root = new GameObject("SpriteSkin");
        try
        {
            var spriteSkin = root.AddComponent<SpriteSkin>();
            spriteSkin.enabled = true;

            SpriteSkinPrefabBoneAssignerWindow.RefreshSpriteSkin(spriteSkin);

            AssertTrue(spriteSkin.enabled, "SpriteSkin refresh should leave enabled components enabled.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerRequiresSelectedSpriteRenderer()
    {
        var selected = new GameObject("Selected");
        try
        {
            AssertFalse(
                SpriteSkinPrefabBoneAssignerWindow.CanRecreateSkeletonForSelection(null),
                "Recreate Skeleton button should be disabled with no selected GameObject.");
            AssertFalse(
                SpriteSkinPrefabBoneAssignerWindow.CanRecreateSkeletonForSelection(selected),
                "Recreate Skeleton button should be disabled when selected GameObject has no SpriteRenderer.");

            selected.AddComponent<SpriteRenderer>();
            AssertTrue(
                SpriteSkinPrefabBoneAssignerWindow.CanRecreateSkeletonForSelection(selected),
                "Recreate Skeleton button should be enabled for a selected GameObject with SpriteRenderer.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(selected);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerRecreatesSkeleton()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            var skinObject = new GameObject("Skin");
            skinObject.transform.SetParent(root.transform, false);
            var spriteRenderer = skinObject.AddComponent<SpriteRenderer>();

            texture = new Texture2D(8, 8);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 1f);
            sprite.SetBones(new[]
            {
                new SpriteBone
                {
                    name = "root_bone",
                    parentId = -1,
                    position = new Vector3(1f, 2f, 0f),
                    rotation = Quaternion.identity,
                    length = 4f
                },
                new SpriteBone
                {
                    name = "child_bone",
                    parentId = 0,
                    position = new Vector3(3f, 0f, 0f),
                    rotation = Quaternion.AngleAxis(25f, Vector3.forward),
                    length = 2f
                }
            });
            spriteRenderer.sprite = sprite;

            var spriteSkin = skinObject.AddComponent<SpriteSkin>();
            var oldRoot = new GameObject("old_root").transform;
            oldRoot.SetParent(skinObject.transform, false);
            SetSpriteSkinBones(spriteSkin, oldRoot, new[] { oldRoot });

            var psdBones = new[]
            {
                new SpriteBone
                {
                    name = "root_bone",
                    parentId = -1,
                    position = new Vector3(1f, 2f, 0f),
                    rotation = Quaternion.identity,
                    length = 4f
                },
                new SpriteBone
                {
                    name = "child_bone",
                    parentId = 0,
                    position = new Vector3(3f, 0f, 0f),
                    rotation = Quaternion.AngleAxis(25f, Vector3.forward),
                    length = 2f
                },
                new SpriteBone
                {
                    name = "unused_psd_bone",
                    parentId = 0,
                    position = new Vector3(0f, 5f, 0f),
                    rotation = Quaternion.identity,
                    length = 3f
                }
            };

            var stats = SpriteSkinPrefabBoneAssignerWindow.RecreateSkeletonFromBones(root, spriteRenderer, psdBones);

            AssertEqual(1, stats.ChangedSpriteSkins, "Recreate skeleton should mark the selected SpriteSkin changed.");
            AssertEqual(1, stats.DeletedBones, "Recreate skeleton should delete the old selected skeleton root.");
            AssertEqual(3, stats.RecreatedBones, "Recreate skeleton should create every PSD bone, not only bones used by the selected sprite.");
            AssertTrue(oldRoot == null, "Old skeleton root should be destroyed.");

            var serialized = new SerializedObject(spriteSkin);
            var rootBone = serialized.FindProperty("m_RootBone").objectReferenceValue as Transform;
            var bones = serialized.FindProperty("m_BoneTransforms");
            AssertTrue(rootBone != null, "Recreated SpriteSkin should have a Root Bone.");
            AssertEqual(2, bones.arraySize, "Recreated SpriteSkin should bind only the selected sprite bones.");

            var createdRoot = bones.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
            var child = bones.GetArrayElementAtIndex(1).objectReferenceValue as Transform;
            var unused = createdRoot != null ? createdRoot.Find("unused_psd_bone") : null;
            AssertTrue(createdRoot != null, "Root sprite bone should be assigned.");
            AssertTrue(ReferenceEquals(createdRoot, rootBone), "Root Bone should be the sprite's parentId=-1 bone, matching Unity SpriteSkin.");
            AssertTrue(child != null, "Child sprite bone should be assigned.");
            AssertTrue(unused != null, "Unused PSD bone should still be created from the linked PSD skeleton.");
            AssertTrue(ReferenceEquals(root.transform, createdRoot.parent), "Root sprite bone should be created beside the selected SpriteRenderer.");
            AssertTrue(ReferenceEquals(createdRoot, child.parent), "Child sprite bone should keep the PSD bone hierarchy.");
            AssertTrue(ReferenceEquals(createdRoot, unused.parent), "Unused PSD bone should keep the PSD bone hierarchy.");
            AssertVector(new Vector2(1f, 2f), createdRoot.localPosition, 0.0001f, "Root sprite bone local position.");
            AssertVector(new Vector2(3f, 0f), child.localPosition, 0.0001f, "Child sprite bone local position.");
            AssertVector(new Vector2(0f, 5f), unused.localPosition, 0.0001f, "Unused PSD bone local position.");
        }
        finally
        {
            if (sprite != null)
                UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerUsesSpriteSpecificRootBone()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            var skinObject = new GameObject("Skin");
            skinObject.transform.SetParent(root.transform, false);
            var spriteRenderer = skinObject.AddComponent<SpriteRenderer>();

            texture = new Texture2D(8, 8);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 1f);
            sprite.SetBones(new[]
            {
                new SpriteBone
                {
                    name = "child_bone",
                    parentId = -1,
                    position = new Vector3(3f, 0f, 0f),
                    rotation = Quaternion.AngleAxis(25f, Vector3.forward),
                    length = 2f
                }
            });
            spriteRenderer.sprite = sprite;

            var spriteSkin = skinObject.AddComponent<SpriteSkin>();
            var psdBones = new[]
            {
                new SpriteBone
                {
                    name = "root_bone",
                    parentId = -1,
                    position = new Vector3(1f, 2f, 0f),
                    rotation = Quaternion.identity,
                    length = 4f
                },
                new SpriteBone
                {
                    name = "child_bone",
                    parentId = 0,
                    position = new Vector3(3f, 0f, 0f),
                    rotation = Quaternion.AngleAxis(25f, Vector3.forward),
                    length = 2f
                }
            };

            var stats = SpriteSkinPrefabBoneAssignerWindow.RecreateSkeletonFromBones(root, spriteRenderer, psdBones);

            AssertEqual(1, stats.ChangedSpriteSkins, "Recreate skeleton should bind the selected SpriteSkin.");
            var serialized = new SerializedObject(spriteSkin);
            var rootBone = serialized.FindProperty("m_RootBone").objectReferenceValue as Transform;
            var bones = serialized.FindProperty("m_BoneTransforms");
            var child = bones.GetArrayElementAtIndex(0).objectReferenceValue as Transform;

            AssertTrue(child != null, "Selected sprite bone should resolve to the recreated full PSD skeleton.");
            AssertTrue(ReferenceEquals(child, rootBone), "SpriteSkin Root Bone should be the sprite's parentId=-1 bone even when the full PSD hierarchy has an outer parent.");
            AssertEqual("root_bone", child.parent != null ? child.parent.name : null, "Recreated Transform should still keep the full PSD hierarchy.");
        }
        finally
        {
            if (sprite != null)
                UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerScalesPsdBonesByPixelsPerUnit()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            var skinObject = new GameObject("Skin");
            skinObject.transform.SetParent(root.transform, false);
            var spriteRenderer = skinObject.AddComponent<SpriteRenderer>();
            skinObject.AddComponent<SpriteSkin>();

            texture = new Texture2D(8, 8);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 100f);
            sprite.SetBones(new[]
            {
                new SpriteBone
                {
                    name = "root_bone",
                    parentId = -1,
                    position = new Vector3(1f, 0f, 0f),
                    rotation = Quaternion.identity,
                    length = 1f
                }
            });
            spriteRenderer.sprite = sprite;

            var psdBones = new[]
            {
                new SpriteBone
                {
                    name = "root_bone",
                    parentId = -1,
                    position = new Vector3(100f, 200f, 0f),
                    rotation = Quaternion.identity,
                    length = 100f
                }
            };

            SpriteSkinPrefabBoneAssignerWindow.RecreateSkeletonFromBones(root, spriteRenderer, psdBones);

            var spriteSkin = skinObject.GetComponent<SpriteSkin>();
            var bones = new SerializedObject(spriteSkin).FindProperty("m_BoneTransforms");
            var createdRoot = bones.GetArrayElementAtIndex(0).objectReferenceValue as Transform;

            AssertTrue(createdRoot != null, "Recreated PSD bone should be assigned.");
            AssertVector(new Vector2(1f, 2f), createdRoot.localPosition, 0.0001f, "Importer PSD bone positions should be converted from pixels to Unity units.");
        }
        finally
        {
            if (sprite != null)
                UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerRebindsAllSpriteSkinsToRecreatedSkeleton()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite selectedSprite = null;
        Sprite otherSprite = null;
        try
        {
            var selectedObject = new GameObject("SelectedSkin");
            selectedObject.transform.SetParent(root.transform, false);
            var selectedRenderer = selectedObject.AddComponent<SpriteRenderer>();
            var selectedSkin = selectedObject.AddComponent<SpriteSkin>();

            var otherObject = new GameObject("OtherSkin");
            otherObject.transform.SetParent(root.transform, false);
            var otherRenderer = otherObject.AddComponent<SpriteRenderer>();
            var otherSkin = otherObject.AddComponent<SpriteSkin>();

            texture = new Texture2D(8, 8);
            selectedSprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 1f);
            selectedSprite.SetBones(new[]
            {
                new SpriteBone
                {
                    name = "child_bone",
                    parentId = -1,
                    position = new Vector3(3f, 0f, 0f),
                    rotation = Quaternion.identity,
                    length = 2f
                }
            });
            selectedRenderer.sprite = selectedSprite;

            otherSprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 1f);
            otherSprite.SetBones(new[]
            {
                new SpriteBone
                {
                    name = "unused_psd_bone",
                    parentId = -1,
                    position = new Vector3(0f, 5f, 0f),
                    rotation = Quaternion.identity,
                    length = 3f
                }
            });
            otherRenderer.sprite = otherSprite;

            var oldSelectedRoot = new GameObject("old_selected_root").transform;
            oldSelectedRoot.SetParent(root.transform, false);
            SetSpriteSkinBones(selectedSkin, root.transform, new[] { oldSelectedRoot });

            var oldOtherRoot = new GameObject("old_other_root").transform;
            oldOtherRoot.SetParent(root.transform, false);
            SetSpriteSkinBones(otherSkin, root.transform, new[] { oldOtherRoot });

            var psdBones = new[]
            {
                new SpriteBone
                {
                    name = "root_bone",
                    parentId = -1,
                    position = new Vector3(1f, 2f, 0f),
                    rotation = Quaternion.identity,
                    length = 4f
                },
                new SpriteBone
                {
                    name = "child_bone",
                    parentId = 0,
                    position = new Vector3(3f, 0f, 0f),
                    rotation = Quaternion.identity,
                    length = 2f
                },
                new SpriteBone
                {
                    name = "unused_psd_bone",
                    parentId = 0,
                    position = new Vector3(0f, 5f, 0f),
                    rotation = Quaternion.identity,
                    length = 3f
                }
            };

            var stats = SpriteSkinPrefabBoneAssignerWindow.RecreateSkeletonFromBones(root, selectedRenderer, psdBones);

            AssertEqual(2, stats.SpriteSkins, "Recreate skeleton should visit every SpriteSkin under the hierarchy root.");
            AssertEqual(2, stats.ChangedSpriteSkins, "Recreate skeleton should bind every SpriteSkin to the recreated full PSD skeleton.");
            AssertEqual(2, stats.DeletedBones, "Recreate skeleton should delete old skeleton roots from every SpriteSkin.");
            AssertTrue(oldSelectedRoot == null, "Recreate skeleton should delete the selected SpriteSkin old root.");
            AssertTrue(oldOtherRoot == null, "Recreate skeleton should delete the other SpriteSkin old root.");

            var selectedRoot = new SerializedObject(selectedSkin).FindProperty("m_RootBone").objectReferenceValue as Transform;
            var otherSerialized = new SerializedObject(otherSkin);
            var otherRoot = otherSerialized.FindProperty("m_RootBone").objectReferenceValue as Transform;
            var otherBones = otherSerialized.FindProperty("m_BoneTransforms");
            var otherBone = otherBones.GetArrayElementAtIndex(0).objectReferenceValue as Transform;

            var selectedBone = new SerializedObject(selectedSkin).FindProperty("m_BoneTransforms").GetArrayElementAtIndex(0).objectReferenceValue as Transform;
            AssertTrue(ReferenceEquals(selectedBone, selectedRoot), "Selected SpriteSkin should use its parentId=-1 sprite bone as Root Bone.");
            AssertTrue(ReferenceEquals(otherBone, otherRoot), "Other SpriteSkin should use its parentId=-1 sprite bone as Root Bone.");
            AssertTrue(otherBone != null, "Other SpriteSkin bone transform should come from the recreated skeleton.");
            AssertEqual("root_bone", otherBone != null && otherBone.parent != null ? otherBone.parent.name : null, "Other SpriteSkin should keep the recreated PSD hierarchy.");
        }
        finally
        {
            if (selectedSprite != null)
                UnityEngine.Object.DestroyImmediate(selectedSprite);
            if (otherSprite != null)
                UnityEngine.Object.DestroyImmediate(otherSprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSkinPrefabBoneAssignerReportsMissingSpriteOrBones()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            var spriteRenderer = root.AddComponent<SpriteRenderer>();
            var noSprite = SpriteSkinPrefabBoneAssignerWindow.RecreateSkeleton(root, spriteRenderer);
            AssertEqual("Selected SpriteRenderer has no sprite.", noSprite.ToStatus(), "Missing sprite should be reported in the tool window status.");

            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 1f);
            spriteRenderer.sprite = sprite;

            var noBones = SpriteSkinPrefabBoneAssignerWindow.RecreateSkeletonFromBones(root, spriteRenderer, Array.Empty<SpriteBone>());
            AssertEqual("Linked PSD has no bones.", noBones.ToStatus(), "Missing PSD bones should be reported in the tool window status.");
        }
        finally
        {
            if (sprite != null)
                UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void PrefabSpriteIssueScannerGroupsSpriteAndSkinProblems()
    {
        var root = new GameObject("PrefabRoot");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            var missingSpriteObject = new GameObject("MissingSpriteRenderer");
            missingSpriteObject.transform.SetParent(root.transform, false);
            missingSpriteObject.AddComponent<SpriteRenderer>();

            var skinObject = new GameObject("SkinWithBadBones");
            skinObject.transform.SetParent(root.transform, false);
            var skinRenderer = skinObject.AddComponent<SpriteRenderer>();
            skinObject.AddComponent<SpriteSkin>();
            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 1f);
            skinRenderer.sprite = sprite;

            var group = ReanimatorPrefabSpriteIssueWindow.AnalyzePrefabRoot(root, "Assets/Test/Bad.prefab", true);
            AssertEqual("Assets/Test/Bad.prefab", group.AssetPath, "Scanner should preserve prefab path.");
            AssertTrue(
                group.Issues.Any(issue => issue.Kind == PrefabSpriteIssueKind.SpriteRendererMissingSprite),
                "Scanner should report SpriteRenderer without sprite.");
            AssertTrue(
                group.Issues.Any(issue => issue.Kind == PrefabSpriteIssueKind.SpriteSkinSpriteHasNoBones),
                "Scanner should report SpriteSkin sprite without sprite bones.");
            AssertTrue(
                group.Issues.Any(issue => issue.Kind == PrefabSpriteIssueKind.SpriteSkinMissingRootBone),
                "Scanner should report SpriteSkin without root bone.");
        }
        finally
        {
            if (sprite != null)
                UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void PrefabSpriteIssueScannerResolvesOpenedPrefabObjectPaths()
    {
        var root = new GameObject("PrefabRoot");
        try
        {
            var parent = new GameObject("Parent");
            parent.transform.SetParent(root.transform, false);
            var child = new GameObject("ProblemGO");
            child.transform.SetParent(parent.transform, false);

            AssertTrue(
                ReanimatorPrefabSpriteIssueWindow.TryFindTransformByPath(root.transform, "PrefabRoot/Parent/ProblemGO", out var resolved),
                "Scanner should resolve saved object paths inside an opened prefab root.");
            AssertTrue(ReferenceEquals(child.transform, resolved), "Resolved transform should be the problem GameObject.");
            AssertFalse(
                ReanimatorPrefabSpriteIssueWindow.TryFindTransformByPath(root.transform, "PrefabRoot/Parent/Missing", out _),
                "Scanner should not resolve missing object path segments.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SpriteSheetRepairToolsExposeFirstTabFixers()
    {
        var labels = Reanimator.GetSpriteSheetRepairButtonLabels();
        AssertArray(
            new[]
            {
                "Repair PSD Params",
                "Generate Missing Geometry",
                "Fix Skin Weights",
                "Refresh Cache"
            },
            labels,
            "Sprite Sheet repair buttons");
    }

    private static void MissingGeometryScanAlertLightsOnceAndClearsOnClick()
    {
        var state = new ReanimatorMissingGeometryScanState();
        const string path = "assets/character.psd";
        const string otherPath = "assets/other.psd";

        AssertTrue(state.ShouldScheduleScan(path), "First open should schedule missing geometry scan.");
        state.MarkScheduled(path);
        AssertFalse(state.ShouldScheduleScan(path), "Scheduled path should not schedule twice.");
        AssertTrue(state.IsScheduledPath(path), "Scheduled path should be tracked.");
        state.ClearScheduledPath(path);
        AssertFalse(state.IsScheduledPath(path), "Clearing scheduled path should reset pending scan.");

        state.RecordScanResult(path, true);
        AssertTrue(state.HasAlertForPath(path), "Missing geometry scan should light the current asset button.");
        AssertFalse(state.HasAlertForPath(otherPath), "Missing geometry alert should be asset-scoped.");
        AssertFalse(state.ShouldScheduleScan(path), "Scanned asset should not be rescanned after alert.");

        state.ClearAlert(path);
        AssertFalse(state.HasAlertForPath(path), "Clicking Generate Missing Geometry should clear the alert.");
        AssertFalse(state.ShouldScheduleScan(path), "Click-cleared asset should remain scanned for this opening.");
        AssertTrue(state.ShouldScheduleScan(otherPath), "A different asset should still get its first-open scan.");
    }

    private static void MissingGeometryScanUsesGenerationCriteria()
    {
        AssertTrue(
            PsdLayerPreviewRenderer.ShouldGenerateMissingGeometry(0, Array.Empty<int>(), 0),
            "No geometry should be reported as missing.");
        AssertTrue(
            PsdLayerPreviewRenderer.ShouldGenerateMissingGeometry(7, new[] { 0, 1, 2 }, 3),
            "Low vertex count should be regenerated.");
        AssertTrue(
            PsdLayerPreviewRenderer.ShouldGenerateMissingGeometry(8, new[] { 0, 1, 99 }, 3),
            "Invalid triangles should be regenerated.");
        AssertTrue(
            PsdLayerPreviewRenderer.ShouldGenerateMissingGeometry(8, new[] { 0, 1, 2 }, 2),
            "Missing edge data should be regenerated.");
        AssertFalse(
            PsdLayerPreviewRenderer.ShouldGenerateMissingGeometry(8, new[] { 0, 1, 2 }, 3),
            "Valid geometry should not light the missing geometry alert.");
    }

    private static void SpriteSheetMissingGeometryTintRequiresSelection()
    {
        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldTintMissingGeometryForSelection("body", string.Empty, false),
            "Sprite Sheet overview should not tint every layer red when no sprite is selected.");
        AssertTrue(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldTintMissingGeometryForSelection("body", "body", false),
            "Selected missing-geometry sprite should still get the warning tint.");
        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldTintMissingGeometryForSelection("body", "body", true),
            "Valid selected geometry should not get the warning tint.");
        AssertFalse(
            ReanimatorSpriteSheetGeometryAlgorithms.ShouldTintMissingGeometryForSelection("body", "head", false),
            "Unselected missing-geometry sprites should not be tinted.");
    }

    private static void SetSpriteSkinBones(SpriteSkin spriteSkin, Transform rootBone, Transform[] bones)
    {
        var serialized = new SerializedObject(spriteSkin);
        serialized.FindProperty("m_RootBone").objectReferenceValue = rootBone;
        var boneTransforms = serialized.FindProperty("m_BoneTransforms");
        boneTransforms.arraySize = bones != null ? bones.Length : 0;
        for (var i = 0; i < boneTransforms.arraySize; i++)
            boneTransforms.GetArrayElementAtIndex(i).objectReferenceValue = bones[i];
        serialized.ApplyModifiedProperties();
    }
}
#endif
