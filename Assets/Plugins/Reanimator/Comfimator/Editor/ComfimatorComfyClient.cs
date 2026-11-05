#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class ComfimatorComfyClient
{
    public const string DefaultServerUrl = "http://127.0.0.1:8188";
    public const string AutoSkeletonEchoWorkflowPath = @"C:\Comfy\user\default\workflows\reanimator\auto_skeleton_echo_api.json";
    public const string AutoSkeletonControlNetPoseWorkflowPath = @"C:\Comfy\user\default\workflows\reanimator\auto_skeleton_controlnet_pose_api.json";
    private const string DefaultOutputDirectory = @"C:\Comfy\output";
    private const string InputToken = "__REANIMATOR_INPUT__";
    private const string NippleVisionModel = "Qwen3-VL-4B-Instruct-Abliterated";
    private const string NippleVisionPrompt =
        "Analyze the single adult character. Locate each nipple/areola anatomical center. " +
        "If covered, infer the point from the forward-projecting apex of each breast mound. " +
        "Never choose cleavage, breast boundary, garment edge, strap, highlight, shoulder, or rig point. " +
        "Each target must be centered on its own breast volume. Return ONLY one compact JSON object " +
        "in exactly this format: {\"viewer_left\":[x,y],\"viewer_right\":[x,y],\"confidence\":[c1,c2]}. " +
        "x and y are integers from 0 to 1000 measured from the top-left of the full image; " +
        "viewer_left means the nipple on the image-left breast. Confidence values are from 0 to 1. " +
        "Do not describe the image and do not add markdown.";
    private const string BreastBoundsPrompt =
        "Analyze this adult character carefully. Identify the complete soft-tissue silhouette of each breast. " +
        "Return ONLY one compact JSON object in exactly this format: " +
        "{\"viewer_left_breast_bbox\":[x1,y1,x2,y2],\"viewer_right_breast_bbox\":[x1,y1,x2,y2]}. " +
        "Coordinates are integers from 0 to 1000 measured from the top-left of the full image. " +
        "Each box must tightly enclose one entire breast mound, not the shoulder, garment, or torso. " +
        "viewer_left means the breast on the left side of the image. Do not add markdown.";
    private const string BoobContourPrompt =
        "Analyze the visible adult character and segment the complete projected soft-tissue area of each breast. " +
        "The breasts may be fused into one body sprite with no separate breast layers. Follow anatomical volume, " +
        "not clothing edges, straps, highlights, cleavage alone, shoulders, arms, belly, or editor markers. " +
        "Return a clockwise contour of 10 to 16 points around each breast, including its upper, outer, lower, and inner boundary. " +
        "Return ONLY one compact JSON object in exactly this format: " +
        "{\"viewer_left_breast_contour\":[[x,y],...],\"viewer_right_breast_contour\":[[x,y],...],\"confidence\":[c1,c2]}. " +
        "Coordinates are integers from 0 to 1000 measured from the top-left of the full image. " +
        "viewer_left means the breast on the image-left side. Do not add markdown.";

    internal sealed class AutoSkeletonResult
    {
        public AutoSkeletonResult(string outputFileName, string poseJsonFileName, string poseJson)
        {
            OutputFileName = outputFileName ?? string.Empty;
            PoseJsonFileName = poseJsonFileName ?? string.Empty;
            PoseJson = poseJson ?? string.Empty;
        }

        public string OutputFileName { get; }
        public string PoseJsonFileName { get; }
        public string PoseJson { get; }
    }

    internal sealed class NippleVisionResult
    {
        public NippleVisionResult(
            Vector2 viewerLeft,
            Vector2 viewerRight,
            float leftConfidence,
            float rightConfidence,
            string rawResponse,
            Rect? viewerLeftBreastBounds = null,
            Rect? viewerRightBreastBounds = null)
        {
            ViewerLeft = viewerLeft;
            ViewerRight = viewerRight;
            LeftConfidence = Mathf.Clamp01(leftConfidence);
            RightConfidence = Mathf.Clamp01(rightConfidence);
            RawResponse = rawResponse ?? string.Empty;
            HasBreastBounds = viewerLeftBreastBounds.HasValue && viewerRightBreastBounds.HasValue;
            ViewerLeftBreastBounds = viewerLeftBreastBounds ?? default;
            ViewerRightBreastBounds = viewerRightBreastBounds ?? default;
        }

        public Vector2 ViewerLeft { get; }
        public Vector2 ViewerRight { get; }
        public float LeftConfidence { get; }
        public float RightConfidence { get; }
        public string RawResponse { get; }
        public bool HasBreastBounds { get; }
        public Rect ViewerLeftBreastBounds { get; }
        public Rect ViewerRightBreastBounds { get; }
    }

    internal sealed class BoobVisionResult
    {
        public BoobVisionResult(
            Vector2[] viewerLeftContour,
            Vector2[] viewerRightContour,
            float leftConfidence,
            float rightConfidence,
            string rawResponse)
        {
            ViewerLeftContour = viewerLeftContour ?? Array.Empty<Vector2>();
            ViewerRightContour = viewerRightContour ?? Array.Empty<Vector2>();
            LeftConfidence = Mathf.Clamp01(leftConfidence);
            RightConfidence = Mathf.Clamp01(rightConfidence);
            RawResponse = rawResponse ?? string.Empty;
        }

        public Vector2[] ViewerLeftContour { get; }
        public Vector2[] ViewerRightContour { get; }
        public float LeftConfidence { get; }
        public float RightConfidence { get; }
        public string RawResponse { get; }
    }

    public static async Task<string> CheckConnectionAsync(string serverUrl)
    {
        var baseUrl = NormalizeServerUrl(serverUrl);
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.GetAsync(baseUrl + "/system_stats");
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            return string.IsNullOrWhiteSpace(body)
                ? "Connected: " + baseUrl
                : "Connected: " + baseUrl + " (/system_stats ok)";
        }
    }

    public static async Task<AutoSkeletonResult> RunAutoSkeletonEchoAsync(
        string serverUrl,
        string screenshotPath,
        string workflowPath)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            throw new FileNotFoundException("Comfimator screenshot was not created.", screenshotPath);
        if (string.IsNullOrWhiteSpace(workflowPath) || !File.Exists(workflowPath))
            throw new FileNotFoundException("Comfimator workflow was not found.", workflowPath);

        var baseUrl = NormalizeServerUrl(serverUrl);
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            var inputName = await UploadImageAsync(client, baseUrl, screenshotPath);
            var workflow = File.ReadAllText(workflowPath, Encoding.UTF8).Replace(InputToken, EscapeJsonString(inputName));
            var graph = JObject.Parse(workflow);
            var keypointsPrefix = ScopeAutoSkeletonKeypoints(graph);
            var promptId = await QueuePromptAsync(client, baseUrl, graph.ToString(Formatting.None));
            return await WaitForAutoSkeletonOutputAsync(client, baseUrl, promptId, keypointsPrefix);
        }
    }

    public static async Task<NippleVisionResult> RunNippleVisionAsync(string serverUrl, string screenshotPath)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            throw new FileNotFoundException("Comfimator screenshot was not created.", screenshotPath);

        var visibleContentBounds = FindVisibleContentBounds(screenshotPath);
        var baseUrl = NormalizeServerUrl(serverUrl);
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            var inputName = await UploadImageAsync(client, baseUrl, screenshotPath);
            var workflow = BuildNippleVisionWorkflow(inputName, NippleVisionPrompt);
            var promptId = await QueuePromptAsync(client, baseUrl, workflow);
            var response = await WaitForNippleVisionResponseAsync(client, baseUrl, promptId);
            var directResult = ParseNippleVisionResponse(response);

            var boundsWorkflow = BuildNippleVisionWorkflow(inputName, BreastBoundsPrompt);
            var boundsPromptId = await QueuePromptAsync(client, baseUrl, boundsWorkflow);
            var boundsResponse = await WaitForNippleVisionResponseAsync(client, baseUrl, boundsPromptId);
            var refinedResult = RefineNippleVisionWithBreastBounds(directResult, boundsResponse);
            var correctedResult = CorrectVisionPairForVisibleContent(refinedResult, visibleContentBounds);
            return RefineVisibleNippleCentersFromWorkspace(correctedResult, screenshotPath);
        }
    }

    private static NippleVisionResult RefineVisibleNippleCentersFromWorkspace(
        NippleVisionResult seed,
        string screenshotPath)
    {
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(screenshotPath), false))
                return seed;

            var pixels = texture.GetPixels32();
            var pairSpanPixels = Mathf.Abs(seed.ViewerRight.x - seed.ViewerLeft.x) * texture.width;
            var defaultSearchRadius = Mathf.Clamp(
                pairSpanPixels * 0.58f,
                Mathf.Min(texture.width, texture.height) * 0.04f,
                Mathf.Min(texture.width, texture.height) * 0.14f);
            var leftSearchCenter = seed.ViewerLeft;
            var rightSearchCenter = seed.ViewerRight;
            var leftSearchRadius = defaultSearchRadius;
            var rightSearchRadius = defaultSearchRadius;
            if (seed.HasBreastBounds)
            {
                BuildVisibleNippleSearchFromBreastBounds(
                    seed.ViewerLeftBreastBounds,
                    texture.width,
                    texture.height,
                    defaultSearchRadius,
                    out leftSearchCenter,
                    out leftSearchRadius);
                BuildVisibleNippleSearchFromBreastBounds(
                    seed.ViewerRightBreastBounds,
                    texture.width,
                    texture.height,
                    defaultSearchRadius,
                    out rightSearchCenter,
                    out rightSearchRadius);
            }
            var leftCandidates = FindVisibleNippleCandidates(
                pixels,
                texture.width,
                texture.height,
                leftSearchCenter,
                leftSearchRadius);
            var rightCandidates = FindVisibleNippleCandidates(
                pixels,
                texture.width,
                texture.height,
                rightSearchCenter,
                rightSearchRadius);
            if (!TrySelectVisibleNipplePair(
                    leftCandidates,
                    rightCandidates,
                    Mathf.Max(leftSearchRadius, rightSearchRadius),
                    out var left,
                    out var right,
                    out var leftConfidence,
                    out var rightConfidence))
                return seed;

            return new NippleVisionResult(
                left,
                right,
                Mathf.Max(seed.LeftConfidence, leftConfidence),
                Mathf.Max(seed.RightConfidence, rightConfidence),
                seed.RawResponse + " | workspace pixel centers",
                seed.HasBreastBounds ? seed.ViewerLeftBreastBounds : (Rect?)null,
                seed.HasBreastBounds ? seed.ViewerRightBreastBounds : (Rect?)null);
        }
        catch
        {
            return seed;
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void BuildVisibleNippleSearchFromBreastBounds(
        Rect bounds,
        int imageWidth,
        int imageHeight,
        float defaultRadius,
        out Vector2 centerTopLeft,
        out float radiusPixels)
    {
        centerTopLeft = bounds.center;
        var boundsSpanPixels = Mathf.Max(
            bounds.width * imageWidth,
            bounds.height * imageHeight);
        var imageMin = Mathf.Min(imageWidth, imageHeight);
        radiusPixels = Mathf.Clamp(
            Mathf.Max(defaultRadius, boundsSpanPixels * 1.1f),
            imageMin * 0.05f,
            imageMin * 0.18f);
    }

    private readonly struct VisibleNippleCandidate
    {
        public VisibleNippleCandidate(Vector2 pixelPoint, Vector2 topLeftNormalized, float redness, float confidence)
        {
            PixelPoint = pixelPoint;
            TopLeftNormalized = topLeftNormalized;
            Redness = redness;
            Confidence = confidence;
        }

        public Vector2 PixelPoint { get; }
        public Vector2 TopLeftNormalized { get; }
        public float Redness { get; }
        public float Confidence { get; }
    }

    private static List<VisibleNippleCandidate> FindVisibleNippleCandidates(
        Color32[] pixels,
        int width,
        int height,
        Vector2 seedTopLeft,
        float searchRadius)
    {
        var result = new List<VisibleNippleCandidate>();
        if (pixels == null || pixels.Length != width * height || width < 8 || height < 8)
            return result;

        var center = new Vector2(
            Mathf.Clamp01(seedTopLeft.x) * width,
            (1f - Mathf.Clamp01(seedTopLeft.y)) * height);
        var minX = Mathf.Clamp(Mathf.FloorToInt(center.x - searchRadius), 0, width - 1);
        var maxX = Mathf.Clamp(Mathf.CeilToInt(center.x + searchRadius), 0, width - 1);
        var minY = Mathf.Clamp(Mathf.FloorToInt(center.y - searchRadius), 0, height - 1);
        var maxY = Mathf.Clamp(Mathf.CeilToInt(center.y + searchRadius), 0, height - 1);
        var rawPeaks = new List<VisibleNippleCandidate>();
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var point = new Vector2(x, y);
                if ((point - center).sqrMagnitude > searchRadius * searchRadius)
                    continue;
                var pixel = pixels[y * width + x];
                if (pixel.a < 64)
                    continue;
                var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                if (redness < 0.08f)
                    continue;
                rawPeaks.Add(new VisibleNippleCandidate(
                    point,
                    new Vector2(point.x / width, 1f - point.y / height),
                    redness,
                    Mathf.Clamp01((redness - 0.05f) / 0.25f)));
            }
        }

        rawPeaks.Sort((a, b) => b.Redness.CompareTo(a.Redness));
        var suppressionRadius = Mathf.Clamp(searchRadius * 0.14f, 8f, 16f);
        for (var i = 0; i < rawPeaks.Count && result.Count < 16; i++)
        {
            var peak = rawPeaks[i];
            var overlaps = false;
            for (var j = 0; j < result.Count; j++)
            {
                if ((result[j].PixelPoint - peak.PixelPoint).sqrMagnitude <=
                    suppressionRadius * suppressionRadius)
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps)
                continue;

            var clusterRadius = Mathf.Clamp(searchRadius * 0.3f, 8f, 28f);
            var threshold = peak.Redness - 0.03f;
            var weightedPoint = Vector2.zero;
            var totalWeight = 0f;
            var clusterMinX = Mathf.Clamp(Mathf.FloorToInt(peak.PixelPoint.x - clusterRadius), 0, width - 1);
            var clusterMaxX = Mathf.Clamp(Mathf.CeilToInt(peak.PixelPoint.x + clusterRadius), 0, width - 1);
            var clusterMinY = Mathf.Clamp(Mathf.FloorToInt(peak.PixelPoint.y - clusterRadius), 0, height - 1);
            var clusterMaxY = Mathf.Clamp(Mathf.CeilToInt(peak.PixelPoint.y + clusterRadius), 0, height - 1);
            for (var y = clusterMinY; y <= clusterMaxY; y++)
            {
                for (var x = clusterMinX; x <= clusterMaxX; x++)
                {
                    var point = new Vector2(x, y);
                    if ((point - peak.PixelPoint).sqrMagnitude > clusterRadius * clusterRadius)
                        continue;
                    var pixel = pixels[y * width + x];
                    var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                    if (pixel.a < 64 || redness < threshold)
                        continue;
                    var weight = Mathf.Max(0.0001f, redness - threshold);
                    weightedPoint += point * weight;
                    totalWeight += weight;
                }
            }

            var refined = totalWeight > 0.0001f ? weightedPoint / totalWeight : peak.PixelPoint;
            result.Add(new VisibleNippleCandidate(
                refined,
                new Vector2(Mathf.Clamp01(refined.x / width), Mathf.Clamp01(1f - refined.y / height)),
                peak.Redness,
                peak.Confidence));
        }

        return result;
    }

    private static bool TrySelectVisibleNipplePair(
        IReadOnlyList<VisibleNippleCandidate> leftCandidates,
        IReadOnlyList<VisibleNippleCandidate> rightCandidates,
        float searchRadius,
        out Vector2 left,
        out Vector2 right,
        out float leftConfidence,
        out float rightConfidence)
    {
        left = default;
        right = default;
        leftConfidence = 0f;
        rightConfidence = 0f;
        var bestScore = float.MinValue;
        if (leftCandidates == null || rightCandidates == null)
            return false;

        for (var i = 0; i < leftCandidates.Count; i++)
        {
            var leftCandidate = leftCandidates[i];
            for (var j = 0; j < rightCandidates.Count; j++)
            {
                var rightCandidate = rightCandidates[j];
                if (leftCandidate.PixelPoint.x >= rightCandidate.PixelPoint.x)
                    continue;
                var verticalMismatch = Mathf.Abs(leftCandidate.PixelPoint.y - rightCandidate.PixelPoint.y) /
                                       Mathf.Max(1f, searchRadius);
                var score = leftCandidate.Redness + rightCandidate.Redness - verticalMismatch * 0.35f;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                left = leftCandidate.TopLeftNormalized;
                right = rightCandidate.TopLeftNormalized;
                leftConfidence = leftCandidate.Confidence;
                rightConfidence = rightCandidate.Confidence;
            }
        }

        return bestScore > 0.16f && left.x < right.x;
    }

    private static bool TryRefineVisibleNippleCenter(
        Color32[] pixels,
        int width,
        int height,
        Vector2 seedTopLeft,
        float searchRadius,
        out Vector2 refinedTopLeft,
        out float confidence)
    {
        refinedTopLeft = seedTopLeft;
        confidence = 0f;
        if (pixels == null || pixels.Length != width * height || width < 8 || height < 8)
            return false;

        var center = new Vector2(
            Mathf.Clamp01(seedTopLeft.x) * width,
            (1f - Mathf.Clamp01(seedTopLeft.y)) * height);
        var minX = Mathf.Clamp(Mathf.FloorToInt(center.x - searchRadius), 0, width - 1);
        var maxX = Mathf.Clamp(Mathf.CeilToInt(center.x + searchRadius), 0, width - 1);
        var minY = Mathf.Clamp(Mathf.FloorToInt(center.y - searchRadius), 0, height - 1);
        var maxY = Mathf.Clamp(Mathf.CeilToInt(center.y + searchRadius), 0, height - 1);
        var bestScore = float.MinValue;
        var bestRedness = float.MinValue;
        var bestPoint = Vector2.zero;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if ((new Vector2(x, y) - center).sqrMagnitude > searchRadius * searchRadius)
                    continue;
                var pixel = pixels[y * width + x];
                if (pixel.a < 64)
                    continue;
                var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                var normalizedDistance = Vector2.Distance(new Vector2(x, y), center) /
                                         Mathf.Max(1f, searchRadius);
                var score = redness - normalizedDistance * 0.08f;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                bestRedness = redness;
                bestPoint = new Vector2(x, y);
            }
        }

        if (bestRedness < 0.08f)
            return false;

        var clusterRadius = Mathf.Clamp(searchRadius * 0.38f, 8f, 32f);
        var rednessThreshold = bestRedness - 0.03f;
        var weightedPoint = Vector2.zero;
        var totalWeight = 0f;
        var clusterMinX = Mathf.Clamp(Mathf.FloorToInt(bestPoint.x - clusterRadius), 0, width - 1);
        var clusterMaxX = Mathf.Clamp(Mathf.CeilToInt(bestPoint.x + clusterRadius), 0, width - 1);
        var clusterMinY = Mathf.Clamp(Mathf.FloorToInt(bestPoint.y - clusterRadius), 0, height - 1);
        var clusterMaxY = Mathf.Clamp(Mathf.CeilToInt(bestPoint.y + clusterRadius), 0, height - 1);
        for (var y = clusterMinY; y <= clusterMaxY; y++)
        {
            for (var x = clusterMinX; x <= clusterMaxX; x++)
            {
                if ((new Vector2(x, y) - bestPoint).sqrMagnitude > clusterRadius * clusterRadius)
                    continue;
                var pixel = pixels[y * width + x];
                if (pixel.a < 64)
                    continue;
                var redness = (pixel.r - (pixel.g + pixel.b) * 0.5f) / 255f;
                if (redness < rednessThreshold)
                    continue;
                var weight = Mathf.Max(0.0001f, redness - rednessThreshold);
                weightedPoint += new Vector2(x, y) * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0.0001f)
            return false;
        var refined = weightedPoint / totalWeight;
        if ((refined - center).sqrMagnitude > searchRadius * searchRadius)
            return false;

        refinedTopLeft = new Vector2(
            Mathf.Clamp01(refined.x / width),
            Mathf.Clamp01(1f - refined.y / height));
        confidence = Mathf.Clamp01((bestRedness - 0.05f) / 0.25f);
        return true;
    }

    public static async Task<BoobVisionResult> RunBoobVisionAsync(
        string serverUrl,
        string screenshotPath,
        bool hasNippleAnchors,
        Vector2 viewerLeftNipple,
        Vector2 viewerRightNipple)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            throw new FileNotFoundException("Comfimator screenshot was not created.", screenshotPath);

        var prompt = BoobContourPrompt;
        if (hasNippleAnchors)
        {
            prompt += " Trusted nipple centers are viewer_left=[" +
                      Mathf.RoundToInt(Mathf.Clamp01(viewerLeftNipple.x) * 1000f) + "," +
                      Mathf.RoundToInt(Mathf.Clamp01(viewerLeftNipple.y) * 1000f) +
                      "] and viewer_right=[" +
                      Mathf.RoundToInt(Mathf.Clamp01(viewerRightNipple.x) * 1000f) + "," +
                      Mathf.RoundToInt(Mathf.Clamp01(viewerRightNipple.y) * 1000f) +
                      "]. Each contour must contain its trusted center.";
        }

        var baseUrl = NormalizeServerUrl(serverUrl);
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromMinutes(10);
            var inputName = await UploadImageAsync(client, baseUrl, screenshotPath);
            var workflow = BuildNippleVisionWorkflow(inputName, prompt, 512, 7);
            var promptId = await QueuePromptAsync(client, baseUrl, workflow);
            var response = await WaitForNippleVisionResponseAsync(client, baseUrl, promptId);
            return ParseBoobVisionResponse(response, hasNippleAnchors);
        }
    }

    private static Rect FindVisibleContentBounds(string imagePath)
    {
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(imagePath), false))
                return new Rect(0f, 0f, 1f, 1f);

            var pixels = texture.GetPixels32();
            var minX = texture.width;
            var minY = texture.height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < texture.height; y++)
            {
                var row = y * texture.width;
                for (var x = 0; x < texture.width; x++)
                {
                    if (pixels[row + x].a < 8)
                        continue;

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
                return new Rect(0f, 0f, 1f, 1f);

            var xMin = minX / (float)texture.width;
            var xMax = (maxX + 1f) / texture.width;
            var yMinTop = 1f - (maxY + 1f) / texture.height;
            var yMaxTop = 1f - minY / (float)texture.height;
            return Rect.MinMaxRect(xMin, yMinTop, xMax, yMaxTop);
        }
        catch
        {
            return new Rect(0f, 0f, 1f, 1f);
        }
        finally
        {
            if (texture != null)
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static NippleVisionResult CorrectVisionPairForVisibleContent(
        NippleVisionResult result,
        Rect contentBounds)
    {
        // On narrow transparent sprites Qwen preserves the pair spacing well but can
        // shift the complete pair toward the document's left edge. Correct only that
        // shared horizontal translation; scaling the coordinates would destroy the
        // already-correct distance between the breasts.
        if (contentBounds.width >= 0.98f)
            return result;

        var pairCenterX = (result.ViewerLeft.x + result.ViewerRight.x) * 0.5f;
        var centerDelta = contentBounds.center.x - pairCenterX;
        if (Mathf.Abs(centerDelta) <= 0.025f)
            return result;

        var correction = Mathf.Clamp(centerDelta * 0.7f, -0.12f, 0.12f);
        var left = result.ViewerLeft + Vector2.right * correction;
        var right = result.ViewerRight + Vector2.right * correction;
        if (!IsReasonableVisionPoint(left) || !IsReasonableVisionPoint(right))
            return result;

        return new NippleVisionResult(
            left,
            right,
            result.LeftConfidence,
            result.RightConfidence,
            result.RawResponse + " | alpha-center correction: " + correction.ToString("0.###", CultureInfo.InvariantCulture),
            result.HasBreastBounds ? result.ViewerLeftBreastBounds : (Rect?)null,
            result.HasBreastBounds ? result.ViewerRightBreastBounds : (Rect?)null);
    }

    private static string BuildNippleVisionWorkflow(
        string inputName,
        string prompt,
        int maxTokens = 128,
        int seed = 2)
    {
        return "{" +
               "\"1\":{\"class_type\":\"LoadImage\",\"inputs\":{\"image\":\"" + EscapeJsonString(inputName) + "\"}}," +
               "\"4\":{\"class_type\":\"ImageScale\",\"inputs\":{" +
               "\"image\":[\"1\",0],\"upscale_method\":\"lanczos\"," +
               "\"width\":1024,\"height\":0,\"crop\":\"disabled\"}}," +
               "\"2\":{\"class_type\":\"AILab_QwenVL_Advanced\",\"inputs\":{" +
               "\"image\":[\"4\",0]," +
               "\"model_name\":\"" + NippleVisionModel + "\"," +
               "\"attention_mode\":\"sdpa\",\"use_torch_compile\":false,\"device\":\"auto\"," +
               "\"preset_prompt\":\"\\ud83d\\uddbc\\ufe0f Simple Description\"," +
               "\"custom_prompt\":\"" + EscapeJsonString(prompt) + "\"," +
               "\"max_tokens\":" + Mathf.Clamp(maxTokens, 64, 4096) +
               ",\"temperature\":0.1,\"top_p\":0.9,\"num_beams\":1," +
               "\"repetition_penalty\":1.1,\"frame_count\":1,\"keep_model_loaded\":true," +
               "\"seed\":" + Mathf.Max(1, seed) + ",\"keep_last_prompt\":false}}," +
               "\"3\":{\"class_type\":\"ShowText|pysssss\",\"inputs\":{\"text\":[\"2\",0]}}" +
               "}";
    }

    private static NippleVisionResult RefineNippleVisionWithBreastBounds(
        NippleVisionResult direct,
        string boundsResponse)
    {
        if (!TryParseVisionRect(boundsResponse, "viewer_left_breast_bbox", out var leftBounds) ||
            !TryParseVisionRect(boundsResponse, "viewer_right_breast_bbox", out var rightBounds))
        {
            return direct;
        }

        var leftBoundsCenter = leftBounds.center;
        var rightBoundsCenter = rightBounds.center;
        if (leftBoundsCenter.x > rightBoundsCenter.x)
        {
            var rectSwap = leftBounds;
            leftBounds = rightBounds;
            rightBounds = rectSwap;
            leftBoundsCenter = leftBounds.center;
            rightBoundsCenter = rightBounds.center;
        }

        var boundsSeparation = rightBoundsCenter.x - leftBoundsCenter.x;
        var overlapWidth = Mathf.Max(0f, Mathf.Min(leftBounds.xMax, rightBounds.xMax) -
                                         Mathf.Max(leftBounds.xMin, rightBounds.xMin));
        var overlapRatio = overlapWidth / Mathf.Max(0.0001f, Mathf.Min(leftBounds.width, rightBounds.width));
        var boundsAreReliable =
            IsReasonableVisionPoint(leftBoundsCenter) &&
            IsReasonableVisionPoint(rightBoundsCenter) &&
            overlapRatio <= 0.9f &&
            boundsSeparation >= 0.025f &&
            boundsSeparation <= 0.75f;

        if (!boundsAreReliable)
            return direct;

        // Direct points can spread toward a garment edge. Pull X toward each
        // volume center and Y toward the upper-middle of the full mound: the
        // nipple/apex normally lies above the geometric box center, especially
        // on a reclining body.
        var left = new Vector2(
            Mathf.Lerp(direct.ViewerLeft.x, leftBoundsCenter.x, 0.5f),
            Mathf.Lerp(direct.ViewerLeft.y, leftBounds.yMin + leftBounds.height * 0.42f, 0.75f));
        var right = new Vector2(
            Mathf.Lerp(direct.ViewerRight.x, rightBoundsCenter.x, 0.5f),
            Mathf.Lerp(direct.ViewerRight.y, rightBounds.yMin + rightBounds.height * 0.42f, 0.75f));
        return new NippleVisionResult(
            left,
            right,
            direct.LeftConfidence,
            direct.RightConfidence,
            direct.RawResponse + " | bounds: " + boundsResponse,
            leftBounds,
            rightBounds);
    }

    private static bool TryParseVisionRect(string source, string key, out Rect rect)
    {
        rect = default;
        var match = Regex.Match(
            source ?? string.Empty,
            "\\\"?" + Regex.Escape(key) +
            "\\\"?\\s*:\\s*\\[\\s*(-?\\d+(?:\\.\\d+)?)\\s*,\\s*(-?\\d+(?:\\.\\d+)?)" +
            "\\s*,\\s*(-?\\d+(?:\\.\\d+)?)\\s*,\\s*(-?\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase);
        if (!match.Success ||
            !TryParseInvariantFloat(match.Groups[1].Value, out var x1) ||
            !TryParseInvariantFloat(match.Groups[2].Value, out var y1) ||
            !TryParseInvariantFloat(match.Groups[3].Value, out var x2) ||
            !TryParseInvariantFloat(match.Groups[4].Value, out var y2))
        {
            return false;
        }

        x1 /= 1000f;
        y1 /= 1000f;
        x2 /= 1000f;
        y2 /= 1000f;
        rect = Rect.MinMaxRect(Mathf.Min(x1, x2), Mathf.Min(y1, y2), Mathf.Max(x1, x2), Mathf.Max(y1, y2));
        return rect.width >= 0.025f && rect.width <= 0.5f &&
               rect.height >= 0.025f && rect.height <= 0.5f;
    }

    private static async Task<string> WaitForNippleVisionResponseAsync(
        HttpClient client,
        string baseUrl,
        string promptId)
    {
        var deadline = DateTime.UtcNow.AddMinutes(9);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            var response = await client.GetAsync(baseUrl + "/history/" + promptId);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            var text = MatchString(
                body,
                "\\\"3\\\"\\s*:\\s*\\{\\s*\\\"text\\\"\\s*:\\s*\\[\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            if (Regex.IsMatch(body, "\\\"status_str\\\"\\s*:\\s*\\\"error\\\"", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Qwen nipple detection failed in Comfy: " + body);
        }

        throw new TimeoutException("Qwen nipple detection timed out: " + promptId);
    }

    private static NippleVisionResult ParseNippleVisionResponse(string response)
    {
        if (!TryParseVisionPoint(response, "viewer_left", out var left) ||
            !TryParseVisionPoint(response, "viewer_right", out var right))
        {
            throw new InvalidOperationException("Qwen returned no nipple coordinates: " + response);
        }

        left /= 1000f;
        right /= 1000f;
        if (left.x > right.x)
        {
            var swap = left;
            left = right;
            right = swap;
        }

        if (!IsReasonableVisionPoint(left) || !IsReasonableVisionPoint(right) ||
            right.x - left.x < 0.025f)
        {
            throw new InvalidOperationException("Qwen returned implausible nipple coordinates: " + response);
        }

        var leftConfidence = 0.65f;
        var rightConfidence = 0.65f;
        var confidenceMatch = Regex.Match(
            response ?? string.Empty,
            "\\\"?confidence\\\"?\\s*:\\s*\\[\\s*(-?\\d+(?:\\.\\d+)?)\\s*,\\s*(-?\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase);
        if (confidenceMatch.Success)
        {
            TryParseInvariantFloat(confidenceMatch.Groups[1].Value, out leftConfidence);
            TryParseInvariantFloat(confidenceMatch.Groups[2].Value, out rightConfidence);
            if (leftConfidence > 1f) leftConfidence /= 100f;
            if (rightConfidence > 1f) rightConfidence /= 100f;
        }

        return new NippleVisionResult(left, right, leftConfidence, rightConfidence, response);
    }

    private static BoobVisionResult ParseBoobVisionResponse(string response, bool allowMissingContours)
    {
        var hasLeft = TryParseVisionContour(response, "viewer_left_breast_contour", out var left);
        var hasRight = TryParseVisionContour(response, "viewer_right_breast_contour", out var right);
        if ((!hasLeft || !hasRight) && !allowMissingContours)
        {
            throw new InvalidOperationException("Qwen returned no breast contours: " + response);
        }

        if (hasLeft && hasRight && AveragePointX(left) > AveragePointX(right))
        {
            var contourSwap = left;
            left = right;
            right = contourSwap;
        }

        ParseConfidencePair(response, out var leftConfidence, out var rightConfidence);
        return new BoobVisionResult(left, right, leftConfidence, rightConfidence, response);
    }

    private static bool TryParseVisionContour(string source, string key, out Vector2[] contour)
    {
        contour = Array.Empty<Vector2>();
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var keyMatch = Regex.Match(
            source,
            "\\\"?" + Regex.Escape(key) + "\\\"?\\s*:\\s*\\[",
            RegexOptions.IgnoreCase);
        if (!keyMatch.Success)
            return false;

        var arrayStart = source.IndexOf('[', keyMatch.Index + keyMatch.Length - 1);
        if (arrayStart < 0)
            return false;

        var depth = 0;
        var arrayEnd = -1;
        for (var i = arrayStart; i < source.Length; i++)
        {
            if (source[i] == '[')
                depth++;
            else if (source[i] == ']' && --depth == 0)
            {
                arrayEnd = i;
                break;
            }
        }
        if (arrayEnd <= arrayStart)
            return false;

        var block = source.Substring(arrayStart, arrayEnd - arrayStart + 1);
        var matches = Regex.Matches(
            block,
            "\\[\\s*(-?\\d+(?:\\.\\d+)?)\\s*,\\s*(-?\\d+(?:\\.\\d+)?)\\s*\\]");
        // Qwen sometimes returns a perfectly valid coarse quadrilateral with
        // the first point repeated at the end (five pairs total). The detailed
        // 24-point contour is generated locally afterwards, so rejecting this
        // coarse helper polygon would abort the useful result for no reason.
        if (matches.Count < 4 || matches.Count > 64)
            return false;

        var points = new List<Vector2>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            if (!TryParseInvariantFloat(matches[i].Groups[1].Value, out var x) ||
                !TryParseInvariantFloat(matches[i].Groups[2].Value, out var y))
            {
                return false;
            }

            var point = new Vector2(x / 1000f, y / 1000f);
            if (point.x < 0.01f || point.x > 0.99f || point.y < 0.03f || point.y > 0.95f)
                return false;
            points.Add(point);
        }

        if (Mathf.Abs(SignedPolygonArea(points)) < 0.0002f)
            return false;

        contour = points.ToArray();
        return true;
    }

    private static float SignedPolygonArea(IReadOnlyList<Vector2> points)
    {
        var area = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area * 0.5f;
    }

    private static float AveragePointX(IReadOnlyList<Vector2> points)
    {
        var sum = 0f;
        for (var i = 0; i < points.Count; i++)
            sum += points[i].x;
        return points.Count > 0 ? sum / points.Count : 0f;
    }

    private static void ParseConfidencePair(string response, out float leftConfidence, out float rightConfidence)
    {
        leftConfidence = 0.65f;
        rightConfidence = 0.65f;
        var confidenceMatch = Regex.Match(
            response ?? string.Empty,
            "\\\"?confidence\\\"?\\s*:\\s*\\[\\s*(-?\\d+(?:\\.\\d+)?)\\s*,\\s*(-?\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase);
        if (!confidenceMatch.Success)
            return;

        TryParseInvariantFloat(confidenceMatch.Groups[1].Value, out leftConfidence);
        TryParseInvariantFloat(confidenceMatch.Groups[2].Value, out rightConfidence);
        if (leftConfidence > 1f) leftConfidence /= 100f;
        if (rightConfidence > 1f) rightConfidence /= 100f;
    }

    private static bool TryParseVisionPoint(string source, string key, out Vector2 point)
    {
        point = Vector2.zero;
        var match = Regex.Match(
            source ?? string.Empty,
            "\\\"?" + Regex.Escape(key) + "\\\"?\\s*:\\s*\\[\\s*(-?\\d+(?:\\.\\d+)?)\\s*,\\s*(-?\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase);
        if (!match.Success ||
            !TryParseInvariantFloat(match.Groups[1].Value, out var x) ||
            !TryParseInvariantFloat(match.Groups[2].Value, out var y))
        {
            return false;
        }

        point = new Vector2(x, y);
        return true;
    }

    private static bool TryParseInvariantFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool IsReasonableVisionPoint(Vector2 point)
    {
        return point.x >= 0.02f && point.x <= 0.98f && point.y >= 0.08f && point.y <= 0.85f;
    }

    private static async Task<string> UploadImageAsync(HttpClient client, string baseUrl, string path)
    {
        var fileName = "reanimator_" + Guid.NewGuid().ToString("N") + ".png";
        using (var content = new MultipartFormDataContent())
        {
            content.Add(new ByteArrayContent(File.ReadAllBytes(path)), "image", fileName);
            content.Add(new StringContent("input"), "type");
            content.Add(new StringContent("true"), "overwrite");

            var response = await client.PostAsync(baseUrl + "/upload/image", content);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            var uploadedName = MatchString(body, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            return string.IsNullOrWhiteSpace(uploadedName) ? fileName : uploadedName;
        }
    }

    private static async Task<string> QueuePromptAsync(HttpClient client, string baseUrl, string workflowJson)
    {
        var payload = "{\"prompt\":" + workflowJson + "}";
        var response = await client.PostAsync(
            baseUrl + "/prompt",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        var promptId = MatchString(body, "\"prompt_id\"\\s*:\\s*\"([^\"]+)\"");
        if (string.IsNullOrWhiteSpace(promptId))
            throw new InvalidOperationException("Comfy did not return prompt_id: " + body);
        return promptId;
    }

    internal static string ScopeAutoSkeletonKeypoints(JObject graph)
    {
        var prefix = "reanimator_auto_skeleton_keypoints_" + Guid.NewGuid().ToString("N");
        var hasFileOutput = false;
        foreach (var node in graph.Properties())
        {
            if ((string)node.Value["class_type"] != "SavePoseKpsAsJsonFile" ||
                !(node.Value["inputs"] is JObject inputs))
                continue;

            inputs["filename_prefix"] = prefix;
            hasFileOutput = true;
        }
        return hasFileOutput ? prefix : null;
    }

    internal static JObject GetCompletedAutoSkeletonOutputs(string history, string promptId)
    {
        var entry = JObject.Parse(history)[promptId] as JObject;
        var status = entry?["status"] as JObject;
        if ((string)status?["status_str"] == "error")
            throw new InvalidOperationException("Auto Skeleton failed in ComfyUI (" + promptId + "): " +
                                                status["messages"]?.ToString(Formatting.None));

        return (bool?)status?["completed"] == true ? entry["outputs"] as JObject ?? new JObject() : null;
    }

    private static async Task<AutoSkeletonResult> WaitForAutoSkeletonOutputAsync(
        HttpClient client,
        string baseUrl,
        string promptId,
        string keypointsPrefix)
    {
        using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45)))
        {
            try
            {
                while (true)
                {
                    await Task.Delay(500, timeout.Token);
                    using (var response = await client.GetAsync(baseUrl + "/history/" + Uri.EscapeDataString(promptId), timeout.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        var outputs = GetCompletedAutoSkeletonOutputs(await response.Content.ReadAsStringAsync(), promptId);
                        if (outputs == null)
                            continue;

                        return await ReadAutoSkeletonResultAsync(client, baseUrl, outputs, keypointsPrefix, timeout.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("Comfy prompt timed out: " + promptId);
            }
        }
    }

    private static async Task<AutoSkeletonResult> ReadAutoSkeletonResultAsync(
        HttpClient client, string baseUrl, JObject outputs, string keypointsPrefix,
        System.Threading.CancellationToken cancellationToken)
    {
        string imageFileName = null;
        JObject poseFile = null;
        string poseJson = null;
        foreach (var output in outputs.Properties())
        {
            if (!(output.Value is JObject node))
                continue;
            if (node["openpose_json"] is JArray poses && poses.Count > 0)
                poseJson = (string)poses[0];
            foreach (var field in node.Properties())
            {
                if (!(field.Value is JArray files))
                    continue;
                foreach (var file in files)
                {
                    if (!(file is JObject descriptor))
                        continue;
                    var name = (string)descriptor["filename"];
                    if (name?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true)
                        imageFileName ??= name;
                    if (name?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true)
                        poseFile ??= descriptor;
                }
            }
        }

        var poseFileName = "history:openpose_json";
        if (string.IsNullOrWhiteSpace(poseJson) && poseFile != null)
        {
            poseFileName = (string)poseFile["filename"];
            var query = "?filename=" + Uri.EscapeDataString(poseFileName) +
                        "&subfolder=" + Uri.EscapeDataString((string)poseFile["subfolder"] ?? "") +
                        "&type=" + Uri.EscapeDataString((string)poseFile["type"] ?? "output");
            using (var response = await client.GetAsync(baseUrl + "/view" + query, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                poseJson = await response.Content.ReadAsStringAsync();
            }
        }
        if (string.IsNullOrWhiteSpace(poseJson) && keypointsPrefix != null && Directory.Exists(DefaultOutputDirectory))
        {
            var files = Directory.GetFiles(DefaultOutputDirectory, keypointsPrefix + "*.json");
            if (files.Length == 1)
            {
                poseFileName = Path.GetFileName(files[0]);
                poseJson = File.ReadAllText(files[0], Encoding.UTF8);
            }
        }
        if (string.IsNullOrWhiteSpace(imageFileName) || string.IsNullOrWhiteSpace(poseJson))
            throw new InvalidOperationException("Auto Skeleton completed without a pose image or unambiguous keypoint JSON.");

        return new AutoSkeletonResult(imageFileName, poseFileName, poseJson);
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        var result = string.IsNullOrWhiteSpace(serverUrl) ? DefaultServerUrl : serverUrl.Trim();
        return result.EndsWith("/", StringComparison.Ordinal) ? result.Substring(0, result.Length - 1) : result;
    }

    private static string MatchString(string source, string pattern)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        var match = Regex.Match(source, pattern, RegexOptions.Singleline);
        return match.Success ? UnescapeJsonString(match.Groups[1].Value) : string.Empty;
    }

    private static string EscapeJsonString(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static string UnescapeJsonString(string value)
    {
        return Regex.Unescape(value ?? string.Empty);
    }
}
#endif
