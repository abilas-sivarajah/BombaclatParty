using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bombaclat.Tests
{
    /// <summary>
    /// Renders preview shots of the board to Logs/Screenshots (camera only, no IMGUI).
    /// Explicit: run with --filter Bombaclat.Tests.ScreenshotCapture.
    /// </summary>
    [Explicit]
    public class ScreenshotCapture
    {
        [UnityTest]
        public IEnumerator CaptureBoard()
        {
            var dir = Path.Combine(Application.dataPath, "..", "Logs", "Screenshots");
            Directory.CreateDirectory(dir);

            SceneManager.LoadScene("Game");
            yield return new WaitForSeconds(1.5f);
            Save(Path.Combine(dir, "01_menu_overview.png"));

            ConnectionManager.Instance.HostLocal();
            float end = Time.realtimeSinceStartup + 5f;
            while ((BoardGameManager.Instance == null || BoardGameManager.Instance.Players.Count == 0) && Time.realtimeSinceStartup < end)
                yield return null;

            var gm = BoardGameManager.Instance;
            gm.StartGameRpc(5);
            yield return new WaitForSeconds(2f);
            Save(Path.Combine(dir, "02_turn_start.png"));

            gm.RollDiceRpc();
            yield return new WaitForSeconds(0.7f);
            Save(Path.Combine(dir, "03_rolling.png"));
            yield return new WaitForSeconds(1.9f);
            Save(Path.Combine(dir, "04_moving.png"));

            var crownCam = new GameObject("CrownCam").AddComponent<Camera>();
            crownCam.CopyFrom(Camera.main);
            var crown = BoardLayout.Top(gm.CrownTile.Value);
            crownCam.transform.position = crown + new Vector3(0f, 4.5f, -5.5f);
            crownCam.transform.LookAt(crown + Vector3.up * 1.2f);
            Save(Path.Combine(dir, "05_crown.png"), crownCam);
            Object.Destroy(crownCam.gameObject);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.Shutdown();
                yield return null;
                Object.Destroy(nm.gameObject);
            }
            yield return null;
        }

        static void Save(string path, Camera cam = null)
        {
            cam ??= Camera.main;
            var rt = new RenderTexture(1600, 900, 24);
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render(); // batch mode never renders on its own; the first frame warms up post-processing LUTs
            cam.Render();
            cam.targetTexture = previous;

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Debug.Log($"[ScreenshotCapture] {path}");
        }
    }
}
