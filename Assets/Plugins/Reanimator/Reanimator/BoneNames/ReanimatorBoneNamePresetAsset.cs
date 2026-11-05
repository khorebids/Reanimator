#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "Reanimator Bone Name Presets",
    menuName = "KhorebdisLab/Reanimator/Bone Name Presets")]
public sealed class ReanimatorBoneNamePresetAsset : ScriptableObject
{
    public List<string> names = new List<string>();
}
#endif
