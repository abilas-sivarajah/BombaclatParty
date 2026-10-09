using System;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bombaclat.Editor
{
    /// <summary>
    /// Generates the Game scene from code so it can be rebuilt any time
    /// (menu: Bombaclat > Build Game Scene, or headless via BuildBatch).
    /// </summary>
    public static class GameSceneBuilder
    {
        const string Root = "Assets/_Bombaclat";
        const string ScenePath = Root + "/Scenes/Game.unity";
        const string MaterialPath = Root + "/Materials/BombaclatLit.mat";
        const string VolumePath = Root + "/Settings/BombaclatVolume.asset";

        static readonly Color Sky = new(0.56f, 0.80f, 0.97f);

        [MenuItem("Bombaclat/Build Game Scene")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Entry point for -executeMethod in batch mode.</summary>
        public static void BuildBatch()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Build()
        {
            foreach (var folder in new[] { "Scenes", "Materials", "Settings" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}")) AssetDatabase.CreateFolder(Root, folder);

            var material = CreateOrReplace(MaterialPath, () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetFloat("_Smoothness", 0.12f);
                return m;
            });
            var volumeProfile = CreateVolumeProfile();

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 90f;
                EditorUtility.SetDirty(urp);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting: warm sun, soft three-color ambient, sky-colored fog so the sea fades out.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(50f, -32f, 0f);

            RenderSettings.skybox = null;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.78f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.64f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.34f, 0.30f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 180f;

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = volumeProfile;

            // Main camera driven by Cinemachine.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sky;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1.3f);

            var rig = new GameObject("Camera Rig");
            var pivot = new GameObject("Orbit Pivot").transform;
            pivot.SetParent(rig.transform, false);

            var overview = new GameObject("CM Overview").AddComponent<CinemachineCamera>();
            overview.transform.SetParent(pivot, false);
            overview.transform.localPosition = new Vector3(0f, 30f, -38f);
            overview.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1f, 0f) - overview.transform.localPosition);
            overview.Lens.FieldOfView = 40f;
            overview.Lens.FarClipPlane = 400f;
            overview.Priority = 10;

            var follow = new GameObject("CM Follow").AddComponent<CinemachineCamera>();
            follow.transform.SetParent(rig.transform, false);
            follow.transform.position = new Vector3(0f, 8f, -26f);
            follow.Lens.FieldOfView = 42f;
            follow.Lens.FarClipPlane = 400f;
            follow.Priority = 0;
            var body = follow.gameObject.AddComponent<CinemachineFollow>();
            body.FollowOffset = new Vector3(0f, 7.5f, -8.5f);
            var tracking = body.TrackerSettings;
            tracking.BindingMode = BindingMode.WorldSpace;
            tracking.PositionDamping = new Vector3(0.7f, 0.7f, 0.7f);
            body.TrackerSettings = tracking;
            follow.gameObject.AddComponent<CinemachineRotationComposer>();

            var boardCamera = rig.AddComponent<BoardCamera>();
            SetFields(boardCamera, ("m_OrbitPivot", pivot), ("m_Overview", overview), ("m_Follow", follow));

            // The island + tiles are generated at runtime from BoardLayout.
            var board = new GameObject("Board").AddComponent<BoardView>();
            SetFields(board, ("m_BaseMaterial", material));

            // Networking.
            var networkGo = new GameObject("NetworkManager");
            var transport = networkGo.AddComponent<UnityTransport>();
            var networkManager = networkGo.AddComponent<NetworkManager>();
            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkGo.AddComponent<ConnectionManager>();

            var gameGo = new GameObject("BoardGameManager");
            gameGo.AddComponent<NetworkObject>();
            gameGo.AddComponent<BoardGameManager>();

            new GameObject("HUD").AddComponent<GameHud>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            // Re-open so in-scene NetworkObjects compute their GlobalObjectIdHash from the saved
            // scene, then save again.
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameSceneBuilder] Built {ScenePath}");
        }

        static VolumeProfile CreateVolumeProfile()
        {
            var profile = CreateOrReplace(VolumePath, ScriptableObject.CreateInstance<VolumeProfile>);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.45f);
            bloom.threshold.Override(1.05f);

            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(18f);
            color.contrast.Override(8f);
            color.postExposure.Override(0.1f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);

            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T CreateOrReplace<T>(string path, Func<T> create) where T : UnityEngine.Object
        {
            AssetDatabase.DeleteAsset(path);
            var asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void SetFields(UnityEngine.Object target, params (string name, UnityEngine.Object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                var property = so.FindProperty(name) ?? throw new InvalidOperationException($"{target.GetType().Name}.{name} not found");
                property.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
