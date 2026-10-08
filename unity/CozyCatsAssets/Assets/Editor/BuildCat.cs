using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Run headless: Unity.exe -batchmode -quit -projectPath <this project> -executeMethod BuildCat.Build
public static class BuildCat
{
    const string FbxPath = "Assets/Cat/CatModel.fbx";
    const string ControllerPath = "Assets/Cat/CatAnimator.controller";
    const string MaskPath = "Assets/Cat/OverlayMask.mask";
    const string PrefabPath = "Assets/Cat/CatModel.prefab";
    const string BundleName = "cozycats";

    static readonly string[] LoopingClips = { "Idle", "Sit", "Loaf", "Held" };
    static readonly string[] OverlayBones = { "Eye.L", "Eye.R", "Ear.L", "Ear.R", "Jaw" };

    [MenuItem("CozyCats/Build Bundle")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        ConfigureImporter();
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
        Debug.Log("[BuildCat] clips: " + string.Join(", ", clips.Keys));

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var mask = BuildMask(model);
        var controller = BuildController(clips, mask);
        BuildPrefab(model, controller);
        TagAudio();

        Directory.CreateDirectory("AssetBundles");
        BuildPipeline.BuildAssetBundles("AssetBundles", BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Report(model);
        Debug.Log("[BuildCat] done");
    }

    static void ConfigureImporter()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        imp.bakeAxisConversion = true;
        imp.globalScale = 1f;
        imp.useFileScale = true;
        imp.importBlendShapes = false;
        imp.importCameras = false;
        imp.importLights = false;
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        imp.importAnimation = true;
        imp.resampleCurves = false;
        imp.animationCompression = ModelImporterAnimationCompression.Off;

        var takes = imp.importedTakeInfos;
        imp.clipAnimations = takes.Select(t =>
        {
            string name = t.name.Contains("|") ? t.name.Substring(t.name.LastIndexOf('|') + 1) : t.name;
            return new ModelImporterClipAnimation
            {
                name = name,
                takeName = t.name,
                firstFrame = t.startTime * t.sampleRate,
                lastFrame = t.stopTime * t.sampleRate,
                loopTime = LoopingClips.Contains(name),
                lockRootRotation = false,
                lockRootHeightY = false,
                lockRootPositionXZ = false,
            };
        }).ToArray();
        imp.SaveAndReimport();
    }

    static AvatarMask BuildMask(GameObject model)
    {
        AssetDatabase.DeleteAsset(MaskPath);
        var mask = new AvatarMask();
        var all = model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform).ToArray();
        mask.transformCount = all.Length;
        for (int i = 0; i < all.Length; i++)
        {
            mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(all[i], model.transform));
            mask.SetTransformActive(i, OverlayBones.Contains(all[i].name));
        }
        AssetDatabase.CreateAsset(mask, MaskPath);
        return mask;
    }

    static AnimatorController BuildController(System.Collections.Generic.Dictionary<string, AnimationClip> clips, AvatarMask mask)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ac.AddParameter("Pose", AnimatorControllerParameterType.Int);
        ac.AddParameter("Blink", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("TwitchL", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("TwitchR", AnimatorControllerParameterType.Trigger);
        if (clips.ContainsKey("Meow")) ac.AddParameter("Meow", AnimatorControllerParameterType.Trigger);

        var baseSm = ac.layers[0].stateMachine;
        string[] poses = { "Idle", "Sit", "Loaf", "Held" };
        for (int i = 0; i < poses.Length; i++)
        {
            if (!clips.TryGetValue(poses[i], out var clip)) continue;
            var st = baseSm.AddState(poses[i]);
            st.motion = clip;
            if (poses[i] == "Loaf") baseSm.defaultState = st;
            var tr = baseSm.AddAnyStateTransition(st);
            tr.AddCondition(AnimatorConditionMode.Equals, i, "Pose");
            tr.duration = poses[i] == "Held" ? 0.2f : 0.45f;
            tr.hasExitTime = false;
            tr.canTransitionToSelf = false;
        }

        var overlay = new AnimatorControllerLayer
        {
            name = "Overlay",
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = mask,
            stateMachine = new AnimatorStateMachine { name = "Overlay" },
        };
        AssetDatabase.AddObjectToAsset(overlay.stateMachine, ac);
        ac.AddLayer(overlay);
        var sm = ac.layers[1].stateMachine;
        var empty = sm.AddState("Empty");
        sm.defaultState = empty;
        AddOverlay(sm, empty, clips, "Blink", "Blink");
        AddOverlay(sm, empty, clips, "EarTwitch.L", "TwitchL");
        AddOverlay(sm, empty, clips, "EarTwitch.R", "TwitchR");
        if (clips.ContainsKey("Meow")) AddOverlay(sm, empty, clips, "Meow", "Meow");

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    static void AddOverlay(AnimatorStateMachine sm, AnimatorState empty, System.Collections.Generic.Dictionary<string, AnimationClip> clips, string clipName, string trigger)
    {
        if (!clips.TryGetValue(clipName, out var clip)) return;
        var st = sm.AddState(clipName.Replace(".", ""));
        st.motion = clip;
        var into = sm.AddAnyStateTransition(st);
        into.AddCondition(AnimatorConditionMode.If, 0, trigger);
        into.duration = 0.05f;
        into.hasExitTime = false;
        into.canTransitionToSelf = false;
        var back = st.AddTransition(empty);
        back.hasExitTime = true;
        back.exitTime = 1f;
        back.duration = 0.05f;
    }

    static void BuildPrefab(GameObject model, AnimatorController controller)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        go.name = "CatModel";
        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        AssetImporter.GetAtPath(PrefabPath).assetBundleName = BundleName;
    }

    static void TagAudio()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
            AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)).assetBundleName = BundleName;
    }

    static void Report(GameObject model)
    {
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Debug.Log($"[BuildCat] mesh bounds {smr.sharedMesh.bounds} materials {string.Join(",", smr.sharedMaterials.Select(m => m ? m.name : "null"))}");
        var head = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Head");
        var tail = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Tail.3");
        if (head && tail) Debug.Log($"[BuildCat] head {head.position} tail {tail.position} (head should be +Z of tail)");
        Debug.Log($"[BuildCat] model root rotation {model.transform.rotation.eulerAngles} scale {model.transform.localScale}");
    }
}
