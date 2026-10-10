using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CozyCats;

public partial class CatItem : GrabbableObject
{
    private const byte PoseIdle = 0, PoseSit = 1, PoseLoaf = 2, PoseHeld = 3, PoseWalk = 4, PoseRun = 5;

    private static readonly int PoseHash = Animator.StringToHash("Pose");
    private static readonly int BlinkHash = Animator.StringToHash("Blink");
    private static readonly int TwitchLHash = Animator.StringToHash("TwitchL");
    private static readonly int TwitchRHash = Animator.StringToHash("TwitchR");
    private static readonly int MeowHash = Animator.StringToHash("Meow");
    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");

    private static Shader litShader;
    private static Material pupilMat, pinkMat;

    private readonly NetworkVariable<int> netSeed = new NetworkVariable<int>(0);
    private readonly NetworkVariable<byte> netPose = new NetworkVariable<byte>(PoseLoaf);
    // Host-resolved name, plus colours for config-defined special cats ("Name|FUR|EYE|Label").
    private readonly NetworkVariable<FixedString128Bytes> netIdentity = new NetworkVariable<FixedString128Bytes>();
    // Set the first time the cat reaches the ship, so the rescue bounty is paid once per cat.
    private readonly NetworkVariable<bool> netRescued = new NetworkVariable<bool>(false);

    private Animator animator;
    private AudioSource audioSource;
    private ScanNodeProperties scanNode;
    private Material furMat, eyeMat;
    private Transform model;
    private BoxCollider bodyCollider;
    private Vector3 modelBaseScale, colliderBaseCenter, colliderBaseSize, modelBasePos;
    private Quaternion modelBaseRot;
    private float size = 1f;
    private AudioClip grabClip, dropClip, purrClip;
    private AudioSource purrSource;

    private const float HeldMeowVolume = 0.27f;
    private const float AmbientMeowVolume = 0.18f;
    private const float HeldPurrVolume = 0.156f;
    // Eyeshine: a faint glow in the iris colour so cats are spottable in the dark. Emissive only, it lights nothing.
    private const float EyeGlow = 0.6f;

    // Extra pose applied to the model while held: rotate about the body's centre, then shift. Prefab space.
    public static Quaternion HeldRot = Quaternion.Euler(312.4f, 12.8f, 321.4f);
    public static Vector3 HeldShift = new Vector3(-0.034f, -0.039f, -0.166f);

    public Vector3 HeldPivot => new Vector3(0f, 0.2f * modelBaseScale.y * size, 0f);
    private bool visualsReady, hasMeowTrigger;
    private int pendingSeed;
    private bool loadedFromSave;

    private float poseTimer, blinkTimer, twitchTimer, ambientMeowTimer, meowCooldown;

    public string CatName { get; private set; } = "your cat";
    // Bounty shown by the "collected" HUD box; only set on the cat that was just rescued.
    public int PendingBountyDisplay { get; set; }

    // LethalLib prefabs are HideAndDontSave and clones inherit that, which hides them from FindObjectsOfType
    // (so vanilla ship saving, end-of-round cleanup and the debug finder would never see cats).
    private void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;
    }

    public override void Start()
    {
        base.Start();
        EnsureVisuals();
        poseTimer = Random.Range(10f, 30f);
        blinkTimer = Random.Range(1f, 5f);
        twitchTimer = Random.Range(8f, 30f);
        ambientMeowTimer = Random.Range(60f, 180f);
        ApplyLooks(netSeed.Value);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            if (netSeed.Value == 0) netSeed.Value = pendingSeed != 0 ? pendingSeed : CatLooks.NewSeed();
            netIdentity.Value = new FixedString128Bytes(CatLooks.ResolveIdentity(netSeed.Value));
            // Only ship items are saved, so a loaded cat was already rescued.
            if (loadedFromSave)
            {
                netRescued.Value = true;
                netTamed.Value = true;
            }
        }
        netSeed.OnValueChanged += OnSeedChanged;
        netIdentity.OnValueChanged += OnIdentityChanged;
        ApplyLooks(netSeed.Value);
    }

    public override void OnNetworkDespawn()
    {
        netSeed.OnValueChanged -= OnSeedChanged;
        netIdentity.OnValueChanged -= OnIdentityChanged;
        base.OnNetworkDespawn();
    }

    private void OnSeedChanged(int _, int seed) => ApplyLooks(seed);
    private void OnIdentityChanged(FixedString128Bytes _, FixedString128Bytes __) => ApplyLooks(netSeed.Value);

    private void EnsureVisuals()
    {
        if (visualsReady) return;
        visualsReady = true;

        animator = GetComponentInChildren<Animator>();
        audioSource = GetComponent<AudioSource>();
        purrSource = transform.Find("Purr")?.GetComponent<AudioSource>();
        scanNode = GetComponentInChildren<ScanNodeProperties>();
        model = transform.Find("Model");
        if (model != null)
        {
            modelBaseScale = model.localScale;
            modelBasePos = model.localPosition;
            modelBaseRot = model.localRotation;
        }
        bodyCollider = GetComponent<BoxCollider>();
        if (bodyCollider != null)
        {
            colliderBaseCenter = bodyCollider.center;
            colliderBaseSize = bodyCollider.size;
        }
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var p in animator.parameters)
                if (p.nameHash == MeowHash) hasMeowTrigger = true;
        }

        // Bundle materials target the built-in pipeline; swap in HDRP/Lit copies so they render in-game.
        litShader ??= Shader.Find("HDRP/Lit");
        if (litShader == null) return;
        pupilMat ??= MakeMat(new Color(0.02f, 0.02f, 0.02f), 0.7f);
        pinkMat ??= MakeMat(new Color(0.95f, 0.55f, 0.6f), 0.2f);
        furMat = MakeMat(Color.white, 0f);
        eyeMat = MakeMat(Color.green, 0.75f);

        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) return;
        var mats = smr.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            string n = mats[i] != null ? mats[i].name : "";
            mats[i] = n.Contains("Eye") ? eyeMat : n.Contains("Pupil") ? pupilMat : n.Contains("Pink") ? pinkMat : furMat;
        }
        smr.sharedMaterials = mats;
    }

    private static Material MakeMat(Color c, float smoothness)
    {
        var m = new Material(litShader);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    private void ApplyLooks(int seed)
    {
        if (seed == 0) return;
        EnsureVisuals();
        var looks = new CatLooks(seed, netIdentity.Value.ToString());
        CatName = looks.Name;
        size = looks.Size;

        var sources = CatRegistry.SoundSources;
        Item soundSource = sources.Length > 0 ? sources[looks.SoundRoll % sources.Length] : null;
        grabClip = soundSource?.grabSFX;
        dropClip = soundSource?.dropSFX;
        purrClip = CatAssets.Purrs.Length > 0 ? CatAssets.Purrs[(looks.SoundRoll / 7) % CatAssets.Purrs.Length] : null;
        if (furMat != null) furMat.SetColor("_BaseColor", looks.Fur);
        if (eyeMat != null)
        {
            eyeMat.SetColor("_BaseColor", looks.Eye);
            eyeMat.SetColor("_EmissiveColor", looks.Eye * EyeGlow);
        }
        if (model != null) model.localScale = modelBaseScale * looks.Size;
        if (bodyCollider != null)
        {
            bodyCollider.center = colliderBaseCenter * looks.Size;
            bodyCollider.size = colliderBaseSize * looks.Size;
        }
        if (scanNode != null)
        {
            scanNode.headerText = CatName;
            scanNode.subText = $"{looks.FurName} cat - not for sale";
        }
        customGrabTooltip = $"Pick up {CatName} : [E]";
    }

    public override void Update()
    {
        base.Update();
        float dt = Time.deltaTime;
        meowCooldown -= dt;

        UpdateMovement(dt);
        if (IsServer) UpdateBrain(dt);

        if (IsServer && !isHeld && !isHeldByEnemy && reachedFloorTarget && !IsMoving && mood == Mood.Calm)
        {
            poseTimer -= dt;
            if (poseTimer <= 0f)
            {
                poseTimer = Random.Range(25f, 70f);
                netPose.Value = PickRestingPose(netPose.Value);
            }

            if (Plugin.Cfg.AmbientMeows.Value)
            {
                ambientMeowTimer -= dt;
                if (ambientMeowTimer <= 0f)
                {
                    ambientMeowTimer = Random.Range(60f, 180f);
                    if (CatAssets.Meows.Length > 0) MeowClientRpc(Random.Range(0, CatAssets.Meows.Length), AmbientMeowVolume);
                }
            }
        }

        UpdatePurr(dt);

        if (animator == null) return;
        animator.SetInteger(PoseHash, isHeld || isHeldByEnemy ? PoseHeld : IsMoving ? gait : netPose.Value);
        if (IsMoving) animator.SetFloat(MoveSpeedHash, AnimSpeedFor(gait, moveSpeed));

        // Blinks and ear twitches are cosmetic, so each client rolls its own timing.
        blinkTimer -= dt;
        if (blinkTimer <= 0f)
        {
            blinkTimer = Random.Range(2.5f, 7f);
            animator.SetTrigger(BlinkHash);
        }
        twitchTimer -= dt;
        if (twitchTimer <= 0f)
        {
            twitchTimer = Random.Range(10f, 40f);
            animator.SetTrigger(Random.value < 0.5f ? TwitchLHash : TwitchRHash);
        }
    }

    // Soft purr that fades in while someone is holding the cat.
    private void UpdatePurr(float dt)
    {
        if (purrSource == null || purrClip == null) return;
        float target = isHeld ? HeldPurrVolume : 0f;
        purrSource.volume = Mathf.MoveTowards(purrSource.volume, target, dt * HeldPurrVolume / 1.5f);
        if (target > 0f && !purrSource.isPlaying)
        {
            purrSource.clip = purrClip;
            purrSource.time = Random.Range(0f, purrClip.length * 0.5f);
            purrSource.Play();
        }
        else if (target == 0f && purrSource.isPlaying && purrSource.volume <= 0f)
        {
            purrSource.Stop();
        }
    }

    private static byte PickRestingPose(byte current)
    {
        byte next;
        do next = (byte)Random.Range(PoseIdle, PoseLoaf + 1);
        while (next == current && Random.value < 0.7f);
        return next;
    }

    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        base.ItemActivate(used, buttonDown);
        if (!buttonDown || meowCooldown > 0f || CatAssets.Meows.Length == 0) return;
        meowCooldown = 1.2f;
        MeowServerRpc(Random.Range(0, CatAssets.Meows.Length));
    }

    [ServerRpc(RequireOwnership = false)]
    private void MeowServerRpc(int clip) => MeowClientRpc(clip, HeldMeowVolume);

    // Played straight on the AudioSource rather than through RoundManager.PlayRandomClip so enemies never hear it.
    [ClientRpc]
    private void MeowClientRpc(int clip, float volume)
    {
        if (CatAssets.Meows.Length == 0 || audioSource == null) return;
        audioSource.pitch = Random.Range(0.92f, 1.1f);
        audioSource.PlayOneShot(CatAssets.Meows[clip % CatAssets.Meows.Length], volume);
        if (hasMeowTrigger) animator.SetTrigger(MeowHash);
    }

    public override void LateUpdate()
    {
        base.LateUpdate();
        if (model == null) return;
        if (isHeld)
        {
            // The Held clip keeps the belly-up body centred ~0.2m up, so rotate around that.
            Vector3 pivot = HeldPivot;
            model.localRotation = HeldRot * modelBaseRot;
            model.localPosition = pivot + HeldRot * (modelBasePos - pivot) + HeldShift;
        }
        else
        {
            model.localRotation = modelBaseRot;
            model.localPosition = modelBasePos;
        }
    }

    // Vanilla PlayDropSFX also calls PlayAudibleNoise, which would let monsters hear the cat land.
    public override void PlayDropSFX()
    {
        if (dropClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(dropClip);
            WalkieTalkie.TransmitOneShotAudio(audioSource, dropClip);
        }
        hasHitGround = true;
    }

    // Runs on every client whose SetItemInElevator saw the cat enter the ship for the first time this round.
    public override void OnBroughtToShip()
    {
        base.OnBroughtToShip();
        if (netRescued.Value) return;
        if (IsServer) TryRescue();
        else RescueServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RescueServerRpc() => TryRescue();

    private void TryRescue()
    {
        if (netRescued.Value || StartOfRound.Instance.inShipPhase || !InShip()) return;
        netRescued.Value = true;
        int bounty = Plugin.Cfg.RescueBounty.Value;
        if (bounty <= 0) return;
        var terminal = FindObjectOfType<Terminal>();
        if (terminal == null) return;
        terminal.groupCredits += bounty;
        terminal.SyncGroupCreditsClientRpc(terminal.groupCredits, terminal.numberOfItemsInDropship);
        Plugin.Log.LogInfo($"{CatName} rescued: +${bounty} (credits now {terminal.groupCredits})");
        RescuedClientRpc(bounty);
    }

    // A client may report the rescue before the host has marked the cat as in the ship, so also check where it is.
    private bool InShip()
    {
        if (isInShipRoom || (playerHeldBy != null && playerHeldBy.isInHangarShipRoom)) return true;
        var bounds = StartOfRound.Instance.shipInnerRoomBounds.bounds;
        return bounds.Contains(transform.position);
    }

    [ClientRpc]
    private void RescuedClientRpc(int bounty)
    {
        PendingBountyDisplay = bounty;
        HUDManager.Instance?.AddNewScrapFoundToDisplay(this);
    }

    public override void GrabItem()
    {
        base.GrabItem();
        if (grabClip != null && audioSource != null) audioSource.PlayOneShot(grabClip, 0.8f);
    }

    public override void DiscardItem()
    {
        base.DiscardItem();
        if (IsServer) poseTimer = Random.Range(1.5f, 4f);
    }

    // Save int layout: bits 0-21 seed, bits 22-29 resting yaw in 2-degree steps.
    public override int GetItemDataToSave()
    {
        int yawSteps = Mathf.RoundToInt(Mathf.Repeat(transform.eulerAngles.y, 360f) / 2f) % 180;
        return (netSeed.Value & CatLooks.SeedMask) | (yawSteps << CatLooks.SeedBits);
    }

    public override void LoadItemSaveData(int saveData)
    {
        base.LoadItemSaveData(saveData);
        pendingSeed = saveData & CatLooks.SeedMask;
        loadedFromSave = true;
        float yaw = ((saveData >> CatLooks.SeedBits) & 0xFF) * 2f;
        floorYRot = Mathf.RoundToInt(Mathf.Repeat(yaw - 90f - itemProperties.floorYOffset, 360f));
    }
}
