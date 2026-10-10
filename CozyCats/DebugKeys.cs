#if DEBUG
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CozyCats;

// Debug builds only. Lives on its own GameObject because LC destroys the BepInEx manager object.
internal class DebugKeys : MonoBehaviour
{
    private static readonly Quaternion DefaultHeldRot = CatItem.HeldRot;
    private static readonly Vector3 DefaultHeldShift = CatItem.HeldShift;

    public static void Install()
    {
        var go = new GameObject("CozyCats.DebugKeys") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<DebugKeys>();
        Plugin.Log.LogWarning("Debug keys: F4 bring everyone to you, F5 toggle god mode + infinite stamina (on by default), F6 teleport to nearest cat, F7 toggle cat finder, F8 spawn cat (host). Not holding a cat: U/J smallest size up/down, I/K largest size up/down. While holding a cat: arrows move left/right/up/down, " +
                              "PgUp/PgDn move away/closer, I/K pitch, J/L yaw, U/O roll, hold Alt for fine steps, " +
                              "F9 print+copy hold pose, F10 reset hold pose.");
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f8Key.wasPressedThisFrame) SpawnCatInFront();
        if (kb.f5Key.wasPressedThisFrame)
        {
            godMode = !godMode;
            HUDManager.Instance?.DisplayTip("God mode", godMode ? "On" : "Off");
        }
        // The game's own switch: DamagePlayer and KillPlayer both check it before hurting the local player.
        if (StartOfRound.Instance != null) StartOfRound.Instance.allowLocalPlayerDeath = !godMode;
        var me = GameNetworkManager.Instance?.localPlayerController;
        if (godMode && me != null)
        {
            me.sprintMeter = 1f;
            me.isExhausted = false;
        }
        if (kb.f7Key.wasPressedThisFrame)
        {
            showMarkers = !showMarkers;
            HUDManager.Instance?.DisplayTip("Cat finder", showMarkers ? "On" : "Off");
        }
        if (kb.f6Key.wasPressedThisFrame) StartCoroutine(TeleportToNearestCat());
        RegisterSummon();
        if (kb.f4Key.wasPressedThisFrame) RequestSummon();
        if (showMarkers && Time.unscaledTime >= nextCatScan)
        {
            nextCatScan = Time.unscaledTime + 0.5f;
            cats = FindObjectsOfType<CatItem>();
        }

        var player = GameNetworkManager.Instance?.localPlayerController;
        if (player == null || player.isTypingChat || player.inTerminalMenu) return;
        if (!(player.currentlyHeldObjectServer is CatItem cat))
        {
            SizeKeys(kb);
            return;
        }

        bool fine = kb.altKey.isPressed;
        float step = fine ? 0.0025f : 0.01f;
        float deg = fine ? 1f : 5f;
        var cam = player.gameplayCamera.transform;

        // Camera-relative input, converted to the cat's prefab space so the result is a fixed hold pose.
        Vector3 move = Vector3.zero;
        if (kb.rightArrowKey.wasPressedThisFrame) move += cam.right;
        if (kb.leftArrowKey.wasPressedThisFrame) move -= cam.right;
        if (kb.upArrowKey.wasPressedThisFrame) move += cam.up;
        if (kb.downArrowKey.wasPressedThisFrame) move -= cam.up;
        if (kb.pageUpKey.wasPressedThisFrame) move += cam.forward;
        if (kb.pageDownKey.wasPressedThisFrame) move -= cam.forward;

        Quaternion rot = Quaternion.identity;
        if (kb.iKey.wasPressedThisFrame) rot = Quaternion.AngleAxis(-deg, Local(cat, cam.right)) * rot;
        if (kb.kKey.wasPressedThisFrame) rot = Quaternion.AngleAxis(deg, Local(cat, cam.right)) * rot;
        if (kb.jKey.wasPressedThisFrame) rot = Quaternion.AngleAxis(-deg, Local(cat, cam.up)) * rot;
        if (kb.lKey.wasPressedThisFrame) rot = Quaternion.AngleAxis(deg, Local(cat, cam.up)) * rot;
        if (kb.uKey.wasPressedThisFrame) rot = Quaternion.AngleAxis(deg, Local(cat, cam.forward)) * rot;
        if (kb.oKey.wasPressedThisFrame) rot = Quaternion.AngleAxis(-deg, Local(cat, cam.forward)) * rot;

        bool changed = false;
        if (move != Vector3.zero)
        {
            CatItem.HeldShift += Local(cat, move) * step;
            changed = true;
        }
        if (rot != Quaternion.identity)
        {
            // HeldRot already pivots about the body centre, so the cat spins in place.
            CatItem.HeldRot = rot * CatItem.HeldRot;
            changed = true;
        }
        if (kb.f10Key.wasPressedThisFrame)
        {
            CatItem.HeldRot = DefaultHeldRot;
            CatItem.HeldShift = DefaultHeldShift;
            changed = true;
        }
        if (changed) Show(false);
        if (kb.f9Key.wasPressedThisFrame) Show(true);
    }

    // ---- Cat finder: F7 markers + laser lines through walls, F6 teleport to the nearest cat ----

    private bool showMarkers;
    private bool godMode = true;
    private float nextCatScan;
    private CatItem[] cats = new CatItem[0];
    private static Texture2D pixel;
    private static GUIStyle labelStyle;

    private void OnGUI()
    {
        if (!showMarkers) return;
        var player = GameNetworkManager.Instance?.localPlayerController;
        if (player == null || player.gameplayCamera == null) return;

        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        var cam = player.gameplayCamera;
        var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
        foreach (var cat in cats)
        {
            if (cat == null || cat.isHeld) continue;
            Vector3 pos = cat.transform.position + Vector3.up * 0.3f;
            Vector3 vp = cam.WorldToViewportPoint(pos);
            bool behind = vp.z < 0f;
            var screen = new Vector2(vp.x * Screen.width, (1f - vp.y) * Screen.height);
            if (behind) screen = centre - (screen - centre);

            // Clamp off-screen targets to the edge so the laser still points the right way.
            Vector2 dir = screen - centre;
            float margin = 40f;
            float sx = dir.x == 0 ? float.MaxValue : (Screen.width / 2f - margin) / Mathf.Abs(dir.x);
            float sy = dir.y == 0 ? float.MaxValue : (Screen.height / 2f - margin) / Mathf.Abs(dir.y);
            float s = Mathf.Min(1f, sx, sy);
            if (behind) s = Mathf.Min(sx, sy);
            Vector2 tip = centre + dir * s;

            Color c = cat.isInShipRoom ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.55f, 0.1f);
            DrawLine(centre, tip, c, 3f);

            float dist = Vector3.Distance(player.transform.position, cat.transform.position);
            float dy = cat.transform.position.y - player.transform.position.y;
            string height = Mathf.Abs(dy) < 2f ? "" : dy > 0 ? $" ▲{dy:0}m" : $" ▼{-dy:0}m";
            string where = cat.isInFactory != player.isInsideFactory ? (cat.isInFactory ? " (inside)" : " (outside)") : "";
            GUI.color = c;
            GUI.Label(new Rect(tip.x - 150f, tip.y - 32f, 300f, 24f), $"{cat.CatName} {dist:0}m{height}{where}", labelStyle);
            GUI.DrawTexture(new Rect(tip.x - 5f, tip.y - 5f, 10f, 10f), pixel);
            GUI.color = Color.white;
        }
    }

    private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        var m = GUI.matrix;
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, (b - a).magnitude, width), pixel);
        GUI.matrix = m;
        GUI.color = Color.white;
    }

    private System.Collections.IEnumerator TeleportToNearestCat()
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        if (player == null) yield break;
        CatItem nearest = null;
        float best = float.MaxValue;
        var all = FindObjectsOfType<CatItem>();
        foreach (var cat in all)
        {
            Plugin.Log.LogInfo($"F6: {cat.CatName} at {cat.transform.position} held={cat.isHeld} inShip={cat.isInShipRoom} inFactory={cat.isInFactory}");
            if (cat.isHeld || cat.isInShipRoom) continue;
            float d = Vector3.Distance(player.transform.position, cat.transform.position);
            if (d < best) { best = d; nearest = cat; }
        }
        if (nearest == null)
        {
            HUDManager.Instance?.DisplayTip("Cat finder", $"No loose cats on this moon ({all.Length} cats total).");
            yield break;
        }

        // Go through the real main entrance first so the facility's lighting, audio and culling switch over.
        if (nearest.isInFactory && !player.isInsideFactory)
        {
            foreach (var door in FindObjectsOfType<EntranceTeleport>())
            {
                if (door.isEntranceToBuilding && door.entranceId == 0)
                {
                    door.TeleportPlayer();
                    break;
                }
            }
            yield return null;
            yield return null;
        }

        // Random spot around the cat; if it lands you in a wall, just press F6 again.
        Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.5f, 3f);
        Vector3 dest = nearest.transform.position + new Vector3(r.x, 0.2f, r.y);
        if (UnityEngine.AI.NavMesh.SamplePosition(dest, out var hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) dest = hit.position + Vector3.up * 0.2f;
        player.TeleportPlayer(dest);
        Plugin.Log.LogInfo($"Teleported to {nearest.CatName} at {nearest.transform.position}");
    }

    // Not holding a cat: U/J raise/lower the smallest size, I/K the largest. This PC only; every cat rescales live.
    private static void SizeKeys(Keyboard kb)
    {
        float min = CatLooks.SizeMin, max = CatLooks.SizeMax;
        if (kb.uKey.wasPressedThisFrame) min += 0.1f;
        if (kb.jKey.wasPressedThisFrame) min -= 0.1f;
        if (kb.iKey.wasPressedThisFrame) max += 0.1f;
        if (kb.kKey.wasPressedThisFrame) max -= 0.1f;
        if (min == CatLooks.SizeMin && max == CatLooks.SizeMax) return;
        CatLooks.SizeMin = Mathf.Clamp(min, 0.1f, 5f);
        CatLooks.SizeMax = Mathf.Clamp(max, 0.1f, 5f);
        foreach (var c in FindObjectsOfType<CatItem>()) c.ApplySize();
        string text = $"Smallest {CatLooks.SizeMin:0.00}, largest {CatLooks.SizeMax:0.00}";
        HUDManager.Instance?.DisplayTip("Cat sizes", text);
        Plugin.Log.LogInfo($"Cat sizes: {text}");
    }

    // ---- F4: bring every player to whoever pressed it (client -> host -> everyone) ----

    private const string SummonRequestMsg = "CozyCats.SummonRequest", SummonMsg = "CozyCats.Summon";
    private CustomMessagingManager summonRegisteredOn;

    // The messaging manager is new for every hosted/joined session, so register again whenever it changes.
    private void RegisterSummon()
    {
        var cmm = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.CustomMessagingManager : null;
        if (cmm == null || cmm == summonRegisteredOn) return;
        summonRegisteredOn = cmm;
        cmm.RegisterNamedMessageHandler(SummonRequestMsg, (sender, reader) =>
        {
            if (!NetworkManager.Singleton.IsServer) return;
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out bool inFactory);
            BroadcastSummon(sender, pos, inFactory);
        });
        cmm.RegisterNamedMessageHandler(SummonMsg, (_, reader) =>
        {
            reader.ReadValueSafe(out ulong summoner);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out bool inFactory);
            OnSummoned(summoner, pos, inFactory);
        });
    }

    private void RequestSummon()
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        var nm = NetworkManager.Singleton;
        if (player == null || nm == null || !nm.IsListening) return;
        if (nm.IsServer)
        {
            BroadcastSummon(nm.LocalClientId, player.transform.position, player.isInsideFactory);
            return;
        }
        using var w = new FastBufferWriter(32, Allocator.Temp);
        w.WriteValueSafe(player.transform.position);
        w.WriteValueSafe(player.isInsideFactory);
        nm.CustomMessagingManager.SendNamedMessage(SummonRequestMsg, NetworkManager.ServerClientId, w);
    }

    private void BroadcastSummon(ulong summoner, Vector3 pos, bool inFactory)
    {
        var nm = NetworkManager.Singleton;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (id == nm.LocalClientId) continue;
            using var w = new FastBufferWriter(48, Allocator.Temp);
            w.WriteValueSafe(summoner);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(inFactory);
            nm.CustomMessagingManager.SendNamedMessage(SummonMsg, id, w);
        }
        OnSummoned(summoner, pos, inFactory);
    }

    private void OnSummoned(ulong summoner, Vector3 pos, bool inFactory)
    {
        if (summoner == NetworkManager.Singleton.LocalClientId)
        {
            HUDManager.Instance?.DisplayTip("Debug", "Bringing everyone to you");
            return;
        }
        StartCoroutine(TeleportTo(pos, inFactory));
    }

    private System.Collections.IEnumerator TeleportTo(Vector3 pos, bool inFactory)
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        if (player == null || player.isPlayerDead) yield break;
        // Use the real main entrance to swap inside/outside so lighting, audio and culling follow.
        if (inFactory != player.isInsideFactory)
        {
            foreach (var door in FindObjectsOfType<EntranceTeleport>())
            {
                if (door.isEntranceToBuilding != player.isInsideFactory && door.entranceId == 0)
                {
                    door.TeleportPlayer();
                    break;
                }
            }
            yield return null;
            yield return null;
        }
        Vector2 r = Random.insideUnitCircle.normalized * 1.2f;
        player.TeleportPlayer(pos + new Vector3(r.x, 0.2f, r.y));
    }

    private static Vector3 Local(CatItem cat, Vector3 worldDir) => cat.transform.InverseTransformDirection(worldDir).normalized;

    private static void Show(bool copy)
    {
        Vector3 e = CatItem.HeldRot.eulerAngles;
        Vector3 s = CatItem.HeldShift;
        string code = $"HeldRot = Quaternion.Euler({e.x:0.#}f, {e.y:0.#}f, {e.z:0.#}f); HeldShift = new Vector3({s.x:0.###}f, {s.y:0.###}f, {s.z:0.###}f);";
        Plugin.Log.LogInfo("Hold pose: " + code);
        if (copy) GUIUtility.systemCopyBuffer = code;
        HUDManager.Instance?.DisplayTip(copy ? "Hold pose copied" : "Cat hold",
            $"rot ({e.x:0},{e.y:0},{e.z:0})  shift ({s.x:0.00},{s.y:0.00},{s.z:0.00})");
    }

    private static void SpawnCatInFront()
    {
        var player = GameNetworkManager.Instance?.localPlayerController;
        var sor = StartOfRound.Instance;
        if (player == null || sor == null || CatRegistry.Prefab == null) return;
        if (!NetworkManager.Singleton.IsServer)
        {
            HUDManager.Instance.DisplayTip("CozyCats", "Only the host can spawn debug cats.");
            return;
        }

        var cam = player.gameplayCamera.transform;
        Vector3 flatFwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 pos = player.transform.position + flatFwd * 1.5f + Vector3.up * 0.5f;

        bool inShip = player.isInHangarShipRoom || player.isInElevator;
        Transform parent = inShip ? sor.elevatorTransform : RoundManager.Instance?.spawnedScrapContainer;
        var go = Instantiate(CatRegistry.Prefab, pos, Quaternion.LookRotation(-flatFwd), parent);
        var cat = go.GetComponent<GrabbableObject>();
        cat.fallTime = 0f;
        cat.isInElevator = inShip;
        cat.isInShipRoom = inShip;
        cat.isInFactory = player.isInsideFactory;
        go.GetComponent<NetworkObject>().Spawn();
        Plugin.Log.LogInfo($"Debug cat spawned at {pos} (inShip={inShip})");
    }
}
#endif
