using System;
using UnityEngine;

public sealed partial class ReaniIK
{
    // Unity does not bind animation curves through the serialized reference
    // classes used by the rig modules. Primitive component fields are the
    // stable animation channels; existing module settings remain compatible.
    [HideInInspector] public float animate_facePose_headTurn,animate_facePose_neckTurn;
    [HideInInspector] public float animate_facePose_gaze_x,animate_facePose_gaze_y;
    [HideInInspector] public float animate_facePose_upperLipHeight,animate_facePose_upperLipSize;
    [HideInInspector] public float animate_facePose_lowerLipHeight,animate_facePose_lowerLipSize;
    [HideInInspector] public float animate_facePose_lipCornersHeight,animate_facePose_eyelidClosure;
    [HideInInspector] public float animate_bodyScale_size=1;
    [HideInInspector] public float animate_bodyScale_leftArmSize=1,animate_bodyScale_rightArmSize=1;
    [HideInInspector] public float animate_surfaceEffects_tremor,animate_surfaceEffects_redness,animate_surfaceEffects_sweat;
    [HideInInspector] public float animate_surfaceEffects_darkness,animate_surfaceEffects_contrast,animate_surfaceEffects_desaturation;
    [HideInInspector] public float animate_leftArmMask_amount=1,animate_rightArmMask_amount=1;
    [HideInInspector] public bool animate_leftArmMask_flip,animate_rightArmMask_flip;
    [HideInInspector] public bool animate_leftArm_flip=true,animate_rightArm_flip,animate_leftLeg_flip=true,animate_rightLeg_flip,animate_leftArm2_flip,animate_rightArm2_flip;
    [HideInInspector] public bool animate_leftArm_invertDinoFlip,animate_rightArm_invertDinoFlip,animate_leftLeg_invertDinoFlip,animate_rightLeg_invertDinoFlip,animate_leftArm2_invertDinoFlip,animate_rightArm2_invertDinoFlip;
    [SerializeField,HideInInspector] private int animationControlsVersion;
    [NonSerialized] private float[] animationControlLast;

    public static string AnimationControlProperty(string moduleProperty)=>"animate_"+moduleProperty.Replace('.','_');

    public void SynchronizeAnimationControls(bool sampling=false)
    {
        bool initialize=animationControlLast==null||animationControlsVersion==0;
        if(initialize)animationControlLast=new float[35];
        int index=0;
        Float(ref animate_facePose_headTurn,ref facePose.headTurn);
        Float(ref animate_facePose_neckTurn,ref facePose.neckTurn);
        Float(ref animate_facePose_gaze_x,ref facePose.gaze.x);Float(ref animate_facePose_gaze_y,ref facePose.gaze.y);
        Float(ref animate_facePose_upperLipHeight,ref facePose.upperLipHeight);Float(ref animate_facePose_upperLipSize,ref facePose.upperLipSize);
        Float(ref animate_facePose_lowerLipHeight,ref facePose.lowerLipHeight);Float(ref animate_facePose_lowerLipSize,ref facePose.lowerLipSize);
        Float(ref animate_facePose_lipCornersHeight,ref facePose.lipCornersHeight);Float(ref animate_facePose_eyelidClosure,ref facePose.eyelidClosure);
        Float(ref animate_bodyScale_size,ref bodyScale.size);
        Float(ref animate_bodyScale_leftArmSize,ref bodyScale.leftArmSize);
        Float(ref animate_bodyScale_rightArmSize,ref bodyScale.rightArmSize);
        Float(ref animate_surfaceEffects_tremor,ref surfaceEffects.tremor);Float(ref animate_surfaceEffects_redness,ref surfaceEffects.redness);
        Float(ref animate_surfaceEffects_sweat,ref surfaceEffects.sweat);Float(ref animate_surfaceEffects_darkness,ref surfaceEffects.darkness);
        Float(ref animate_surfaceEffects_contrast,ref surfaceEffects.contrast);Float(ref animate_surfaceEffects_desaturation,ref surfaceEffects.desaturation);
        Float(ref animate_leftArmMask_amount,ref leftArmMask.amount);Bool(ref animate_leftArmMask_flip,ref leftArmMask.flip);
        Float(ref animate_rightArmMask_amount,ref rightArmMask.amount);Bool(ref animate_rightArmMask_flip,ref rightArmMask.flip);
        Bool(ref animate_leftArm_flip,ref leftArm.flip);Bool(ref animate_leftArm_invertDinoFlip,ref leftArm.invertDinoFlip);
        Bool(ref animate_rightArm_flip,ref rightArm.flip);Bool(ref animate_rightArm_invertDinoFlip,ref rightArm.invertDinoFlip);
        Bool(ref animate_leftLeg_flip,ref leftLeg.flip);Bool(ref animate_leftLeg_invertDinoFlip,ref leftLeg.invertDinoFlip);
        Bool(ref animate_rightLeg_flip,ref rightLeg.flip);Bool(ref animate_rightLeg_invertDinoFlip,ref rightLeg.invertDinoFlip);
        Bool(ref animate_leftArm2_flip,ref leftArm2.flip);Bool(ref animate_leftArm2_invertDinoFlip,ref leftArm2.invertDinoFlip);
        Bool(ref animate_rightArm2_flip,ref rightArm2.flip);Bool(ref animate_rightArm2_invertDinoFlip,ref rightArm2.invertDinoFlip);
        animationControlsVersion=1;

        void Float(ref float channel,ref float module)
        {
            float last=animationControlLast[index];
            if(initialize&&animationControlsVersion==0)channel=module;
            else if(initialize||sampling||channel!=last)module=channel;
            else if(module!=last)channel=module;
            animationControlLast[index++]=channel;
        }
        void Bool(ref bool channel,ref bool module)
        {
            float a=channel?1:0,b=module?1:0;Float(ref a,ref b);channel=a>.5f;module=b>.5f;
        }
    }
}
