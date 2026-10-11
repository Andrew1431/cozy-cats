#if DEBUG
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CozyCats;

// F2: a coloured ball on every furniture nap spot, for tuning where cats curl up. The balls are the real spots,
// so F3 sends cats to wherever they've been moved. F9 copies the lot as code for FurniturePerch.Defs.
internal static class PerchEditor
{
    private class Ball
    {
        public FurniturePerch Host;
        public FurniturePerch.TunedSpot Spot;
        public GameObject Go, Nose;
    }

    public static bool On { get; private set; }
    private static readonly List<Ball> balls = new List<Ball>();
    private static int selected;
    private static Shader unlit;

    // Returns true while the editor owns the keyboard.
    public static bool Tick(Keyboard kb, PlayerControllerB player)
    {
        if (kb.f2Key.wasPressedThisFrame)
        {
            On = !On;
            if (!On) Clear();
            HUDManager.Instance?.DisplayTip("Nap spot editor", On
                ? "[ ] pick spot, G snap to crosshair, arrows/PgUp/PgDn nudge, , . turn, Alt fine, F9 copy"
                : "Off");
        }
        if (!On) return false;
        Sync();
        if (balls.Count == 0) return true;
        selected = (selected + balls.Count) % balls.Count;

        bool picked = false;
        if (kb.rightBracketKey.wasPressedThisFrame) { selected = (selected + 1) % balls.Count; picked = true; }
        if (kb.leftBracketKey.wasPressedThisFrame) { selected = (selected + balls.Count - 1) % balls.Count; picked = true; }
        var ball = balls[selected];
        var spot = ball.Spot;

        bool fine = kb.altKey.isPressed;
        float step = fine ? 0.01f : 0.05f, deg = fine ? 5f : 15f;
        var cam = player.gameplayCamera.transform;
        Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 move = Vector3.zero;
        if (kb.rightArrowKey.wasPressedThisFrame) move += right;
        if (kb.leftArrowKey.wasPressedThisFrame) move -= right;
        if (kb.upArrowKey.wasPressedThisFrame) move += fwd;
        if (kb.downArrowKey.wasPressedThisFrame) move -= fwd;
        if (kb.pageUpKey.wasPressedThisFrame) move += Vector3.up;
        if (kb.pageDownKey.wasPressedThisFrame) move -= Vector3.up;

        bool changed = false;
        if (move != Vector3.zero)
        {
            ball.Host.SetWorld(spot, spot.Point.position + move * step);
            changed = true;
        }
        if (kb.gKey.wasPressedThisFrame && Physics.Raycast(cam.position, cam.forward, out var hit, 8f,
                StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
        {
            ball.Host.SetWorld(spot, hit.point);
            changed = true;
        }
        float turn = (kb.periodKey.wasPressedThisFrame ? deg : 0f) - (kb.commaKey.wasPressedThisFrame ? deg : 0f);
        if (turn != 0f)
        {
            spot.Yaw = Mathf.Repeat(spot.Yaw + turn, 360f);
            ball.Host.Apply(spot);
            changed = true;
        }
        if (picked || changed)
            HUDManager.Instance?.DisplayTip($"{ball.Host.Label}: {spot.Name}", Code(spot));
        if (changed) Plugin.Log.LogInfo($"Nap spot {ball.Host.Label} {spot.Name}: {Code(spot)}");
        if (kb.f9Key.wasPressedThisFrame) CopyAll();

        foreach (var b in balls)
        {
            float size = b == ball ? 0.16f + Mathf.PingPong(Time.time * 0.2f, 0.06f) : 0.12f;
            b.Go.transform.localScale = Vector3.one * size;
        }
        return true;
    }

    // One ball per spot on furniture that's out; furniture put away or destroyed loses its balls.
    private static void Sync()
    {
        balls.RemoveAll(b =>
        {
            if (b.Host != null && b.Host.IsOut) return false;
            if (b.Go != null) Object.Destroy(b.Go);
            if (b.Nose != null) Object.Destroy(b.Nose);
            return true;
        });
        foreach (var host in PerchHost.All.OfType<FurniturePerch>().Where(h => h.IsOut))
            foreach (var s in host.Tuned)
                if (balls.All(b => b.Spot != s)) balls.Add(Make(host, s));
    }

    private static Ball Make(FurniturePerch host, FurniturePerch.TunedSpot s)
    {
        unlit ??= Shader.Find("HDRP/Unlit");
        var mat = new Material(unlit);
        mat.SetColor("_UnlitColor", s.Color);
        var go = Sphere(mat);
        go.name = "CozyPerchBall";
        go.transform.SetParent(s.Point, false);
        // A small ball out front shows which way the cat will face.
        var nose = Sphere(mat);
        nose.transform.SetParent(s.Point, false);
        nose.transform.localPosition = Vector3.down * 0.22f;
        nose.transform.localScale = Vector3.one * 0.06f;
        return new Ball { Host = host, Spot = s, Go = go, Nose = nose };
    }

    private static GameObject Sphere(Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static void Clear()
    {
        foreach (var b in balls)
        {
            if (b.Go != null) Object.Destroy(b.Go);
            if (b.Nose != null) Object.Destroy(b.Nose);
        }
        balls.Clear();
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "f";

    private static string Code(FurniturePerch.TunedSpot s) =>
        $"Local = new Vector3({F(s.Local.x)}, {F(s.Local.y)}, {F(s.Local.z)}), Yaw = {F(s.Yaw)}";

    private static void CopyAll()
    {
        var lines = balls.Select(b => $"{b.Host.Label} / {b.Spot.Name}: {Code(b.Spot)}").ToList();
        string text = string.Join("\n", lines);
        GUIUtility.systemCopyBuffer = text;
        Plugin.Log.LogInfo("Nap spots:\n" + text);
        HUDManager.Instance?.DisplayTip("Nap spots copied", $"{lines.Count} spots, also in the log");
    }

    public static void OnGUI(Camera cam, GUIStyle style)
    {
        if (!On || cam == null) return;
        for (int i = 0; i < balls.Count; i++)
        {
            var b = balls[i];
            if (b.Spot.Point == null) continue;
            Vector3 vp = cam.WorldToViewportPoint(b.Spot.Point.position + Vector3.up * 0.25f);
            if (vp.z < 0f) continue;
            var p = new Vector2(vp.x * Screen.width, (1f - vp.y) * Screen.height);
            GUI.color = b.Spot.Color;
            GUI.Label(new Rect(p.x - 150f, p.y - 12f, 300f, 24f), (i == selected ? "> " : "") + $"{b.Host.Label} {b.Spot.Name}", style);
        }
        GUI.color = Color.white;
    }
}
#endif
