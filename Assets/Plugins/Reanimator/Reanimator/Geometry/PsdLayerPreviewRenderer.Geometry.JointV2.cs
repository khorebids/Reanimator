#if UNITY_EDITOR
using System.Collections.Generic;

public partial class PsdLayerPreviewRenderer
{
    public bool TryGetJointV2CurrentBoneSprite(out string spriteName)
    {
        spriteName = string.Empty;
        if (!TryResolveJointV2CurrentBoneLayer(out var currentData) || currentData == null || currentData.layer == null)
            return false;

        spriteName = currentData.layer.name ?? string.Empty;
        return !string.IsNullOrWhiteSpace(spriteName);
    }

    public bool TryGetJointV2BodySprite(out string spriteName, out bool namedBodyFound)
    {
        spriteName = string.Empty;
        namedBodyFound = false;
        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
            return false;

        var parentRendererBone = bones[selectedBoneIndex].parentId;
        if (parentRendererBone < 0 || parentRendererBone >= bones.Count || bones[parentRendererBone] == null)
            return false;

        if (!TryResolveJointV2BodyLayer(parentRendererBone, out var bodyData) || bodyData == null || bodyData.layer == null)
            return false;

        spriteName = bodyData.layer.name ?? string.Empty;
        namedBodyFound = string.Equals(
            NormalizeAutoWeightName(spriteName),
            "body",
            System.StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public bool TryGetJointV2SpecialConnection(float specialJointDistance, out bool isSpecial)
    {
        var contourPoints = new List<UnityEngine.Vector2>(16);
        var resolved = TryGetJointV2AttachedGuidePoints(
            specialJointDistance,
            out _,
            out _,
            out _,
            out isSpecial,
            contourPoints);
        return resolved && contourPoints.Count == 16;
    }

    private bool TryResolveJointV2BodyLayer(int parentRendererBone, out LayerVertexData bodyData)
    {
        bodyData = null;
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return false;

        var candidates = new List<LayerVertexData>();
        var normalizedNames = new List<string>();
        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            if (candidate == null || candidate.layer == null || !candidate.layer.visible)
                continue;

            var spriteBoneIndex = FindSpriteBoneIndexForRendererBone(candidate, parentRendererBone);
            if (spriteBoneIndex < 0 || !JointV2LayerUsesSpriteBone(candidate, spriteBoneIndex))
                continue;

            candidates.Add(candidate);
            normalizedNames.Add(NormalizeAutoWeightName(candidate.layer.name));
        }

        var preferredIndex = ReanimatorJointGeometryAlgorithms.SelectPreferredParentSpriteIndex(normalizedNames);
        if (preferredIndex < 0 || preferredIndex >= candidates.Count)
            return false;

        bodyData = candidates[preferredIndex];
        return bodyData != null;
    }

    private bool TryResolveJointV2CurrentBoneLayer(out LayerVertexData currentData)
    {
        currentData = null;
        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
            return false;
        if (vertexDataByLayerSelectionKey == null || vertexDataByLayerSelectionKey.Count == 0)
            return false;

        LayerVertexData parentData = null;
        var parentRendererBone = bones[selectedBoneIndex].parentId;
        if (parentRendererBone >= 0 && parentRendererBone < bones.Count)
            TryResolveJointV2BodyLayer(parentRendererBone, out parentData);

        foreach (var pair in vertexDataByLayerSelectionKey)
        {
            var candidate = pair.Value;
            if (candidate == null || candidate.layer == null || !candidate.layer.visible)
                continue;
            if (IsSameJointV2LayerCandidate(candidate, parentData))
                continue;

            if (!JointV2LayerUsesRendererBone(candidate, selectedBoneIndex))
                continue;

            if (string.Equals(pair.Key, selectedLayerSelectionKey, System.StringComparison.OrdinalIgnoreCase))
            {
                currentData = candidate;
                return true;
            }

            if (currentData == null)
                currentData = candidate;
        }

        return currentData != null;
    }

    private static bool IsSameJointV2LayerCandidate(LayerVertexData left, LayerVertexData right)
    {
        if (left == null || right == null)
            return false;
        if (ReferenceEquals(left, right) || ReferenceEquals(left.layer, right.layer))
            return true;
        if (left.layer == null || right.layer == null)
            return false;

        return left.layer.layerId == right.layer.layerId &&
               string.Equals(left.layer.name, right.layer.name, System.StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetJointV2PoseIntersectionPair(
        float poseAngleDegrees,
        out UnityEngine.Vector2 pointA,
        out UnityEngine.Vector2 pointB)
    {
        pointA = UnityEngine.Vector2.zero;
        pointB = UnityEngine.Vector2.zero;
        if (!TryResolveJointV2CurrentBoneLayer(out var currentData) ||
            currentData == null || currentData.vertices == null || currentData.vertices.Length < 3)
        {
            return false;
        }

        var selectedBone = bones[selectedBoneIndex];
        var parentRendererBone = selectedBone.parentId;
        if (parentRendererBone < 0 || parentRendererBone >= bones.Count ||
            !TryResolveJointV2BodyLayer(parentRendererBone, out var bodyData) ||
            bodyData == null || bodyData.vertices == null || bodyData.vertices.Length < 3)
        {
            return false;
        }

        var currentBoundary = ResolveBoundaryPairsForJointIntersections(currentData);
        var bodyBoundary = ResolveBoundaryPairsForJointIntersections(bodyData);
        if (currentBoundary == null || currentBoundary.Length < 6 || bodyBoundary == null || bodyBoundary.Length < 6)
            return false;

        var currentDrawVertices = GetLayerVerticesForDraw(currentData);
        var bodyDrawVertices = GetLayerVerticesForDraw(bodyData);
        if (currentDrawVertices == null || currentDrawVertices.Length != currentData.vertices.Length ||
            bodyDrawVertices == null || bodyDrawVertices.Length != bodyData.vertices.Length)
        {
            return false;
        }

        var currentRelativeAngle = ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
            selectedBone.initialRotation,
            selectedBone.rotation);
        var poseDeltaDegrees = ReanimatorJointGeometryAlgorithms.GetSamplePoseDeltaDegrees(
            currentRelativeAngle,
            poseAngleDegrees);
        var pivot = GetBoneBasePoint(selectedBone);
        var rotatedVertices = new UnityEngine.Vector2[currentDrawVertices.Length];
        for (var vertexIndex = 0; vertexIndex < currentDrawVertices.Length; vertexIndex++)
        {
            rotatedVertices[vertexIndex] = ReanimatorJointGeometryAlgorithms.RotatePointAround(
                currentDrawVertices[vertexIndex],
                pivot,
                poseDeltaDegrees);
        }

        var intersections = new List<UnityEngine.Vector2>();
        CollectAutoJointContourIntersections(
            rotatedVertices,
            currentBoundary,
            bodyDrawVertices,
            bodyBoundary,
            intersections);
        return TryPickAutoJointIntersectionPair(intersections, pivot, out pointA, out pointB);
    }

    private bool TryGetJointV2AttachedBonePoints(
        out UnityEngine.Vector2 pointA,
        out UnityEngine.Vector2 pointB)
    {
        pointA = UnityEngine.Vector2.zero;
        pointB = UnityEngine.Vector2.zero;
        if (selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
            return false;
        if (!TryGetJointV2PoseIntersectionPair(0f, out var bindPointA, out var bindPointB))
            return false;

        var selectedBone = bones[selectedBoneIndex];
        var currentRelativeAngle = ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
            selectedBone.initialRotation,
            selectedBone.rotation);
        var pivot = GetBoneBasePoint(selectedBone);
        pointA = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindPointA,
            pivot,
            currentRelativeAngle);
        pointB = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindPointB,
            pivot,
            currentRelativeAngle);
        return true;
    }

    private bool TryGetJointV2AttachedGuidePoints(
        float specialJointDistance,
        out UnityEngine.Vector2 centerPoint,
        out UnityEngine.Vector2 anchorPointA,
        out UnityEngine.Vector2 anchorPointB,
        out bool isSpecial,
        List<UnityEngine.Vector2> contourPoints)
    {
        centerPoint = UnityEngine.Vector2.zero;
        anchorPointA = UnityEngine.Vector2.zero;
        anchorPointB = UnityEngine.Vector2.zero;
        isSpecial = false;
        contourPoints?.Clear();
        if (contourPoints == null ||
            selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null ||
            !TryGetJointV2PoseIntersectionPair(0f, out var bindPointA, out var bindPointB))
        {
            return false;
        }

        var selectedBone = bones[selectedBoneIndex];
        var currentRelativeAngle = ReanimatorJointGeometryAlgorithms.GetRelativeBoneAngleDegrees(
            selectedBone.initialRotation,
            selectedBone.rotation);
        var pivot = GetBoneBasePoint(selectedBone);
        var bindCenterPoint = (bindPointA + bindPointB) * 0.5f;
        centerPoint = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindCenterPoint,
            pivot,
            currentRelativeAngle);
        anchorPointA = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindPointA,
            pivot,
            currentRelativeAngle);
        anchorPointB = ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindPointB,
            pivot,
            currentRelativeAngle);

        var directionA = bindPointA - bindCenterPoint;
        var directionB = bindPointB - bindCenterPoint;
        if (directionA.sqrMagnitude <= 1e-8f || directionB.sqrMagnitude <= 1e-8f)
            return true;

        if (!TryResolveJointV2CurrentBoneLayer(out var currentData) || currentData == null)
            return true;

        var currentDrawVertices = GetLayerVerticesForDraw(currentData);
        var currentBoundary = ResolveBoundaryPairsForJointIntersections(currentData);
        if (currentDrawVertices == null || currentBoundary == null)
            return true;

        var bindVertices = new UnityEngine.Vector2[currentDrawVertices.Length];
        var poseDeltaDegrees = ReanimatorJointGeometryAlgorithms.GetSamplePoseDeltaDegrees(
            currentRelativeAngle,
            0f);
        for (var vertexIndex = 0; vertexIndex < currentDrawVertices.Length; vertexIndex++)
        {
            bindVertices[vertexIndex] = ReanimatorJointGeometryAlgorithms.RotatePointAround(
                currentDrawVertices[vertexIndex],
                pivot,
                poseDeltaDegrees);
        }

        if (!TryBuildBoundaryLoopFromPairs(currentBoundary, bindVertices.Length, out var boundaryLoop) ||
            boundaryLoop == null || boundaryLoop.Count < 3)
        {
            return true;
        }

        var loopPoints = new List<UnityEngine.Vector2>(boundaryLoop.Count);
        var currentLoopPoints = new List<UnityEngine.Vector2>(boundaryLoop.Count);
        for (var loopIndex = 0; loopIndex < boundaryLoop.Count; loopIndex++)
        {
            var vertexIndex = boundaryLoop[loopIndex];
            if (vertexIndex < 0 || vertexIndex >= bindVertices.Length)
                continue;
            loopPoints.Add(bindVertices[vertexIndex]);
            currentLoopPoints.Add(currentDrawVertices[vertexIndex]);
        }

        if (!TryBuildContourPerimeterSamples(loopPoints, out var segmentLengths, out var perimeterLength))
            return true;

        var guideBindPointA = bindPointA;
        var guideBindPointB = bindPointB;
        if (TryClassifyJointV2Connection(
                centerPoint,
                anchorPointA,
                anchorPointB,
                currentLoopPoints,
                out isSpecial,
                out var directionToStatic) &&
            isSpecial)
        {
            var bindDirectionToStatic = RotateJointV2Direction(
                directionToStatic,
                -currentRelativeAngle);
            var hasSpecialAnchorA = TryBuildJointV2SpecialAnchor(
                bindCenterPoint,
                bindPointA,
                bindDirectionToStatic,
                specialJointDistance,
                loopPoints,
                segmentLengths,
                perimeterLength,
                out var specialBindPointA);
            var hasSpecialAnchorB = TryBuildJointV2SpecialAnchor(
                bindCenterPoint,
                bindPointB,
                bindDirectionToStatic,
                specialJointDistance,
                loopPoints,
                segmentLengths,
                perimeterLength,
                out var specialBindPointB);
            if (!hasSpecialAnchorA || !hasSpecialAnchorB)
            {
                // A special connection must never silently fall back to the
                // original orange anchors. No green pair means no magenta chains.
                contourPoints.Clear();
                return false;
            }

            guideBindPointA = specialBindPointA;
            guideBindPointB = specialBindPointB;

            anchorPointA = ReanimatorJointGeometryAlgorithms.RotatePointAround(
                guideBindPointA,
                pivot,
                currentRelativeAngle);
            anchorPointB = ReanimatorJointGeometryAlgorithms.RotatePointAround(
                guideBindPointB,
                pivot,
                currentRelativeAngle);
        }

        if (isSpecial)
        {
            AddJointV2SpecialContourPointsByDistance(
                bindCenterPoint,
                guideBindPointA,
                loopPoints,
                segmentLengths,
                perimeterLength,
                pivot,
                currentRelativeAngle,
                contourPoints);
            AddJointV2SpecialContourPointsByDistance(
                bindCenterPoint,
                guideBindPointB,
                loopPoints,
                segmentLengths,
                perimeterLength,
                pivot,
                currentRelativeAngle,
                contourPoints);
        }
        else
        {
            AddJointV2DiagonalContourPoints(
                bindCenterPoint,
                (guideBindPointA - bindCenterPoint).normalized,
                guideBindPointA,
                bindVertices,
                currentBoundary,
                loopPoints,
                segmentLengths,
                perimeterLength,
                pivot,
                currentRelativeAngle,
                contourPoints);
            AddJointV2DiagonalContourPoints(
                bindCenterPoint,
                (guideBindPointB - bindCenterPoint).normalized,
                guideBindPointB,
                bindVertices,
                currentBoundary,
                loopPoints,
                segmentLengths,
                perimeterLength,
                pivot,
                currentRelativeAngle,
                contourPoints);
        }
        return true;
    }

    private bool TryClassifyJointV2Connection(
        UnityEngine.Vector2 centerPoint,
        UnityEngine.Vector2 orangePointA,
        UnityEngine.Vector2 orangePointB,
        IReadOnlyList<UnityEngine.Vector2> currentContour,
        out bool isSpecial,
        out UnityEngine.Vector2 directionToStatic)
    {
        isSpecial = false;
        directionToStatic = UnityEngine.Vector2.zero;
        if (currentContour == null || currentContour.Count < 3 ||
            selectedBoneIndex < 0 || selectedBoneIndex >= bones.Count || bones[selectedBoneIndex] == null)
        {
            return false;
        }

        var staticBoneIndex = bones[selectedBoneIndex].parentId;
        if (staticBoneIndex < 0 || staticBoneIndex >= bones.Count || bones[staticBoneIndex] == null)
            return false;

        var staticStart = GetBoneBasePoint(bones[staticBoneIndex]);
        var staticEnd = GetBoneEnd(staticBoneIndex);
        var closestStaticPoint = ClosestJointV2PointOnSegment(centerPoint, staticStart, staticEnd);
        directionToStatic = closestStaticPoint - centerPoint;
        if (directionToStatic.sqrMagnitude <= 1e-8f)
            directionToStatic = staticStart - centerPoint;
        if (directionToStatic.sqrMagnitude <= 1e-8f)
            directionToStatic = staticEnd - centerPoint;
        if (directionToStatic.sqrMagnitude <= 1e-8f)
            return false;
        directionToStatic.Normalize();

        var orangeRadius = (UnityEngine.Vector2.Distance(centerPoint, orangePointA) +
                            UnityEngine.Vector2.Distance(centerPoint, orangePointB)) * 0.5f;
        if (orangeRadius <= 1e-5f)
            return false;

        var testPoint = centerPoint + directionToStatic * (orangeRadius * 0.2f);
        isSpecial = !ReanimatorQuadNetAlgorithms.IsPointInsideOrOnPolygon(testPoint, currentContour);
        return true;
    }

    private static bool TryBuildJointV2SpecialAnchor(
        UnityEngine.Vector2 centerPoint,
        UnityEngine.Vector2 orangePoint,
        UnityEngine.Vector2 directionToStatic,
        float normalizedDistance,
        IReadOnlyList<UnityEngine.Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        out UnityEngine.Vector2 greenPoint)
    {
        greenPoint = orangePoint;
        if (directionToStatic.sqrMagnitude <= 1e-8f ||
            loopPoints == null || segmentLengths == null ||
            loopPoints.Count < 3 || segmentLengths.Count != loopPoints.Count ||
            perimeterLength <= 1e-6f ||
            !TryProjectPointToContourPerimeter(
                loopPoints,
                segmentLengths,
                perimeterLength,
                orangePoint,
                out var orangeDistance))
        {
            return false;
        }

        directionToStatic.Normalize();
        var fullOffsetDistance = UnityEngine.Vector2.Distance(centerPoint, orangePoint);
        var contourOffset = fullOffsetDistance * UnityEngine.Mathf.Clamp01(normalizedDistance);
        if (contourOffset <= 1e-6f)
            return true;

        if (!TrySampleContourPointAtDistance(
                loopPoints,
                segmentLengths,
                perimeterLength,
                orangeDistance + contourOffset,
                out var forwardPoint) ||
            !TrySampleContourPointAtDistance(
                loopPoints,
                segmentLengths,
                perimeterLength,
                orangeDistance - contourOffset,
                out var backwardPoint))
        {
            return false;
        }

        var forwardDirection = (forwardPoint - centerPoint).normalized;
        var backwardDirection = (backwardPoint - centerPoint).normalized;
        greenPoint = UnityEngine.Vector2.Dot(forwardDirection, directionToStatic) <=
                     UnityEngine.Vector2.Dot(backwardDirection, directionToStatic)
            ? forwardPoint
            : backwardPoint;
        return true;
    }

    private static UnityEngine.Vector2 ClosestJointV2PointOnSegment(
        UnityEngine.Vector2 point,
        UnityEngine.Vector2 segmentStart,
        UnityEngine.Vector2 segmentEnd)
    {
        var segment = segmentEnd - segmentStart;
        var lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 1e-8f)
            return segmentStart;

        var t = UnityEngine.Mathf.Clamp01(UnityEngine.Vector2.Dot(point - segmentStart, segment) / lengthSquared);
        return segmentStart + segment * t;
    }

    private static void AddJointV2SpecialContourPointsByDistance(
        UnityEngine.Vector2 centerPoint,
        UnityEngine.Vector2 greenPoint,
        IReadOnlyList<UnityEngine.Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        UnityEngine.Vector2 pivot,
        float currentRelativeAngle,
        List<UnityEngine.Vector2> contourPoints)
    {
        if (contourPoints == null || loopPoints == null || segmentLengths == null ||
            loopPoints.Count < 3 || segmentLengths.Count != loopPoints.Count || perimeterLength <= 1e-6f ||
            !TryProjectPointToContourPerimeter(
                loopPoints,
                segmentLengths,
                perimeterLength,
                greenPoint,
                out var greenDistance))
        {
            return;
        }

        var contourOffset = UnityEngine.Vector2.Distance(centerPoint, greenPoint);
        contourOffset = UnityEngine.Mathf.Min(contourOffset, perimeterLength * 0.49f);
        AddJointV2SpecialContourPointByDistance(
            greenPoint,
            greenDistance + contourOffset,
            loopPoints,
            segmentLengths,
            perimeterLength,
            pivot,
            currentRelativeAngle,
            contourPoints);
        AddJointV2SpecialContourPointByDistance(
            greenPoint,
            greenDistance - contourOffset,
            loopPoints,
            segmentLengths,
            perimeterLength,
            pivot,
            currentRelativeAngle,
            contourPoints);
    }

    private static void AddJointV2SpecialContourPointByDistance(
        UnityEngine.Vector2 greenPoint,
        float targetDistance,
        IReadOnlyList<UnityEngine.Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        UnityEngine.Vector2 pivot,
        float currentRelativeAngle,
        List<UnityEngine.Vector2> contourPoints)
    {
        if (!TrySampleContourPointAtDistance(
                loopPoints,
                segmentLengths,
                perimeterLength,
                targetDistance,
                out var bindHit))
        {
            return;
        }

        contourPoints.Add(ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindHit,
            pivot,
            currentRelativeAngle));
        AddJointV2ContourSubdivisionPoints(
            greenPoint,
            bindHit,
            loopPoints,
            segmentLengths,
            perimeterLength,
            pivot,
            currentRelativeAngle,
            contourPoints);
    }

    private static void AddJointV2DiagonalContourPoints(
        UnityEngine.Vector2 centerPoint,
        UnityEngine.Vector2 baseDirection,
        UnityEngine.Vector2 whiteBindPoint,
        IReadOnlyList<UnityEngine.Vector2> currentVertices,
        IReadOnlyList<int> currentBoundary,
        IReadOnlyList<UnityEngine.Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        UnityEngine.Vector2 pivot,
        float currentRelativeAngle,
        List<UnityEngine.Vector2> contourPoints)
    {
        AddJointV2DiagonalContourPoint(
            centerPoint,
            RotateJointV2Direction(baseDirection, 45f),
            whiteBindPoint,
            currentVertices,
            currentBoundary,
            loopPoints,
            segmentLengths,
            perimeterLength,
            pivot,
            currentRelativeAngle,
            contourPoints);
        AddJointV2DiagonalContourPoint(
            centerPoint,
            RotateJointV2Direction(baseDirection, -45f),
            whiteBindPoint,
            currentVertices,
            currentBoundary,
            loopPoints,
            segmentLengths,
            perimeterLength,
            pivot,
            currentRelativeAngle,
            contourPoints);
    }

    private static void AddJointV2DiagonalContourPoint(
        UnityEngine.Vector2 centerPoint,
        UnityEngine.Vector2 direction,
        UnityEngine.Vector2 whiteBindPoint,
        IReadOnlyList<UnityEngine.Vector2> currentVertices,
        IReadOnlyList<int> currentBoundary,
        IReadOnlyList<UnityEngine.Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        UnityEngine.Vector2 pivot,
        float currentRelativeAngle,
        List<UnityEngine.Vector2> contourPoints)
    {
        var angleRad = UnityEngine.Mathf.Atan2(direction.y, direction.x);
        if (!ReanimatorJointGeometryAlgorithms.TryGetNearestContourRayIntersection(
            currentVertices,
            currentBoundary,
            centerPoint,
            angleRad,
            out var bindHit))
        {
            return;
        }

        contourPoints.Add(ReanimatorJointGeometryAlgorithms.RotatePointAround(
            bindHit,
            pivot,
            currentRelativeAngle));
        AddJointV2ContourSubdivisionPoints(
            whiteBindPoint,
            bindHit,
            loopPoints,
            segmentLengths,
            perimeterLength,
            pivot,
            currentRelativeAngle,
            contourPoints);
    }

    private static void AddJointV2ContourSubdivisionPoints(
        UnityEngine.Vector2 whiteBindPoint,
        UnityEngine.Vector2 contourBindPoint,
        IReadOnlyList<UnityEngine.Vector2> loopPoints,
        IReadOnlyList<float> segmentLengths,
        float perimeterLength,
        UnityEngine.Vector2 pivot,
        float currentRelativeAngle,
        List<UnityEngine.Vector2> contourPoints)
    {
        if (!TryProjectPointToContourPerimeter(
                loopPoints,
                segmentLengths,
                perimeterLength,
                whiteBindPoint,
                out var whiteDistance) ||
            !TryProjectPointToContourPerimeter(
                loopPoints,
                segmentLengths,
                perimeterLength,
                contourBindPoint,
                out var contourDistance))
        {
            return;
        }

        var forwardLength = UnityEngine.Mathf.Repeat(contourDistance - whiteDistance, perimeterLength);
        var backwardLength = perimeterLength - forwardLength;
        var signedArcLength = forwardLength <= backwardLength ? forwardLength : -backwardLength;
        for (var divisionIndex = 1; divisionIndex < 4; divisionIndex++)
        {
            var targetDistance = whiteDistance + signedArcLength * (divisionIndex / 4f);
            if (!TrySampleContourPointAtDistance(
                    loopPoints,
                    segmentLengths,
                    perimeterLength,
                    targetDistance,
                    out var bindPoint))
            {
                continue;
            }

            contourPoints.Add(ReanimatorJointGeometryAlgorithms.RotatePointAround(
                bindPoint,
                pivot,
                currentRelativeAngle));
        }
    }

    private static UnityEngine.Vector2 RotateJointV2Direction(
        UnityEngine.Vector2 direction,
        float angleDegrees)
    {
        var angleRad = angleDegrees * UnityEngine.Mathf.Deg2Rad;
        var sin = UnityEngine.Mathf.Sin(angleRad);
        var cos = UnityEngine.Mathf.Cos(angleRad);
        return new UnityEngine.Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos);
    }

    private static bool JointV2LayerUsesSpriteBone(LayerVertexData data, int spriteBoneIndex)
    {
        if (data == null || data.boneWeights == null || spriteBoneIndex < 0)
            return false;

        for (var vertexIndex = 0; vertexIndex < data.boneWeights.Length; vertexIndex++)
        {
            if (GetSpriteBoneWeight(data.boneWeights[vertexIndex], spriteBoneIndex) > 0.0001f)
                return true;
        }

        return false;
    }

    private static bool JointV2LayerUsesRendererBone(LayerVertexData data, int rendererBoneIndex)
    {
        var spriteBoneIndex = FindSpriteBoneIndexForRendererBone(data, rendererBoneIndex);
        return spriteBoneIndex >= 0 && JointV2LayerUsesSpriteBone(data, spriteBoneIndex);
    }
}
#endif
