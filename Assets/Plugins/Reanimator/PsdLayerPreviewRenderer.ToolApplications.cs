#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Rendering;

public partial class PsdLayerPreviewRenderer
{
    public bool HasElbowSelection()
    {
        return FindActiveElbowLineIndex() >= 0;
    }

    public bool HasLimbSelection()
    {
        return FindActiveLimbLineIndex() >= 0;
    }

}

#endif
