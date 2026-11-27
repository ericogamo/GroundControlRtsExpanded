using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal struct GcreNetMessage
{
    public string Text;
}

internal sealed class GcreNetworkSync : IAddonTick, IAddonResetSession
{
    private const char Sep = '\u001f';
    private const string PartVerb = "part";
    private const int MaxWireBytes = 1050;
    private const int MaxPiecesPerPayload = 512;
    private const int MaxOpenPayloadsPerSender = 8;
    private const float PieceTimeoutSeconds = 30f;
    private const float HelloIntervalSeconds = 5.0f;
    private const float SyncIntervalSeconds = 0.5f;
    private const float FullSyncIntervalSeconds = 4.0f;
    private const string WireProtocolVersion = "gcre-1";

    private static readonly string SepText = Sep.ToString();

    internal static GcreNetworkSync Instance { get; } = new();

    private static bool messageRegistered;

    private MessageHandler? boundServer;
    private MessageHandler? boundClient;
    private bool wasServerActive;
    private bool wasClientActive;

    private readonly HashSet<INetworkPlayer> moddedGuests = new();
    private readonly HashSet<INetworkPlayer> knownAuthPlayers = new();
    private readonly List<INetworkPlayer> prunePlayersScratch = new();

    private readonly Dictionary<string, string?[]> pieceBuffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> pieceStartedAt = new(StringComparer.Ordinal);
    private readonly List<string> pieceScratch = new();
    private readonly List<string> stalePieceScratch = new();
    private int nextPieceId;

    private readonly List<(uint Pid, int KindInt, int Level)> citySyncScratch = new();
    private readonly List<GlobalPosition> remoteWaypointsScratch = new();
    private readonly List<bool> remoteForcedBridgeScratch = new();
    private readonly Dictionary<INetworkPlayer, string> lastAbmDigestByGuest = new();

    private float nextHelloAt;
    private float nextSyncAt;
    private float nextFullSyncAt;
    private bool cityDirty = true;
    private string lastConfigDigest = string.Empty;
    private string lastCityDigest = string.Empty;

    private GcreNetworkSync()
    {
    }

    internal static bool HostHasGcre { get; private set; }

    internal static bool IsPureClient =>
        GameManager.gameState == GameState.Multiplayer &&
        NetworkManagerNuclearOption.i != null &&
        NetworkManagerNuclearOption.i.Client != null &&
        NetworkManagerNuclearOption.i.Client.Active &&
        (NetworkManagerNuclearOption.i.Server == null || !NetworkManagerNuclearOption.i.Server.Active);

    private static bool IsServerAuthority =>
        GameManager.gameState == GameState.SinglePlayer ||
        (NetworkManagerNuclearOption.i != null &&
         NetworkManagerNuclearOption.i.Server != null &&
         NetworkManagerNuclearOption.i.Server.Active);

    // Remote Host Config & Cheat Overrides
    internal static bool RemoteEnableDynamicCaps { get; private set; } = true;
    internal static int RemoteStartGroundCap { get; private set; } = 15;
    internal static int RemoteStartAirCap { get; private set; } = 2;
    internal static int RemoteStartNavalCap { get; private set; } = 2;
    internal static int RemoteAbmMaxStockPerSite { get; private set; } = 8;
    internal static float RemoteAbmMissileCost { get; private set; } = 200f;
    internal static float RemoteAbmProductionSeconds { get; private set; } = 90f;
    internal static bool RemoteEnforceContractorFactionLock { get; private set; } = true;
    internal static string RemoteEconomyPreset { get; private set; } = "Fair";
    internal static bool RemoteEnableCapturedBaseIncome { get; private set; } = true;
    internal static float RemoteCapturedBaseIncomePerMinute { get; private set; } = 50f;
    internal static bool RemoteRequirePhysicalConstruction { get; private set; } = true;
    internal static float RemoteMaxRoadGradePercent { get; private set; } = 30f;
    internal static float RemoteRoadCostPer100m { get; private set; } = 25f;
    internal static float RemoteBridgeCostPer100m { get; private set; } = 75f;
    internal static string RemoteCheatTier3HqName { get; private set; } = string.Empty;
    internal static string RemoteCheatIgnoreCapsHqName { get; private set; } = string.Empty;
    internal static bool RemoteCheatIgnoreFactionLock { get; private set; }

    internal static bool EffectiveEnableDynamicCaps =>
        (IsPureClient && HostHasGcre) ? RemoteEnableDynamicCaps : AddonConfig.EnableDynamicCaps.Value;

    internal static int EffectiveStartGroundCap =>
        (IsPureClient && HostHasGcre) ? RemoteStartGroundCap : AddonConfig.StartGroundCap.Value;

    internal static int EffectiveStartAirCap =>
        (IsPureClient && HostHasGcre) ? RemoteStartAirCap : AddonConfig.StartAirCap.Value;

    internal static int EffectiveStartNavalCap =>
        (IsPureClient && HostHasGcre) ? RemoteStartNavalCap : AddonConfig.StartNavalCap.Value;

    internal static int EffectiveAbmMaxStockPerSite =>
        (IsPureClient && HostHasGcre) ? RemoteAbmMaxStockPerSite : AddonConfig.AbmMaxStockPerSite.Value;

    internal static float EffectiveAbmMissileCost =>
        (IsPureClient && HostHasGcre) ? RemoteAbmMissileCost : AddonConfig.AbmMissileCost.Value;

    internal static float EffectiveAbmProductionSeconds =>
        (IsPureClient && HostHasGcre) ? RemoteAbmProductionSeconds : AddonConfig.AbmProductionSeconds.Value;

    internal static bool EffectiveEnforceContractorFactionLock =>
        (IsPureClient && HostHasGcre) ? RemoteEnforceContractorFactionLock : AddonConfig.EnforceContractorFactionLock.Value;

    internal static string EffectiveEconomyPreset =>
        (IsPureClient && HostHasGcre) ? RemoteEconomyPreset : (AddonConfig.EconomyPreset.Value ?? "Fair");

    internal static bool EffectiveEnableCapturedBaseIncome =>
        (IsPureClient && HostHasGcre) ? RemoteEnableCapturedBaseIncome : AddonConfig.EnableCapturedBaseIncome.Value;

    internal static float EffectiveCapturedBaseIncomePerMinute =>
        (IsPureClient && HostHasGcre) ? RemoteCapturedBaseIncomePerMinute : AddonConfig.CapturedBaseIncomePerMinute.Value;

    internal static bool EffectiveRequirePhysicalConstruction =>
        (IsPureClient && HostHasGcre) ? RemoteRequirePhysicalConstruction : AddonConfig.RequirePhysicalConstruction.Value;

    internal static float EffectiveMaxRoadGradePercent =>
        (IsPureClient && HostHasGcre) ? RemoteMaxRoadGradePercent : AddonConfig.MaxRoadGradePercent.Value;

    internal static float EffectiveRoadCostPer100m =>
        (IsPureClient && HostHasGcre) ? RemoteRoadCostPer100m : AddonConfig.RoadCostPer100m.Value;

    internal static float EffectiveBridgeCostPer100m =>
        (IsPureClient && HostHasGcre) ? RemoteBridgeCostPer100m : AddonConfig.BridgeCostPer100m.Value;

    internal static void Initialize()
    {
        if (messageRegistered)
        {
            return;
        }

        try
        {
            Writer<GcreNetMessage>.Write = (writer, msg) => writer.WriteString(msg.Text);
            Reader<GcreNetMessage>.Read = reader => new GcreNetMessage
            {
                Text = reader.ReadString() ?? string.Empty
            };

            int id = MessagePacker.GetId<GcreNetMessage>();
            if (MessagePacker.MessageTypes.TryGetValue(id, out Type? existing) && existing != typeof(GcreNetMessage))
            {
                AddonLog.Error($"[Net] GcreNetMessage ID {id} collides with {existing.FullName}; GCRE multiplayer sync disabled.");
                return;
            }

            MessagePacker.RegisterMessage<GcreNetMessage>();
            messageRegistered = true;
        }
        catch (Exception ex)
        {
            AddonLog.Error($"[Net] Failed registering GcreNetMessage: {ex.Message}");
        }
    }

    public void ResetSession()
    {
        moddedGuests.Clear();
        knownAuthPlayers.Clear();
        pieceBuffers.Clear();
        pieceStartedAt.Clear();
        lastAbmDigestByGuest.Clear();
        cityDirty = true;
        HostHasGcre = false;
        nextHelloAt = 0f;
        nextSyncAt = 0f;
        nextFullSyncAt = 0f;
        lastConfigDigest = string.Empty;
        lastCityDigest = string.Empty;

        RemoteEnableDynamicCaps = true;
        RemoteStartGroundCap = 15;
        RemoteStartAirCap = 2;
        RemoteStartNavalCap = 2;
        RemoteAbmMaxStockPerSite = 8;
        RemoteAbmMissileCost = 200f;
        RemoteAbmProductionSeconds = 90f;
        RemoteEnforceContractorFactionLock = true;
        RemoteEconomyPreset = "Fair";
        RemoteEnableCapturedBaseIncome = true;
        RemoteCapturedBaseIncomePerMinute = 50f;
        RemoteRequirePhysicalConstruction = true;
        RemoteMaxRoadGradePercent = 30f;
        RemoteRoadCostPer100m = 25f;
        RemoteBridgeCostPer100m = 75f;
        RemoteCheatTier3HqName = string.Empty;
        RemoteCheatIgnoreCapsHqName = string.Empty;
        RemoteCheatIgnoreFactionLock = false;
    }

    public void Tick()
    {
        if (!messageRegistered)
        {
            return;
        }

        NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
        NetworkServer? server = net != null ? net.Server : null;
        NetworkClient? client = net != null ? net.Client : null;

        bool serverActive = server != null && server.Active;
        bool clientActive = client != null && client.Active;

        if ((wasServerActive && !serverActive) || (wasClientActive && !clientActive && !serverActive))
        {
            boundServer = null;
            boundClient = null;
            AddonRuntimeController.Instance?.TriggerNetworkSessionReset();
        }

        wasServerActive = serverActive;
        wasClientActive = clientActive;

        if (serverActive && server != null)
        {
            TickServer(server);
            return;
        }

        if (clientActive && client != null)
        {
            TickClient(client);
        }
        else
        {
            boundClient = null;
            HostHasGcre = false;
        }
    }

    private void TickServer(NetworkServer server)
    {
        if (!ReferenceEquals(boundServer, server.MessageHandler) && server.MessageHandler != null)
        {
            boundServer = server.MessageHandler;
            boundServer.RegisterHandler<GcreNetMessage>(OnHostMessage, allowUnauthenticated: false);
        }

        float now = Time.unscaledTime;
        IReadOnlyList<INetworkPlayer> players = server.AuthenticatedPlayers;
        bool newPlayerJoined = false;
        for (int i = 0; i < players.Count; i++)
        {
            INetworkPlayer p = players[i];
            if (p != null && !p.IsHost && knownAuthPlayers.Add(p))
            {
                newPlayerJoined = true;
            }
        }

        if (newPlayerJoined || now >= nextHelloAt)
        {
            nextHelloAt = now + HelloIntervalSeconds;
            PruneDisconnectedPlayers(players);
            SendHelloToAll(players);
        }

        if (moddedGuests.Count == 0)
        {
            return;
        }

        if (now >= nextSyncAt)
        {
            bool forceFull = now >= nextFullSyncAt;
            nextSyncAt = now + SyncIntervalSeconds;
            if (forceFull)
            {
                nextFullSyncAt = now + FullSyncIntervalSeconds;
            }

            FlushConfigIfChanged(forceFull, targetPlayer: null);
            FlushCitySync(forceFull || cityDirty, targetPlayer: null);
            cityDirty = false;
            FlushAbmSync(forceFull, targetPlayer: null);
        }
    }

    private void TickClient(NetworkClient client)
    {
        if (!ReferenceEquals(boundClient, client.MessageHandler) && client.MessageHandler != null)
        {
            boundClient = client.MessageHandler;
            boundClient.RegisterHandler<GcreNetMessage>(OnGuestMessage, allowUnauthenticated: false);
        }
    }

    // --- Public / Internal Mod API for broadcasting and Client requests ---

    internal static void MarkCityDirty()
    {
        Instance.cityDirty = true;
    }

    internal static void RequestUpgrade(uint pidId)
    {
        if (IsPureClient && HostHasGcre && pidId != 0)
        {
            Instance.SendToHost(Join("req_up", pidId.ToString(CultureInfo.InvariantCulture)));
        }
    }

    internal static void RequestAbmAutoProduce(bool enabled)
    {
        if (IsPureClient && HostHasGcre)
        {
            Instance.SendToHost(Join("req_abm_auto", enabled ? "1" : "0"));
        }
    }

    internal static void RequestBuildRoad(IReadOnlyList<GlobalPosition> waypoints, IReadOnlyList<bool> forcedBridges)
    {
        if (!IsPureClient || !HostHasGcre || waypoints == null || waypoints.Count < 2)
        {
            return;
        }

        var wpSb = new StringBuilder();
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (i > 0) wpSb.Append(';');
            wpSb.Append(waypoints[i].x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(waypoints[i].y.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(waypoints[i].z.ToString("0.##", CultureInfo.InvariantCulture));
        }

        var fbSb = new StringBuilder();
        for (int i = 0; i < forcedBridges.Count; i++)
        {
            if (i > 0) fbSb.Append(',');
            fbSb.Append(forcedBridges[i] ? '1' : '0');
        }

        Instance.SendToHost(Join("req_road", wpSb.ToString(), fbSb.ToString()));
    }

    internal static void RequestCancelRoad(int projectId)
    {
        if (IsPureClient && HostHasGcre && projectId > 0)
        {
            Instance.SendToHost(Join("req_road_cancel", projectId.ToString(CultureInfo.InvariantCulture)));
        }
    }

    internal static void RequestHitch(uint containerPid)
    {
        if (IsPureClient && HostHasGcre && containerPid != 0)
        {
            Instance.SendToHost(Join("req_hitch", containerPid.ToString(CultureInfo.InvariantCulture)));
        }
    }

    internal static void RequestDrop(uint unitPid)
    {
        if (IsPureClient && HostHasGcre && unitPid != 0)
        {
            Instance.SendToHost(Join("req_drop", unitPid.ToString(CultureInfo.InvariantCulture)));
        }
    }

    internal static void BroadcastFactionToast(FactionHQ? hq, bool isAlert, string text)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || hq == null || string.IsNullOrEmpty(text))
        {
            return;
        }

        string payload = Join("toast", isAlert ? "1" : "0", text);
        foreach (INetworkPlayer guest in Instance.moddedGuests)
        {
            if (guest != null && ReferenceEquals(ResolvePlayerHq(guest), hq))
            {
                Instance.SendToPlayer(guest, payload);
            }
        }
    }

    internal static void BroadcastGlobalToast(bool isAlert, string text)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || string.IsNullOrEmpty(text))
        {
            return;
        }

        Instance.SendToAllModdedGuests(Join("toast", isAlert ? "1" : "0", text));
    }

    internal static void BroadcastPartHeal(uint pidId)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || pidId == 0)
        {
            return;
        }

        Instance.SendToAllModdedGuests(Join("part_heal", pidId.ToString(CultureInfo.InvariantCulture)));
    }

    internal static void BroadcastRearmVisual(uint pidId, byte stationIndex, int ammoDelta)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || pidId == 0 || ammoDelta <= 0)
        {
            return;
        }

        Instance.SendToAllModdedGuests(Join(
            "rearm_vis",
            pidId.ToString(CultureInfo.InvariantCulture),
            stationIndex.ToString(CultureInfo.InvariantCulture),
            ammoDelta.ToString(CultureInfo.InvariantCulture)));
    }

    internal static void BroadcastRadarEmcon(uint pidId, bool active)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || pidId == 0)
        {
            return;
        }

        Instance.SendToAllModdedGuests(Join(
            "radar_emcon",
            pidId.ToString(CultureInfo.InvariantCulture),
            active ? "1" : "0"));
    }

    internal static void BroadcastRoadCompleted(int id, IReadOnlyList<RoadSample> samples)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || samples == null || samples.Count < 2)
        {
            return;
        }

        Instance.SendToAllModdedGuests(Join(
            "road_done",
            id.ToString(CultureInfo.InvariantCulture),
            PioneerRoadService.SerializeSamples(samples)));
    }

    internal static void BroadcastRoadProgress(int id, FactionHQ? hq, float totalLength, float builtLength, bool cancelled, IReadOnlyList<RoadSample>? samples)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority)
        {
            return;
        }

        string hqName = hq?.faction?.factionName ?? string.Empty;
        string sampleData = samples != null && samples.Count >= 2 ? PioneerRoadService.SerializeSamples(samples) : string.Empty;
        Instance.SendToAllModdedGuests(Join(
            "road_prog",
            id.ToString(CultureInfo.InvariantCulture),
            hqName,
            totalLength.ToString("0.##", CultureInfo.InvariantCulture),
            builtLength.ToString("0.##", CultureInfo.InvariantCulture),
            cancelled ? "1" : "0",
            sampleData));
    }

    internal static void BroadcastHaulSync(
        uint containerPid,
        uint haulerPid,
        int stageInt,
        Vector3 bedOffset,
        float loadDur,
        float loadElapsed,
        INetworkPlayer? targetPlayer = null)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || containerPid == 0 || haulerPid == 0)
        {
            return;
        }

        string payload = Join(
            "haul_sync",
            containerPid.ToString(CultureInfo.InvariantCulture),
            haulerPid.ToString(CultureInfo.InvariantCulture),
            stageInt.ToString(CultureInfo.InvariantCulture),
            bedOffset.x.ToString("0.###", CultureInfo.InvariantCulture),
            bedOffset.y.ToString("0.###", CultureInfo.InvariantCulture),
            bedOffset.z.ToString("0.###", CultureInfo.InvariantCulture),
            loadDur.ToString("0.##", CultureInfo.InvariantCulture),
            loadElapsed.ToString("0.##", CultureInfo.InvariantCulture));
        if (targetPlayer != null)
        {
            Instance.SendToPlayer(targetPlayer, payload);
        }
        else
        {
            Instance.SendToAllModdedGuests(payload);
        }
    }

    internal static void BroadcastHaulDrop(
        uint containerPid,
        uint haulerPid,
        bool placedOnGround,
        GlobalPosition dropPos,
        float yawDeg)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || containerPid == 0)
        {
            return;
        }

        string payload = Join(
            "haul_drop",
            containerPid.ToString(CultureInfo.InvariantCulture),
            haulerPid.ToString(CultureInfo.InvariantCulture),
            placedOnGround ? "1" : "0",
            dropPos.x.ToString("0.##", CultureInfo.InvariantCulture),
            dropPos.y.ToString("0.##", CultureInfo.InvariantCulture),
            dropPos.z.ToString("0.##", CultureInfo.InvariantCulture),
            yawDeg.ToString("0.##", CultureInfo.InvariantCulture));
        Instance.SendToAllModdedGuests(payload);
    }

    internal static void BroadcastRelocatedBuilding(uint pid, GlobalPosition pos, float yawDeg, INetworkPlayer? targetPlayer = null)
    {
        if (!messageRegistered || Instance.moddedGuests.Count == 0 || !IsServerAuthority || pid == 0)
        {
            return;
        }

        string payload = Join(
            "reloc_bldg",
            pid.ToString(CultureInfo.InvariantCulture),
            pos.x.ToString("0.##", CultureInfo.InvariantCulture),
            pos.y.ToString("0.##", CultureInfo.InvariantCulture),
            pos.z.ToString("0.##", CultureInfo.InvariantCulture),
            yawDeg.ToString("0.##", CultureInfo.InvariantCulture));
        if (targetPlayer != null)
        {
            Instance.SendToPlayer(targetPlayer, payload);
        }
        else
        {
            Instance.SendToAllModdedGuests(payload);
        }
    }

    // --- Host Send / Flush Helpers ---

    private void SendHelloToAll(IReadOnlyList<INetworkPlayer> players)
    {
        string payload = Join("hello", WireProtocolVersion, BuildConfigPayloadBody());
        for (int i = 0; i < players.Count; i++)
        {
            INetworkPlayer p = players[i];
            if (p != null && !p.IsHost)
            {
                SendToPlayer(p, payload);
            }
        }
    }

    private void FlushConfigIfChanged(bool force, INetworkPlayer? targetPlayer)
    {
        string body = BuildConfigPayloadBody();
        if (!force && targetPlayer == null && string.Equals(body, lastConfigDigest, StringComparison.Ordinal))
        {
            return;
        }

        if (targetPlayer == null)
        {
            lastConfigDigest = body;
        }

        string payload = Join("cfg", body);
        if (targetPlayer != null)
        {
            SendToPlayer(targetPlayer, payload);
        }
        else
        {
            SendToAllModdedGuests(payload);
        }
    }

    private static string BuildConfigPayloadBody()
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        string tier3Hq = (SingleplayerCheatService.IsAllowed && localHq != null && SingleplayerCheatService.IsUnlockTier3ActiveFor(localHq))
            ? (localHq.faction?.factionName ?? string.Empty)
            : string.Empty;
        string ignoreCapsHq = (SingleplayerCheatService.IsAllowed && localHq != null && SingleplayerCheatService.IsIgnoreCapsActiveFor(localHq))
            ? (localHq.faction?.factionName ?? string.Empty)
            : string.Empty;
        bool ignoreLock = SingleplayerCheatService.IsAllowed && SingleplayerCheatService.IsIgnoreFactionLockActive;

        return string.Join("|",
            AddonConfig.EnableDynamicCaps.Value ? "1" : "0",
            AddonConfig.StartGroundCap.Value.ToString(CultureInfo.InvariantCulture),
            AddonConfig.StartAirCap.Value.ToString(CultureInfo.InvariantCulture),
            AddonConfig.StartNavalCap.Value.ToString(CultureInfo.InvariantCulture),
            AddonConfig.AbmMaxStockPerSite.Value.ToString(CultureInfo.InvariantCulture),
            AddonConfig.AbmMissileCost.Value.ToString("0.#", CultureInfo.InvariantCulture),
            AddonConfig.AbmProductionSeconds.Value.ToString("0.#", CultureInfo.InvariantCulture),
            AddonConfig.EnforceContractorFactionLock.Value ? "1" : "0",
            AddonConfig.EconomyPreset.Value ?? "Fair",
            AddonConfig.EnableCapturedBaseIncome.Value ? "1" : "0",
            AddonConfig.CapturedBaseIncomePerMinute.Value.ToString("0.#", CultureInfo.InvariantCulture),
            AddonConfig.RequirePhysicalConstruction.Value ? "1" : "0",
            AddonConfig.MaxRoadGradePercent.Value.ToString("0.#", CultureInfo.InvariantCulture),
            AddonConfig.RoadCostPer100m.Value.ToString("0.#", CultureInfo.InvariantCulture),
            AddonConfig.BridgeCostPer100m.Value.ToString("0.#", CultureInfo.InvariantCulture),
            tier3Hq,
            ignoreCapsHq,
            ignoreLock ? "1" : "0");
    }

    private void FlushCitySync(bool force, INetworkPlayer? targetPlayer)
    {
        var recSb = new StringBuilder();
        foreach (RtsBuildingRecord rec in DynamicCapAndTechService.Instance.AllRecords)
        {
            if (rec.Unit == null || rec.Unit.disabled || !rec.Unit.persistentID.IsValid)
            {
                continue;
            }

            if (recSb.Length > 0) recSb.Append(';');
            recSb.Append(rec.Unit.persistentID.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(((int)rec.Spec.Kind).ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(rec.Level.ToString(CultureInfo.InvariantCulture));
        }

        string mdsCaps = MdsBridgeBootstrap.BuildReservedCapsSyncString?.Invoke() ?? string.Empty;
        string digest = recSb.ToString() + "#" + mdsCaps;
        if (!force && targetPlayer == null && string.Equals(digest, lastCityDigest, StringComparison.Ordinal))
        {
            return;
        }

        if (targetPlayer == null)
        {
            lastCityDigest = digest;
        }

        string payload = Join("city", recSb.ToString(), mdsCaps);
        if (targetPlayer != null)
        {
            SendToPlayer(targetPlayer, payload);
        }
        else
        {
            SendToAllModdedGuests(payload);
        }
    }

    private void FlushAbmSync(bool force, INetworkPlayer? targetPlayer)
    {
        string threatsStr = AbmDefenseService.Instance.BuildServerThreatsPayload();
        foreach (INetworkPlayer guest in moddedGuests)
        {
            if (guest == null || (targetPlayer != null && !ReferenceEquals(guest, targetPlayer)))
            {
                continue;
            }

            FactionHQ? guestHq = ResolvePlayerHq(guest);
            string stateStr = guestHq != null
                ? AbmDefenseService.Instance.BuildServerFactionPayload(guestHq)
                : string.Empty;
            string digest = stateStr + "#" + threatsStr;
            if (!force && lastAbmDigestByGuest.TryGetValue(guest, out string prev) && string.Equals(prev, digest, StringComparison.Ordinal))
            {
                continue;
            }

            lastAbmDigestByGuest[guest] = digest;
            SendToPlayer(guest, Join("abm", guestHq?.faction?.factionName ?? string.Empty, stateStr, threatsStr));
        }
    }

    private void ReplayInitialWorldState(INetworkPlayer guest)
    {
        FlushConfigIfChanged(force: true, targetPlayer: guest);
        FlushCitySync(force: true, targetPlayer: guest);
        FlushAbmSync(force: true, targetPlayer: guest);

        IReadOnlyList<(int Id, List<RoadSample> Samples)> doneRoads = PioneerRoadService.Instance.GetCompletedRoads();
        for (int i = 0; i < doneRoads.Count; i++)
        {
            (int Id, List<RoadSample> Samples) item = doneRoads[i];
            SendToPlayer(guest, Join("road_done", item.Id.ToString(CultureInfo.InvariantCulture), PioneerRoadService.SerializeSamples(item.Samples)));
        }

        IReadOnlyList<RoadConstructionProject> activeRoads = PioneerRoadService.Instance.GetActiveProjects();
        for (int i = 0; i < activeRoads.Count; i++)
        {
            RoadConstructionProject p = activeRoads[i];
            SendToPlayer(guest, Join(
                "road_prog",
                p.Id.ToString(CultureInfo.InvariantCulture),
                p.Hq?.faction?.factionName ?? string.Empty,
                p.TotalLengthMeters.ToString("0.##", CultureInfo.InvariantCulture),
                p.BuiltLengthMeters.ToString("0.##", CultureInfo.InvariantCulture),
                "0",
                PioneerRoadService.SerializeSamples(p.Samples)));
        }

        MdsFlatbedHaulService.Instance.SendInitialSyncToPlayer(guest);
    }

    // --- Message Handlers ---

    private void OnHostMessage(INetworkPlayer sender, GcreNetMessage message)
    {
        string? full = TryReassemble(sender.ToString() ?? "guest", message.Text ?? string.Empty);
        if (string.IsNullOrEmpty(full))
        {
            return;
        }

        string[] args = full!.Split(Sep);
        if (args.Length == 0)
        {
            return;
        }

        string verb = args[0];
        if (verb == "hi")
        {
            bool added = moddedGuests.Add(sender);
            if (added)
            {
                AddonLog.Info($"[Net] Guest {sender} verified GCRE sync ({WireProtocolVersion}). Sending initial state.");
            }

            ReplayInitialWorldState(sender);
            return;
        }

        FactionHQ? senderHq = ResolvePlayerHq(sender);
        if (senderHq == null)
        {
            return;
        }

        switch (verb)
        {
            case "req_up":
                if (args.Length >= 2 && uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint upPid))
                {
                    if (DynamicCapAndTechService.Instance.TryUpgradeRemote(senderHq, upPid, out string upMsg))
                    {
                        cityDirty = true;
                        FlushCitySync(force: true, targetPlayer: null);
                        SendToPlayer(sender, Join("toast", "0", upMsg));
                    }
                    else if (!string.IsNullOrEmpty(upMsg))
                    {
                        SendToPlayer(sender, Join("toast", "0", upMsg));
                    }
                }
                break;

            case "req_abm_auto":
                if (args.Length >= 2)
                {
                    AbmDefenseService.Instance.SetAutoProduce(senderHq, args[1] == "1");
                    FlushAbmSync(force: true, targetPlayer: null);
                }
                break;

            case "req_road":
                if (args.Length >= 3)
                {
                    ParseRemoteRoadArgs(args[1], args[2], remoteWaypointsScratch, remoteForcedBridgeScratch);
                    if (!PioneerRoadService.Instance.TryCommitRemoteRoad(senderHq, remoteWaypointsScratch, remoteForcedBridgeScratch, out string roadMsg))
                    {
                        SendToPlayer(sender, Join("toast", "0", roadMsg));
                    }
                    else if (!string.IsNullOrEmpty(roadMsg))
                    {
                        SendToPlayer(sender, Join("toast", "0", roadMsg));
                    }
                }
                break;

            case "req_road_cancel":
                if (args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int projId))
                {
                    if (PioneerRoadService.Instance.TryCancelRemoteRoad(senderHq, projId, out string cancelMsg) && !string.IsNullOrEmpty(cancelMsg))
                    {
                        SendToPlayer(sender, Join("toast", "0", cancelMsg));
                    }
                }
                break;

            case "req_hitch":
                if (args.Length >= 2 && uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint containerPid))
                {
                    MdsFlatbedHaulService.Instance.TryBeginHitchRemote(senderHq, containerPid, out string hitchMsg);
                    if (!string.IsNullOrEmpty(hitchMsg))
                    {
                        SendToPlayer(sender, Join("toast", "0", hitchMsg));
                    }
                }
                break;

            case "req_drop":
                if (args.Length >= 2 && uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint unitPid))
                {
                    MdsFlatbedHaulService.Instance.TryDropRemote(senderHq, unitPid, out string dropMsg);
                    if (!string.IsNullOrEmpty(dropMsg))
                    {
                        SendToPlayer(sender, Join("toast", "0", dropMsg));
                    }
                }
                break;
        }
    }

    private void OnGuestMessage(INetworkPlayer _, GcreNetMessage message)
    {
        if (IsServerAuthority)
        {
            return;
        }

        string? full = TryReassemble("host", message.Text ?? string.Empty);
        if (string.IsNullOrEmpty(full))
        {
            return;
        }

        string[] args = full!.Split(Sep);
        if (args.Length == 0)
        {
            return;
        }

        string verb = args[0];
        if (verb == "hello")
        {
            string hostProto = args.Length > 1 ? args[1] : string.Empty;
            if (args.Length > 2)
            {
                ApplyConfigBody(args[2]);
            }

            bool firstHandshake = !HostHasGcre;
            HostHasGcre = string.Equals(hostProto, WireProtocolVersion, StringComparison.Ordinal);
            if (HostHasGcre && firstHandshake)
            {
                AddonLog.Info($"[Net] Connected to GCRE host ({hostProto}). Sending 'hi'.");
                SendToHost(Join("hi", WireProtocolVersion));
            }
            return;
        }

        if (!HostHasGcre)
        {
            return;
        }

        switch (verb)
        {
            case "cfg":
                if (args.Length >= 2)
                {
                    ApplyConfigBody(args[1]);
                    DynamicCapAndTechService.Instance.InvalidateCache();
                }
                break;

            case "city":
                HandleCitySyncOnClient(args);
                break;

            case "abm":
                if (args.Length >= 4)
                {
                    AbmDefenseService.Instance.ApplyClientSnapshot(FindHqByName(args[1]), args[2], args[3]);
                }
                break;

            case "toast":
                if (args.Length >= 3)
                {
                    if (args[1] == "1")
                    {
                        GroundControlReflection.NotifyAlert(args[2]);
                    }
                    else
                    {
                        GroundControlReflection.NoticeFail(args[2]);
                    }
                }
                break;

            case "part_heal":
                if (args.Length >= 2 && uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint healPid) && healPid != 0)
                {
                    var pid = new PersistentID { Id = healPid };
                    if (pid.TryGetUnit(out Unit unit) && unit != null && !unit.disabled)
                    {
                        List<UnitPart> parts = unit.GetAllParts();
                        for (int p = 0; p < parts.Count; p++)
                        {
                            UnitPart? part = parts[p];
                            if (part != null && part.hitPoints > 0f && part.hitPoints < 99.9f)
                            {
                                part.Repair();
                            }
                        }
                    }
                }
                break;

            case "rearm_vis":
                if (args.Length >= 4 &&
                    uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint rearmPid) &&
                    byte.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte stationIdx) &&
                    int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ammoDelta) &&
                    ammoDelta > 0)
                {
                    var pid = new PersistentID { Id = rearmPid };
                    if (pid.TryGetUnit(out Unit unit) &&
                        unit != null &&
                        !unit.disabled &&
                        unit.weaponStations != null &&
                        stationIdx < unit.weaponStations.Count)
                    {
                        unit.weaponStations[stationIdx]?.Rearm(ammoDelta);
                    }
                }
                break;

            case "radar_emcon":
                if (args.Length >= 3 &&
                    uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint radarPid))
                {
                    bool active = args[2] == "1";
                    var pid = new PersistentID { Id = radarPid };
                    if (pid.TryGetUnit(out Unit unit) && unit != null && !unit.disabled)
                    {
                        Radar[] radars = unit.GetComponentsInChildren<Radar>(true);
                        for (int i = 0; i < radars.Length; i++)
                        {
                            Radar r = radars[i];
                            if (r == null || !r.IsOperational())
                            {
                                continue;
                            }

                            r.activated = active;
                            r.enabled = active || r.IsJammed();
                            if (!active)
                            {
                                r.detectedTargets.Clear();
                                r.ResetRotators();
                            }
                        }
                    }
                }
                break;

            case "road_done":
                if (args.Length >= 3 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int doneId))
                {
                    List<RoadSample> samples = PioneerRoadService.DeserializeSamples(args[2]);
                    if (samples.Count >= 2)
                    {
                        PioneerRoadService.Instance.ApplyClientCompletedRoad(doneId, samples);
                    }
                }
                break;

            case "road_prog":
                if (args.Length >= 6 &&
                    int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int progId) &&
                    float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float totalLen) &&
                    float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float builtLen))
                {
                    bool cancelled = args[5] == "1";
                    List<RoadSample>? samples = (args.Length >= 7 && !string.IsNullOrEmpty(args[6]))
                        ? PioneerRoadService.DeserializeSamples(args[6])
                        : null;
                    PioneerRoadService.Instance.ApplyClientRoadProgress(progId, args[2], totalLen, builtLen, cancelled, samples);
                }
                break;

            case "haul_sync":
                if (args.Length >= 9 &&
                    uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint cPid) &&
                    uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint hPid) &&
                    int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stInt) &&
                    float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float bx) &&
                    float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float by) &&
                    float.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float bz) &&
                    float.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float ld) &&
                    float.TryParse(args[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float le))
                {
                    MdsFlatbedHaulService.Instance.ApplyClientHaulSync(cPid, hPid, stInt, new Vector3(bx, by, bz), ld, le);
                }
                break;

            case "haul_drop":
                if (args.Length >= 8 &&
                    uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint dcPid) &&
                    uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint dhPid) &&
                    float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) &&
                    float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy) &&
                    float.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float dz) &&
                    float.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
                {
                    MdsFlatbedHaulService.Instance.ApplyClientHaulDrop(dcPid, dhPid, args[3] == "1", new GlobalPosition(dx, dy, dz), yaw);
                }
                break;

            case "reloc_bldg":
                if (args.Length >= 6 &&
                    uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint rPid) &&
                    float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float rx) &&
                    float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float ry) &&
                    float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float rz) &&
                    float.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float ryaw))
                {
                    MdsFlatbedHaulService.Instance.ApplyClientRelocatedBuilding(rPid, new GlobalPosition(rx, ry, rz), ryaw);
                }
                break;
        }
    }

    private static void ApplyConfigBody(string body)
    {
        string[] p = body.Split('|');
        if (p.Length < 15)
        {
            return;
        }

        RemoteEnableDynamicCaps = p[0] == "1";
        if (int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sg)) RemoteStartGroundCap = sg;
        if (int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sa)) RemoteStartAirCap = sa;
        if (int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sn)) RemoteStartNavalCap = sn;
        if (int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms)) RemoteAbmMaxStockPerSite = ms;
        if (float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float ac)) RemoteAbmMissileCost = ac;
        if (float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float ap)) RemoteAbmProductionSeconds = ap;
        RemoteEnforceContractorFactionLock = p[7] == "1";
        if (!string.IsNullOrEmpty(p[8])) RemoteEconomyPreset = p[8];
        RemoteEnableCapturedBaseIncome = p[9] == "1";
        if (float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float cb)) RemoteCapturedBaseIncomePerMinute = cb;
        RemoteRequirePhysicalConstruction = p[11] == "1";
        if (float.TryParse(p[12], NumberStyles.Float, CultureInfo.InvariantCulture, out float mg)) RemoteMaxRoadGradePercent = mg;
        if (float.TryParse(p[13], NumberStyles.Float, CultureInfo.InvariantCulture, out float rc)) RemoteRoadCostPer100m = rc;
        if (float.TryParse(p[14], NumberStyles.Float, CultureInfo.InvariantCulture, out float bc)) RemoteBridgeCostPer100m = bc;
        RemoteCheatTier3HqName = p.Length > 15 ? p[15] : string.Empty;
        RemoteCheatIgnoreCapsHqName = p.Length > 16 ? p[16] : string.Empty;
        RemoteCheatIgnoreFactionLock = p.Length > 17 && p[17] == "1";
    }

    private void HandleCitySyncOnClient(string[] args)
    {
        citySyncScratch.Clear();
        if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
        {
            string[] items = args[1].Split(';');
            for (int i = 0; i < items.Length; i++)
            {
                string[] f = items[i].Split(',');
                if (f.Length >= 3 &&
                    uint.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint pid) &&
                    int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind) &&
                    int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lvl))
                {
                    citySyncScratch.Add((pid, kind, lvl));
                }
            }
        }

        DynamicCapAndTechService.Instance.ApplyClientCitySync(citySyncScratch);
        citySyncScratch.Clear();

        if (args.Length > 2)
        {
            MdsBridgeBootstrap.ApplyClientReservedCapsAction?.Invoke(args[2]);
        }
    }

    private static void ParseRemoteRoadArgs(
        string waypointsStr,
        string forcedStr,
        List<GlobalPosition> outWaypoints,
        List<bool> outForced)
    {
        outWaypoints.Clear();
        outForced.Clear();

        if (!string.IsNullOrEmpty(waypointsStr))
        {
            string[] pts = waypointsStr.Split(';');
            for (int i = 0; i < pts.Length; i++)
            {
                string[] c = pts[i].Split(',');
                if (c.Length >= 3 &&
                    float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    outWaypoints.Add(new GlobalPosition(x, y, z));
                }
            }
        }

        if (!string.IsNullOrEmpty(forcedStr))
        {
            string[] fl = forcedStr.Split(',');
            for (int i = 0; i < fl.Length; i++)
            {
                outForced.Add(fl[i] == "1");
            }
        }
    }

    // --- Transport / Chunking / Player Resolution ---

    private void PruneDisconnectedPlayers(IReadOnlyList<INetworkPlayer> players)
    {
        var active = new HashSet<INetworkPlayer>();
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null) active.Add(players[i]);
        }

        prunePlayersScratch.Clear();
        foreach (INetworkPlayer p in moddedGuests)
        {
            if (!active.Contains(p)) prunePlayersScratch.Add(p);
        }

        for (int i = 0; i < prunePlayersScratch.Count; i++)
        {
            moddedGuests.Remove(prunePlayersScratch[i]);
            lastAbmDigestByGuest.Remove(prunePlayersScratch[i]);
        }

        prunePlayersScratch.Clear();
        foreach (INetworkPlayer p in knownAuthPlayers)
        {
            if (!active.Contains(p)) prunePlayersScratch.Add(p);
        }

        for (int i = 0; i < prunePlayersScratch.Count; i++)
        {
            knownAuthPlayers.Remove(prunePlayersScratch[i]);
        }
    }

    internal static FactionHQ? FindHqByName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null && hq.faction != null && string.Equals(hq.faction.factionName, name, StringComparison.OrdinalIgnoreCase))
            {
                return hq;
            }
        }

        return null;
    }

    private static FactionHQ? ResolvePlayerHq(INetworkPlayer? player)
    {
        if (player == null)
        {
            return null;
        }

        NetworkIdentity? identity = ((IObjectOwner)player).Identity;
        if (identity != null && identity.TryGetComponent(out Player pComp) && pComp.HQ != null)
        {
            return pComp.HQ;
        }

        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null) continue;
            List<Player> list = hq.GetPlayers(false);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && ((NetworkBehaviour)list[i]).Owner == player)
                {
                    return hq;
                }
            }
        }

        return null;
    }

    private void SendToAllModdedGuests(string payload)
    {
        if (moddedGuests.Count == 0 || string.IsNullOrEmpty(payload))
        {
            return;
        }

        List<string> chunks = SplitPayload(payload);
        foreach (INetworkPlayer player in moddedGuests)
        {
            if (player == null) continue;
            for (int c = 0; c < chunks.Count; c++)
            {
                player.Send(new GcreNetMessage { Text = chunks[c] });
            }
        }
    }

    private void SendToPlayer(INetworkPlayer player, string payload)
    {
        if (player == null || string.IsNullOrEmpty(payload))
        {
            return;
        }

        List<string> chunks = SplitPayload(payload);
        for (int i = 0; i < chunks.Count; i++)
        {
            player.Send(new GcreNetMessage { Text = chunks[i] });
        }
    }

    private void SendToHost(string payload)
    {
        NetworkClient? client = NetworkManagerNuclearOption.i != null ? NetworkManagerNuclearOption.i.Client : null;
        if (client == null || !client.Active || string.IsNullOrEmpty(payload))
        {
            return;
        }

        List<string> chunks = SplitPayload(payload);
        for (int i = 0; i < chunks.Count; i++)
        {
            client.Send(new GcreNetMessage { Text = chunks[i] });
        }
    }

    private List<string> SplitPayload(string payload)
    {
        pieceScratch.Clear();
        if (Encoding.UTF8.GetByteCount(payload) <= MaxWireBytes)
        {
            pieceScratch.Add(payload);
            return pieceScratch;
        }

        int id = ++nextPieceId;
        int maxDataBytes = MaxWireBytes - 48;
        var rawPieces = new List<string>();
        int start = 0;
        while (start < payload.Length)
        {
            int len = Math.Min(payload.Length - start, maxDataBytes);
            while (len > 1 && Encoding.UTF8.GetByteCount(payload.Substring(start, len)) > maxDataBytes)
            {
                len = Math.Max(1, len * 3 / 4);
            }

            rawPieces.Add(payload.Substring(start, len));
            start += len;
        }

        int total = rawPieces.Count;
        for (int i = 0; i < total; i++)
        {
            pieceScratch.Add(Join(
                PartVerb,
                id.ToString(CultureInfo.InvariantCulture),
                i.ToString(CultureInfo.InvariantCulture),
                total.ToString(CultureInfo.InvariantCulture),
                rawPieces[i]));
        }

        return pieceScratch;
    }

    private string? TryReassemble(string senderTag, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] parts = text.Split(Sep);
        if (parts.Length == 0 || parts[0] != PartVerb)
        {
            return text;
        }

        if (parts.Length < 5 ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int total))
        {
            return null;
        }

        if (total <= 0 || total > MaxPiecesPerPayload || idx < 0 || idx >= total)
        {
            return null;
        }

        float now = Time.unscaledTime;
        PruneStalePieces(now);

        string key = senderTag + ":" + id.ToString(CultureInfo.InvariantCulture);
        if (!pieceBuffers.TryGetValue(key, out string?[] buf))
        {
            if (pieceBuffers.Count >= MaxOpenPayloadsPerSender * 4)
            {
                return null;
            }

            buf = new string?[total];
            pieceBuffers[key] = buf;
            pieceStartedAt[key] = now;
        }
        else if (buf.Length != total)
        {
            pieceBuffers.Remove(key);
            pieceStartedAt.Remove(key);
            return null;
        }

        buf[idx] = parts[4];
        for (int i = 0; i < buf.Length; i++)
        {
            if (buf[i] == null)
            {
                return null;
            }
        }

        pieceBuffers.Remove(key);
        pieceStartedAt.Remove(key);
        return string.Concat(buf);
    }

    private void PruneStalePieces(float now)
    {
        if (pieceStartedAt.Count == 0)
        {
            return;
        }

        stalePieceScratch.Clear();
        foreach (KeyValuePair<string, float> kv in pieceStartedAt)
        {
            if (now - kv.Value > PieceTimeoutSeconds)
            {
                stalePieceScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < stalePieceScratch.Count; i++)
        {
            pieceStartedAt.Remove(stalePieceScratch[i]);
            pieceBuffers.Remove(stalePieceScratch[i]);
        }
    }

    private static string Join(params string[] parts)
    {
        return string.Join(SepText, parts);
    }
}
