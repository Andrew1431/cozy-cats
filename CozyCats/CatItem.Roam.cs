using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CozyCats;

// Skittish cats: a cat nobody has picked up yet bolts from standing players and keeps its distance,
// but creeps up to anyone crouching. The host decides; every client walks the same navmesh path locally.
public partial class CatItem
{
    private enum Mood : byte { Calm, Alarmed, Coaxed }

    // Metres. 3-4.5 m is the 10-15 ft of personal space the cat keeps from standing players.
    private const float SpotRange = 8f, HearRange = 2.5f, PersonalSpace = 4.5f, FleeMin = 8f, FleeMax = 14f;
    private const float CoaxRange = 12f, CoaxStop = 1.0f, CalmDownAfter = 20f;
    // Faster than a walking player and close to a sprint: chasing works sometimes, crouching works every time.
    private const float WalkSpeed = 0.55f, RunSpeed = 7.5f;
    // Tamed cats trail whoever last put them down: walk when a little behind, run when left well behind.
    private const float FollowWalkSpeed = 0.9f, FollowRunSpeed = 6f;
    private const float FollowStop = 1.6f, FollowStart = 2.6f, FollowRunStart = 8f, FollowRunStop = 4f, FollowDoorRange = 12f, FollowCatchUp = 30f, FollowStuckTime = 4f;
    // Ground speed each clip covers at playback speed 1 for a size-1 cat, measured from the paw sweep in Blender.
    private const float WalkClipSpeed = 0.13f, RunClipSpeed = 1.0f;

    // Picked up at least once: it trusts people now and stays put.
    private readonly NetworkVariable<bool> netTamed = new NetworkVariable<bool>(false);

    private Mood mood = Mood.Calm;
    private float brainTimer, calmTimer, coaxRepathTimer;
    private NavMeshPath navPath;

    private PlayerControllerB followTarget;
    private bool followRunning;
    private float followRepath, lastFollowDist = float.MaxValue;
    private Vector3 lastFollowDest;
    private Vector3 stuckAnchor;
    private float stuckTimer;

    private Vector3[] path;
    private int pathIndex;
    private float moveSpeed;
    private byte gait;

    private bool IsMoving => path != null;

    private float AnimSpeedFor(byte g, float speed)
    {
        float natural = (g == PoseRun ? RunClipSpeed : WalkClipSpeed) * size * modelBaseScale.y;
        return Mathf.Clamp(speed / Mathf.Max(natural, 0.01f), 0.6f, g == PoseRun ? 1.5f : 2.5f);
    }

    // ---- Every client: follow the path the host sent ----

    private void UpdateMovement(float dt)
    {
        if (!IsMoving) return;
        if (isHeld || isHeldByEnemy)
        {
            path = null;
            return;
        }
        float step = moveSpeed * dt;
        Vector3 pos = transform.position;
        while (step > 0f && pathIndex < path.Length)
        {
            Vector3 target = path[pathIndex];
            float d = Vector3.Distance(pos, target);
            if (IsSteep(target - pos))
            {
                // Ladders and ledges are navmesh links: hop straight to the other end like a cat would.
                pos = target;
                pathIndex++;
                continue;
            }
            if (d <= step)
            {
                pos = target;
                step -= d;
                pathIndex++;
            }
            else
            {
                pos = Vector3.MoveTowards(pos, target, step);
                step = 0f;
            }
        }
        Vector3 flat = Vector3.ProjectOnPlane(pos - transform.position, Vector3.up);
        transform.position = pos;
        if (flat.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), dt * 10f);
        // Otherwise the item's own "rest on the floor" logic snaps it back to where it was dropped.
        targetFloorPosition = transform.localPosition;
        if (pathIndex >= path.Length) path = null;
    }

    private static bool IsSteep(Vector3 seg)
    {
        float rise = Mathf.Abs(seg.y), run = new Vector2(seg.x, seg.z).magnitude;
        return rise > 0.6f && rise > run * 1.2f;
    }

    [ClientRpc]
    private void MoveClientRpc(Vector3[] corners, float speed, byte newGait)
    {
        if (isHeld || corners == null || corners.Length < 2) return;
        // Dropped on the ship's deck but outside the room: it was riding the ship; stop riding it once it walks off.
        if (!isInShipRoom && transform.parent == StartOfRound.Instance.elevatorTransform)
        {
            transform.SetParent(StartOfRound.Instance.propsContainer, worldPositionStays: true);
            isInElevator = false;
        }
        // Corner 0 is the host's position; each client starts from wherever it has the cat, which is close enough.
        path = corners;
        pathIndex = 1;
        moveSpeed = speed;
        gait = newGait;
    }

    [ClientRpc]
    private void StopClientRpc(Vector3 position, float yaw)
    {
        path = null;
        if (isHeld) return;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        targetFloorPosition = transform.localPosition;
    }

    // ---- Host only: decide what the cat does ----

    private void UpdateBrain(float dt)
    {
        if (isHeld)
        {
            if (!netTamed.Value) netTamed.Value = true;
            if (playerHeldBy != null) followTarget = playerHeldBy;
            return;
        }
        if (isHeldByEnemy || netRescued.Value) return;
        if (isInShipRoom && !IsMoving) return;
        if (!reachedFloorTarget && !IsMoving) return;
        var sor = StartOfRound.Instance;
        if (sor == null || sor.inShipPhase || !sor.shipHasLanded) return;

        const float tick = 0.25f;
        brainTimer -= dt;
        if (brainTimer > 0f) return;
        brainTimer = tick;

        // Rescued (brought aboard) cats are home for good; following is only for taking a cat home.
        if (netTamed.Value)
        {
            Follow(tick);
            return;
        }

        Vector3 eye = transform.position + Vector3.up * 0.3f;
        PlayerControllerB threat = null, friend = null;
        float threatDist = float.MaxValue, friendDist = float.MaxValue;
        foreach (var p in sor.allPlayerScripts)
        {
            if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
            float d = Vector3.Distance(p.transform.position, transform.position);
            if (d > CoaxRange) continue;
            bool sees = !Physics.Linecast(eye, p.gameplayCamera.transform.position, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);
            if (!p.isCrouching)
            {
                if (((sees && d < SpotRange) || d < HearRange) && d < threatDist)
                {
                    threat = p;
                    threatDist = d;
                }
            }
            else if (sees && d < friendDist)
            {
                friend = p;
                friendDist = d;
            }
        }

        // Anyone standing wins over anyone crouching, so the cat never flip-flops between fleeing and approaching.
        if (threat != null)
        {
            calmTimer = CalmDownAfter;
            bool firstSighting = mood != Mood.Alarmed;
            mood = Mood.Alarmed;
            bool alreadyRunning = IsMoving && gait == PoseRun;
            if ((firstSighting || threatDist < PersonalSpace) && !alreadyRunning && !Flee())
                FaceAndStop(threat.transform.position, PoseSit);
            return;
        }

        if (friend != null)
        {
            mood = Mood.Coaxed;
            if (friendDist <= CoaxStop + 0.3f)
            {
                if (IsMoving) FaceAndStop(friend.transform.position, PoseSit);
                return;
            }
            coaxRepathTimer -= tick;
            if (!IsMoving || coaxRepathTimer <= 0f)
            {
                coaxRepathTimer = 1f;
                Vector3 toFriend = Vector3.ProjectOnPlane(friend.transform.position - transform.position, Vector3.up).normalized;
                Vector3 dest = friend.transform.position - toFriend * CoaxStop;
                if (!SendPath(dest, WalkSpeed, PoseWalk) && IsMoving) FaceAndStop(friend.transform.position, PoseSit);
            }
            return;
        }

        if (mood == Mood.Coaxed)
        {
            // The crouching player left or lost sight of us: stay put.
            mood = Mood.Calm;
            if (IsMoving) StopHere(PoseSit);
        }
        else if (mood == Mood.Alarmed && !IsMoving)
        {
            calmTimer -= tick;
            if (calmTimer <= 0f) mood = Mood.Calm;
        }
    }

    private void Follow(float tick)
    {
        var sor = StartOfRound.Instance;
        // Walked in on its own: settle into the ship exactly as if it had been dropped there.
        if (sor.shipInnerRoomBounds.bounds.Contains(transform.position))
        {
            followTarget = null;
            netPose.Value = PoseSit;
            ArrivedInShipClientRpc();
            SettleInShip();
            return;
        }

        var t = followTarget;
        if (t == null || !t.isPlayerControlled || t.isPlayerDead)
        {
            followTarget = null;
            if (IsMoving) StopHere(PoseSit);
            return;
        }
        if (t.isInHangarShipRoom && !t.isInsideFactory && !isInFactory)
        {
            BoardShip(t);
            return;
        }

        // They went through an entrance. If we were close behind, pop through with them; otherwise we've lost them.
        if (t.isInsideFactory != isInFactory)
        {
            // The main entrance flags the player as outside a moment before it moves them; the facility sits ~200 m
            // below the surface, so wait until they're actually far away or we'd hop to where they just were.
            if (lastFollowDist < FollowDoorRange)
            {
                if (Vector3.Distance(t.transform.position, transform.position) > 40f) HopTo(t, "entrance");
                return;
            }
            else
            {
                followTarget = null;
                if (IsMoving) StopHere(PoseSit);
            }
            return;
        }

        float d = Vector3.Distance(transform.position, t.transform.position);
        lastFollowDist = d;
        // Left far behind, or not getting anywhere (ladders, the ship's step, odd stairs): catch up instead of getting lost.
        if (d > FollowStart + 1f)
        {
            if (Vector3.Distance(transform.position, stuckAnchor) > 0.4f)
            {
                stuckAnchor = transform.position;
                stuckTimer = 0f;
            }
            else stuckTimer += tick;
        }
        else stuckTimer = 0f;
        if (d > FollowCatchUp || stuckTimer > FollowStuckTime)
        {
            HopTo(t, d > FollowCatchUp ? $"{d:0}m behind" : "stuck");
            stuckTimer = 0f;
            return;
        }
        if (!followRunning && d > FollowRunStart) followRunning = true;
        else if (followRunning && d < FollowRunStop) followRunning = false;

        if (d < FollowStop)
        {
            if (IsMoving) FaceAndStop(t.transform.position, PoseSit);
            return;
        }
        if (!IsMoving && d < FollowStart) return;

        byte g = followRunning ? PoseRun : PoseWalk;
        Vector3 dest = t.transform.position + FollowOffset(t);
        // The player may be mid-jump or standing on a crate: aim for the floor near them.
        if (NavMesh.SamplePosition(dest, out var floor, 3f, NavMesh.AllAreas)) dest = floor.position;
        followRepath -= tick;
        bool stale = followRepath <= 0f && Vector3.Distance(dest, lastFollowDest) > 0.75f;
        if (IsMoving && gait == g && !stale) return;
        followRepath = 0.5f;
        lastFollowDest = dest;
        // No route (the ship's floor, a gap) leaves the cat standing still, and the stuck timer hops it over.
        SendPath(dest, followRunning ? FollowRunSpeed : FollowWalkSpeed, g, maxLength: 80f);
    }

    // The ship's floor isn't on the navmesh, so boarding is scripted: hop in beside the player, then stroll to a spot.
    private void BoardShip(PlayerControllerB t)
    {
        var b = StartOfRound.Instance.shipInnerRoomBounds.bounds;
        HopTo(t, "boarding");
        if (!b.Contains(transform.position + Vector3.up * 0.1f))
        {
            // The spot behind them was outside the room (they're just through the door): land at their feet instead.
            TeleportClientRpc(t.transform.position, t.transform.eulerAngles.y + 180f, false);
        }
        followTarget = null;
        netPose.Value = PoseSit;
        ArrivedInShipClientRpc();
        SettleInShip();
    }

    // Walk a few metres in a straight line to a random clear patch of ship floor so cats don't crowd the door.
    private void SettleInShip()
    {
        var sor = StartOfRound.Instance;
        var b = sor.shipInnerRoomBounds.bounds;
        Vector3 from = transform.position;
        for (int i = 0; i < 10; i++)
        {
            var p = Vector3.Lerp(b.center, new Vector3(Random.Range(b.min.x, b.max.x), 0f, Random.Range(b.min.z, b.max.z)), 0.7f);
            p.y = from.y + 1f;
            if (!Physics.Raycast(p, Vector3.down, out var hit, 2f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.point.y - from.y) > 0.3f || Vector3.Distance(hit.point, from) < 1f) continue;
            if (Physics.Linecast(from + Vector3.up * 0.3f, hit.point + Vector3.up * 0.3f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
            netPose.Value = PoseSit;
            MoveClientRpc(new[] { from, hit.point }, FollowWalkSpeed, PoseWalk);
            return;
        }
    }

    // A spot behind the player, fanned out per cat so several followers don't stack on one point.
    private Vector3 FollowOffset(PlayerControllerB t)
    {
        int seed = netSeed.Value;
        Vector3 back = Vector3.ProjectOnPlane(-t.transform.forward, Vector3.up).normalized;
        float angle = (seed % 7 - 3) * 22f;
        float radius = 1.1f + seed % 3 * 0.35f;
        return Quaternion.Euler(0f, angle, 0f) * back * radius;
    }

    private void HopTo(PlayerControllerB t, string reason)
    {
        var sor = StartOfRound.Instance;
        Vector3 p = t.transform.position + FollowOffset(t) + Vector3.up * 0.5f;
        p = Physics.Raycast(p, Vector3.down, out var hit, 3f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)
            ? hit.point : t.transform.position;
        Vector3 look = Vector3.ProjectOnPlane(t.transform.position - p, Vector3.up);
        float yaw = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look).eulerAngles.y : transform.eulerAngles.y;
#if DEBUG
        Plugin.Log.LogInfo($"{CatName} hops to {t.playerUsername} ({reason}): from {transform.position} to {p}, player at {t.transform.position}");
#endif
        lastFollowDist = 0f;
        netPose.Value = PoseSit;
        TeleportClientRpc(p, yaw, t.isInsideFactory);
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 position, float yaw, bool inFactory)
    {
        path = null;
        if (isHeld) return;
        isInFactory = inFactory;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        targetFloorPosition = transform.localPosition;
    }

    [ClientRpc]
    private void ArrivedInShipClientRpc()
    {
        path = null;
        if (isHeld) return;
        // Same as the game's own drop-in-ship handling: ride with the ship, count as collected (and pay the bounty once).
        transform.SetParent(StartOfRound.Instance.elevatorTransform, worldPositionStays: true);
        targetFloorPosition = transform.localPosition;
        GameNetworkManager.Instance.localPlayerController?.SetItemInElevator(true, true, this);
    }

    // Run to a reachable spot away from every standing player. No leash: crouching is how you catch a cat.
    private bool Flee()
    {
        var sor = StartOfRound.Instance;
        Vector3 from = transform.position;
        Vector3 away = Vector3.zero;
        foreach (var p in sor.allPlayerScripts)
        {
            if (!IsStanding(p)) continue;
            Vector3 v = Vector3.ProjectOnPlane(from - p.transform.position, Vector3.up);
            if (v.magnitude < SpotRange * 1.5f) away += v.normalized / Mathf.Max(v.magnitude, 0.5f);
        }
        away = away.sqrMagnitude > 1e-6f ? away.normalized : transform.forward;

        // Last attempt ignores "away": a cornered cat will dart past whoever is chasing it.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            bool dartPast = attempt == 3;
            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            for (int i = 0; i < 14; i++)
            {
                Vector2 r = Random.insideUnitCircle * (0.9f + attempt * 0.6f);
                Vector3 dir = dartPast ? new Vector3(r.x, 0f, r.y).normalized : (away + new Vector3(r.x, 0f, r.y)).normalized;
                if (dir.sqrMagnitude < 1e-4f) continue;
                Vector3 cand = from + dir * Random.Range(FleeMin, FleeMax);
                if (!NavMesh.SamplePosition(cand, out var hit, 2f, NavMesh.AllAreas)) continue;
                float nearest = float.MaxValue;
                foreach (var p in sor.allPlayerScripts)
                    if (IsStanding(p)) nearest = Mathf.Min(nearest, Vector3.Distance(hit.position, p.transform.position));
                if (nearest > bestScore)
                {
                    bestScore = nearest;
                    best = hit.position;
                }
            }
            // A spot no further than our personal space means we're cornered: sit tight and let them catch us.
            if (bestScore > PersonalSpace && SendPath(best, RunSpeed, PoseRun)) return true;
        }
        return false;
    }

    private static bool IsStanding(PlayerControllerB p) => p != null && p.isPlayerControlled && !p.isPlayerDead && !p.isCrouching;

    private bool SendPath(Vector3 dest, float speed, byte newGait, float maxLength = FleeMax * 3f)
    {
        navPath ??= new NavMeshPath();
        if (!NavMesh.SamplePosition(transform.position, out var start, 1.5f, NavMesh.AllAreas)) return false;
        if (!NavMesh.CalculatePath(start.position, dest, NavMesh.AllAreas, navPath) || navPath.status != NavMeshPathStatus.PathComplete) return false;
        var corners = navPath.corners;
        if (corners.Length < 2) return false;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }
        if (length > maxLength) return false;
        corners[0] = transform.position;
        netPose.Value = PoseSit;
#if DEBUG
        Plugin.Log.LogInfo($"{CatName} {(newGait == PoseRun ? "flees" : "walks")} {length:0.0}m to {dest} ({corners.Length} corners)");
#endif
        MoveClientRpc(corners, speed, newGait);
        return true;
    }

    private void FaceAndStop(Vector3 lookAt, byte restPose)
    {
        Vector3 flat = Vector3.ProjectOnPlane(lookAt - transform.position, Vector3.up);
        float yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : transform.eulerAngles.y;
        netPose.Value = restPose;
        poseTimer = Random.Range(20f, 40f);
        StopClientRpc(transform.position, yaw);
    }

    private void StopHere(byte restPose)
    {
        netPose.Value = restPose;
        poseTimer = Random.Range(20f, 40f);
        StopClientRpc(transform.position, transform.eulerAngles.y);
    }
}
