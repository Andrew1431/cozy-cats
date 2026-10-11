using Unity.Netcode;
using UnityEngine;

namespace CozyCats;

// Life aboard: rescued cats potter about the ship, and if there's a cat tree they wander over now and then and nap on it.
// The ship moves (landing, taking off), so everything here is sent in the ship's local space.
public partial class CatItem
{
    private const float TreeChance = 0.45f, ShipWanderMin = 1.5f, ShipWanderMax = 5f;

    // Host only.
    private CatTree.Spot treeSpot, headingFor;
    private float shipTimer;
    private bool shipTimerSet;

    internal bool IsOnSpot(CatTree.Spot s) => s != null && (transform.position - s.Point.position).sqrMagnitude < 0.2f * 0.2f;

    // Sitting on it, or on the way over to it.
    internal bool Claims(CatTree.Spot s) => (treeSpot == s && IsOnSpot(s)) || headingFor == s;

    private bool InTree => treeSpot != null;

    internal static bool TryParsePose(string clip, out byte pose)
    {
        switch (clip)
        {
            case "Idle": pose = PoseIdle; return true;
            case "Sit": pose = PoseSit; return true;
            case "Loaf": pose = PoseLoaf; return true;
            case "Curl": pose = PoseCurl; return true;
            case "Drape": pose = PoseDrape; return true;
            default: pose = 0; return false;
        }
    }

    private void ShipLife(float dt)
    {
        if (!shipTimerSet)
        {
            shipTimerSet = true;
            shipTimer = Random.Range(10f, 40f);
        }
        shipTimer -= dt;
        brainTimer -= dt;
        if (brainTimer > 0f) return;
        brainTimer = 0.5f;

        var tree = CatTree.Instance;
        bool treeOut = tree != null && tree.IsOut;

        if (treeSpot != null)
        {
            // The tree was moved or packed away under us: hop down.
            if (!treeOut || !IsOnSpot(treeSpot))
            {
                LeaveTree();
                if (treeOut) HopOffTree(tree);
                else DropToFloor();
            }
            else if (shipTimer <= 0f) HopOffTree(tree);
            return;
        }
        if (IsMoving) return;

        if (headingFor != null)
        {
            var spot = headingFor;
            headingFor = null;
            if (treeOut && tree.Spots.Contains(spot) && Vector3.Distance(transform.position, spot.Point.position) < 3f)
                ClimbInto(spot);
            return;
        }
        if (!reachedFloorTarget) return;

        // Up on the tree but not on a spot (loaded from a save, or dropped there): settle onto one, or get down.
        if (treeOut && transform.position.y > tree.transform.position.y + 0.35f
            && Vector3.ProjectOnPlane(transform.position - tree.transform.position, Vector3.up).magnitude < 1.5f)
        {
            var near = tree.Spots.Find(s => (s.Point.position - transform.position).sqrMagnitude < 0.4f * 0.4f
                                            && (s.Occupant == null || s.Occupant == this || !s.Occupant.Claims(s)));
            if (near != null) ClimbInto(near);
            else HopOffTree(tree);
            return;
        }

        if (shipTimer > 0f) return;
        shipTimer = Random.Range(25f, 60f);
#if DEBUG
        Plugin.Log.LogInfo($"{CatName} gets up (tree {(treeOut ? "out" : "not out")}), next in {shipTimer:0}s");
#endif
        if (treeOut && Random.value < TreeChance && GoToTree(tree)) return;
        Wander();
    }

    private bool GoToTree(CatTree tree)
    {
        var spot = tree.FreeSpot();
        if (spot == null || !tree.FloorBeside(transform.position, out var foot)) return false;
        headingFor = spot;
        spot.Occupant = this;
        if (Vector3.Distance(foot, transform.position) < 0.5f) return true;
        if (ShipWalkTo(foot)) return true;
        headingFor = null;
        spot.Occupant = null;
        return false;
    }

    private void ClimbInto(CatTree.Spot spot)
    {
        treeSpot = spot;
        spot.Occupant = this;
        netPose.Value = spot.Pose;
        shipTimer = Random.Range(60f, 180f);
        ShipTeleport(spot.Point.position, CatTree.SpotYaw(spot));
#if DEBUG
        Plugin.Log.LogInfo($"{CatName} climbs into the cat tree: {spot.Name}");
#endif
    }

    private void LeaveTree()
    {
        CatTree.Instance?.Release(this);
        treeSpot = null;
        headingFor = null;
    }

    private void HopOffTree(CatTree tree)
    {
        LeaveTree();
        netPose.Value = PoseSit;
        poseTimer = Random.Range(15f, 30f);
        shipTimer = Random.Range(20f, 50f);
        if (!tree.FloorBeside(transform.position, out var p))
        {
            DropToFloor();
            return;
        }
        Vector3 away = Vector3.ProjectOnPlane(p - tree.transform.position, Vector3.up);
        ShipTeleport(p, away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away).eulerAngles.y : transform.eulerAngles.y);
    }

    // Somewhere on the open ship floor, for when the tree has gone: the lowest of a few spots is the floor, not furniture.
    private void DropToFloor()
    {
        var sor = StartOfRound.Instance;
        var b = ShipCore();
        Vector3? best = null;
        for (int i = 0; i < 12; i++)
        {
            var p = new Vector3(Random.Range(b.min.x, b.max.x), b.max.y - 0.1f, Random.Range(b.min.z, b.max.z));
            if (!Physics.Raycast(p, Vector3.down, out var hit, b.size.y + 1f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
            if (best == null || hit.point.y < best.Value.y - 0.05f) best = hit.point;
        }
        if (best == null) return;
        netPose.Value = PoseSit;
        ShipTeleport(best.Value, Random.Range(0f, 360f));
    }

    // A short stroll in a straight line to clear floor, like settling in after boarding.
    private void Wander()
    {
        var sor = StartOfRound.Instance;
        var b = ShipCore();
        Vector3 from = transform.position;
        for (int i = 0; i < 10; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(ShipWanderMin, ShipWanderMax);
            var p = new Vector3(from.x + r.x, from.y + 1f, from.z + r.y);
            if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
            if (!Physics.Raycast(p, Vector3.down, out var hit, 2f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.point.y - from.y) > 0.3f) continue;
            if (ShipWalkTo(hit.point))
            {
#if DEBUG
                Plugin.Log.LogInfo($"{CatName} wanders {Vector3.Distance(from, hit.point):0.0}m around the ship");
#endif
                return;
            }
        }
#if DEBUG
        Plugin.Log.LogInfo($"{CatName} found nowhere clear to wander (ship core {b.min} to {b.max}, cat at {from})");
#endif
    }

    private bool ShipWalkTo(Vector3 dest)
    {
        var sor = StartOfRound.Instance;
        Vector3 from = transform.position;
        if (Physics.Linecast(from + Vector3.up * 0.3f, dest + Vector3.up * 0.3f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) return false;
        var elev = sor.elevatorTransform;
        netPose.Value = PoseSit;
        poseTimer = Random.Range(15f, 30f);
        ShipMoveClientRpc(new[] { elev.InverseTransformPoint(from), elev.InverseTransformPoint(dest) }, FollowWalkSpeed, PoseWalk);
        return true;
    }

    private void ShipTeleport(Vector3 world, float yaw)
    {
        var elev = StartOfRound.Instance.elevatorTransform;
        float localYaw = Mathf.DeltaAngle(elev.eulerAngles.y, yaw);
        ShipTeleportClientRpc(elev.InverseTransformPoint(world), localYaw);
    }

    [ClientRpc]
    private void ShipMoveClientRpc(Vector3[] localCorners, float speed, byte newGait)
    {
        if (isHeld || localCorners == null || localCorners.Length < 2) return;
        path = localCorners;
        pathLocal = true;
        pathIndex = 1;
        moveSpeed = speed;
        gait = newGait;
    }

    [ClientRpc]
    private void ShipTeleportClientRpc(Vector3 local, float localYaw)
    {
        path = null;
        if (isHeld) return;
        var elev = StartOfRound.Instance.elevatorTransform;
        transform.position = elev.TransformPoint(local);
        transform.rotation = elev.rotation * Quaternion.Euler(0f, localYaw, 0f);
        targetFloorPosition = transform.localPosition;
    }

#if DEBUG
    // Debug spawns in the ship count as already rescued, so they start living aboard straight away.
    internal void DevMakeHome()
    {
        netRescued.Value = true;
        netTamed.Value = true;
        shipTimerSet = true;
        shipTimer = Random.Range(1f, 4f);
    }

    // F3: off the tree if on it, otherwise to the tree now (walking if there's a clear line, else straight up).
    internal void DevTree()
    {
        var tree = CatTree.Instance;
        if (!netRescued.Value || !isInShipRoom || isHeld || tree == null || !tree.IsOut) return;
        if (InTree)
        {
            HopOffTree(tree);
            return;
        }
        if (GoToTree(tree)) return;
        var spot = tree.FreeSpot();
        if (spot != null) ClimbInto(spot);
    }
#endif
}
