#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public partial class Reanimator
{
    [SerializeField] private bool reanimateShowIdleMascot;
    [SerializeField] private bool reanimateIdleMascotError;
    [SerializeField] private bool reanimateIdleMascotSuccess;
    private static Texture2D idleMascot;
    private static Texture2D errorMascot;
    private static Texture2D successMascot;

    private void ResetSuccessfulMascotOnNavigation()
    {
        if(!reanimateIdleMascotSuccess)return;
        reanimateIdleMascotSuccess=false;
        reanimateIdleMascotError=false;
        reanimateShowIdleMascot=true;
        Repaint();
    }

    private bool DrawIdleMascotFooter(Rect area)
    {
        if(!idleMascot)idleMascot=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/Reanimator/Graphics/idle-mascot.png");
        if(reanimateIdleMascotError && !errorMascot)errorMascot=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/Reanimator/Graphics/error-mascot.png");
        if(reanimateIdleMascotSuccess && !successMascot)successMascot=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/Reanimator/Graphics/success-mascot.png");
        var mascot=reanimateIdleMascotError && errorMascot?errorMascot:
            reanimateIdleMascotSuccess && successMascot?successMascot:idleMascot;
        if(!mascot)return false;
        float scale=Mathf.Min(Mathf.Max(1,area.width-12)/mascot.width,Mathf.Max(1,area.height-12)/mascot.height);
        var artwork=new Rect(area.center.x-mascot.width*scale*.5f,
            area.yMax-6-mascot.height*scale,mascot.width*scale,mascot.height*scale);
        var tint=GUI.color;GUI.color=Color.white;
        GUI.DrawTexture(artwork,mascot,ScaleMode.StretchToFill,true);
        GUI.color=tint;
        EditorGUIUtility.AddCursorRect(artwork,MouseCursor.Link);
        if(GUI.Button(artwork,new GUIContent(string.Empty,"Show layer indicator"),GUIStyle.none))
        {
            reanimateShowIdleMascot=false;
            Repaint();
        }
        return true;
    }
}
#endif
