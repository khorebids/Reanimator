#if UNITY_EDITOR
public sealed class PsdPreviewToolOptions
{
    public bool AutoGeometry;
    public bool Elbow;
    public bool Limb;
    public bool LimbBone;
    public int LimbBoneChild;
    public bool LimbBoneCull;
    public bool Boob;
    public bool Curve;
    public bool WeightSlider;
    public bool CreateEdge;
    public bool SplitEdge;
    public bool EditGeometry;
    public bool RadiusCull;
    public bool Joint;
    public bool BoneInfluence;
    public bool SpriteInfluence;

    public void Clear()
    {
        AutoGeometry = false;
        Elbow = false;
        Limb = false;
        LimbBone = false;
        LimbBoneChild = -1;
        LimbBoneCull = false;
        Boob = false;
        Curve = false;
        WeightSlider = false;
        CreateEdge = false;
        SplitEdge = false;
        EditGeometry = false;
        RadiusCull = false;
        Joint = false;
        BoneInfluence = false;
        SpriteInfluence = false;
    }
}
#endif
