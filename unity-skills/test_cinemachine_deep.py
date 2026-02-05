
import unity_skills
import time

def test_cinemachine_deep():
    print("🚀 Starting Cinemachine v2.0 Deep Skills Verification...")
    
    # Setup Names
    vcam_name = "CM_Deep_SplineCam"
    group_name = "CM_Deep_TargetGroup"
    spline_name = "CM_Deep_SplinePath"
    t1_name = "Target_A"
    t2_name = "Target_B"

    # 1. Create TargetGroup
    print("\n[1/5] Testing TargetGroup Creation...")
    res = unity_skills.cinemachine_create_target_group(name=group_name)
    print(f"   -> Result: {res}")
    
    # Create Dummy Targets
    unity_skills.run_command_open(command=f'new GameObject("{t1_name}").transform.position = Vector3.left * 5;')
    unity_skills.run_command_open(command=f'new GameObject("{t2_name}").transform.position = Vector3.right * 5;')

    # 2. Add Members
    print("\n[2/5] Testing Add Member...")
    res = unity_skills.cinemachine_target_group_add_member(groupName=group_name, targetName=t1_name, weight=2.0, radius=1.5)
    print(f"   -> Add Target A: {res}")
    res = unity_skills.cinemachine_target_group_add_member(groupName=group_name, targetName=t2_name, weight=1.0, radius=3.0)
    print(f"   -> Add Target B: {res}")

    # 3. Create VCam and Assign TargetGroup
    print("\n[3/5] Assigning TargetGroup to VCam...")
    unity_skills.cinemachine_create_vcam(name=vcam_name)
    res = unity_skills.cinemachine_set_targets(vcamName=vcam_name, lookAtName=group_name, followName=group_name)
    print(f"   -> Set Targets: {res}")

    # 4. Spline Dolly Integration
    print("\n[4/5] Testing Spline Dolly Integration...")
    # Create Spline Container
    unity_skills.run_command_open(command=f'new GameObject("{spline_name}").AddComponent<UnityEngine.Splines.SplineContainer>()')
    
    # Set VCam Body to SplineDolly
    print("   -> Switching Body to SplineDolly")
    unity_skills.cinemachine_set_component(vcamName=vcam_name, stage="Body", componentType="CinemachineSplineDolly")
    
    # Bind Spline
    print("   -> Binding Spline Container")
    res = unity_skills.cinemachine_set_spline(vcamName=vcam_name, splineName=spline_name)
    if res.get('success'):
        print(f"   ✅ Success: {res.get('message')}")
    else:
        print(f"   ❌ Failed: {res}")

    # 5. Clean up (Optional, commented out for manual inspection)
    # unity_skills.delete_gameobject(name=vcam_name)
    # unity_skills.delete_gameobject(name=group_name)
    
    print("\n🎉 Deep Skills Verification Complete!")

if __name__ == "__main__":
    test_cinemachine_deep()
