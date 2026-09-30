using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BuildKnightAnimator
{
    private const string ControllerPath = "Assets/Characters/knight_3d/KnightAnimator.controller";

    public static string Run()
    {
        var idleClip = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/knight_3d/motion_idle.fbx")
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.Contains("__preview__"));
        var attackClip = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/knight_3d/motion_attack.fbx")
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.Contains("__preview__"));
        if (idleClip == null || attackClip == null) return "FAILED: missing clip(s)";

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

        AnimatorControllerLayer layer = controller.layers[0];
        AnimatorStateMachine sm = layer.stateMachine;

        AnimatorState idleState = sm.AddState("Idle");
        idleState.motion = idleClip;
        idleState.iKOnFeet = true;
        sm.defaultState = idleState;

        AnimatorState attackState = sm.AddState("Attack");
        attackState.motion = attackClip;
        attackState.iKOnFeet = true;

        AnimatorStateTransition toAttack = idleState.AddTransition(attackState);
        toAttack.hasExitTime = false;
        toAttack.hasFixedDuration = true;
        toAttack.duration = 0.1f;
        toAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");

        AnimatorStateTransition toIdle = attackState.AddTransition(idleState);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.95f;
        toIdle.hasFixedDuration = true;
        toIdle.duration = 0.15f;

        // Re-apply since AddState/AddTransition mutate copies in some Animator API versions.
        layer.stateMachine = sm;
        controller.layers = new[] { layer }.Concat(controller.layers.Skip(1)).ToArray();

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"OK: controller at {ControllerPath}, idleClip={idleClip.name} ({idleClip.length}s, loop={idleClip.isLooping}), attackClip={attackClip.name} ({attackClip.length}s)";
    }
}
