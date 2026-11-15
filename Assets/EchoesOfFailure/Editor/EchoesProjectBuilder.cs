using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoesOfFailure.Editor
{
    [InitializeOnLoad]
    public static class EchoesProjectBuilder
    {
        private const string ScenePath = "Assets/EchoesOfFailure/Scenes/EchoesMain.unity";

        static EchoesProjectBuilder()
        {
            EditorApplication.delayCall += EnsureProject;
        }

        private static void EnsureProject()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureProject;
                return;
            }

            if (!File.Exists(ScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject root = new GameObject("ECHOES OF FAILURE");
                root.AddComponent<EchoesGame>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                Selection.activeGameObject = root;
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "Bounce Back Hackathon";
            PlayerSettings.productName = "Echoes of Failure";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.bounceback.echoesoffailure");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.WebGL, "com.bounceback.echoesoffailure.web");
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                SceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Debug.Log("Echoes of Failure project is ready. Press Play to test the complete course.");
        }

        private static void ConfigureWebGl()
        {
            PlayerSettings.WebGL.template = "PROJECT:EchoesItch";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
        }

        [MenuItem("Echoes of Failure/Build macOS App")]
        private static void BuildMacOsApp()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.Combine(projectRoot, "Builds", "macOS");
            string appPath = Path.Combine(outputDirectory, "Echoes of Failure.app");
            Directory.CreateDirectory(outputDirectory);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = appPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("Echoes of Failure macOS build completed: " + appPath);
            }
            else
            {
                Debug.LogError("Echoes of Failure macOS build failed: " + report.summary.result);
            }
        }

        [MenuItem("Echoes of Failure/Build itch.io WebGL")]
        private static void BuildItchWebGl()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.Combine(projectRoot, "Builds", "WebGL");
            Directory.CreateDirectory(outputDirectory);
            ConfigureWebGl();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("Echoes of Failure itch.io WebGL build completed: " + outputDirectory);
            }
            else
            {
                Debug.LogError("Echoes of Failure itch.io WebGL build failed: " + report.summary.result);
            }
        }
    }
}
