using UnityEngine;

public sealed partial class ReaniIK
{
    private ShoulderAttachmentRig2D[] hiddenShoulderRigs;
    private ElbowJointRig2D[] hiddenJointRigs;
    private SideAssRig2D[] hiddenSideAssRigs;

    public void RefreshBoneRigDrivers()
    {
        hiddenShoulderRigs=GetComponentsInChildren<ShoulderAttachmentRig2D>(true);
        hiddenJointRigs=GetComponentsInChildren<ElbowJointRig2D>(true);
        hiddenSideAssRigs=GetComponentsInChildren<SideAssRig2D>(true);
    }

    private void EvaluateHiddenBoneRigs(float dt)
    {
        if(hiddenJointRigs==null)RefreshBoneRigDrivers();
        foreach(var driver in hiddenShoulderRigs)
            if(driver && !driver.gameObject.activeInHierarchy)
            {if(driver.enabled)driver.Evaluate();else driver.RestorePose();}
        foreach(var driver in hiddenJointRigs)
            if(driver && !driver.gameObject.activeInHierarchy)
            {if(driver.enabled)driver.Evaluate();else driver.RestorePose();}
        foreach(var driver in hiddenSideAssRigs)
            if(driver && !driver.gameObject.activeInHierarchy)
            {if(driver.enabled)driver.Evaluate(dt);else driver.RestorePose();}
    }

    private void RestoreHiddenBoneRigPoses()
    {
        if(hiddenJointRigs==null)return;
        foreach(var driver in hiddenShoulderRigs)if(driver && !driver.gameObject.activeInHierarchy)driver.RestorePose();
        foreach(var driver in hiddenJointRigs)if(driver && !driver.gameObject.activeInHierarchy)driver.RestorePose();
        foreach(var driver in hiddenSideAssRigs)if(driver && !driver.gameObject.activeInHierarchy)driver.RestorePose();
    }
}
