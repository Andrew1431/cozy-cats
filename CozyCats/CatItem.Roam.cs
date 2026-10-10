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
    // Ground speed each clip covers at playback speed 1 for a size-1 cat, measured from the paw sweep in Blender.
    private const float WalkClipSpeed = 0.13f, RunClipSpeed = 1.0f;

    // Picked up at least once: it trusts people now and stays put.
    private readonly NetworkVariable<bool> netTamed = new NetworkVariable<bool>(false);

    private Mood mood = Mood.Calm;
    private float brainTimer, calmTimer, coaxRepathTimer;
    private NavMeshPath navPath;

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
        if (isHeld || isHeldByEnemy || isInShipRoom)
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

    [ClientRpc]
    private void MoveClientRpc(Vector3[] corners, float speed, byte newGait)
    {
        if (isHeld || corners == null || corners.Length < 2) return;
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
        if (isHeld && !netTamed.Value) netTamed.Value = true;
        if (netTamed.Value || netRescued.Value || isHeld || isHeldByEnemy || isInShipRoom || isInElevator) return;
        if (!reachedFloorTarget && !IsMoving) return;
        var sor = StartOfRound.Instance;
        if (sor == null || sor.inShipPhase || !sor.shipHasLanded) return;

        const float tick = 0.25f;
        brainTimer -= dt;
        if (brainTimer > 0f) return;
        brainTimer = tick;

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

    private bool SendPath(Vector3 dest, float speed, byte newGait)
    {
        navPath ??= new NavMeshPath();
        if (!NavMesh.SamplePosition(transform.position, out var start, 1.5f, NavMesh.AllAreas)) return false;
        if (!NavMesh.CalculatePath(start.position, dest, NavMesh.AllAreas, navPath) || navPath.status != NavMeshPathStatus.PathComplete) return false;
        var corners = navPath.corners;
        if (corners.Length < 2) return false;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 seg = corners[i] - corners[i - 1];
            float rise = Mathf.Abs(seg.y), run = new Vector2(seg.x, seg.z).magnitude;
            // Steeper than any stairs means a jump/drop link between floors; the cat would glide through the floor.
            if (rise > 0.6f && rise > run * 1.2f) return false;
            length += seg.magnitude;
        }
        if (length > FleeMax * 3f) return false;
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
