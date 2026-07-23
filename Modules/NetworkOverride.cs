using NeonLite.Modules;
using UnityEngine;
using NeonNetwork.Online;
using UnityEngine.Networking;
using MelonLoader.TinyJSON;
using TMPro;

namespace NMOConnect.Modules
{

    [Module]
    internal static class NetworkOverride
    {
        const bool priority = true;
        const bool active = true;

        const ushort HID = 0x7870;

        static TextMeshProUGUI roomsC;
        internal static TextMeshProUGUI buildText;

        static void Activate(bool _)
        {
            Rooms.RegisterCustomHandler(HID, ChatMsgHandler);
            // Rooms.RegisterPostHandler(Rooms.LobbyChatOp.RaceCall, OnRaceCall);
            Rooms.RegisterPostHandler(Rooms.LobbyChatOp.RacePB, OnRacePB);
            Rooms.RegisterPostHandler(Rooms.LobbyChatOp.RaceLastRun, OnRacePB);

            Rooms.RegisterPostHandler(Rooms.LobbyChatOp.Leave, OnPlayerExit);
            Rooms.RegisterPostHandler(Rooms.LobbyChatOp.Kick, OnPlayerExit);

            Patching.AddPatch(typeof(Rooms), "CallRace", OnRaceCall, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(Rooms), "StartRace", OnRaceStart, Patching.PatchTarget.Postfix);
            Patching.AddPatch(typeof(Rooms), "StopRace", OnRaceCancel, Patching.PatchTarget.Prefix);

            Patching.AddPatch(typeof(Rooms), "LeaveRoom", Reset, Patching.PatchTarget.Prefix);

            var type = Type.GetType("NeonNetwork.Objects.SidePanel.Contents.RoomBase, NeonNetwork");
            Patching.AddPatch(type, "Start", SetRoomsC, Patching.PatchTarget.Postfix);
            type = Type.GetType("NeonNetwork.Objects.Other.Version, NeonNetwork");
            Patching.AddPatch(type, "Start", SetBuildT, Patching.PatchTarget.Postfix);
        }

        static string tourneyID = null;
        static readonly HashSet<ulong> players = [];
        static string duelID = null;
        static long lastSentPB;

        enum NetworkOpcodes : byte
        {
            HasNMO = 0x00,
            DuelID = 0x10
        }

        static void ChatMsgHandler(BinaryReader reader, BinaryWriter writer, ulong id)
        {
            var opcode = (NetworkOpcodes)reader.ReadByte();

            bool fromOwner = Rooms.owner.m_SteamID == id;
            bool fromSelf = Online.steamID.m_SteamID == id;

            switch (opcode)
            {
                case NetworkOpcodes.HasNMO:
                    {
                        if (Rooms.owner == Online.steamID)
                        { // if we're owner
                            // if we have no tournament and a player doesn't have always set,
                            // don't remove so we never start the duel on the serverside
                            if (tourneyID == null && !reader.ReadBoolean())
                                break;

                            players.Remove(id);
                            if (players.Count != 0)
                                break;

                            players.UnionWith(Rooms.inRoom.Keys);

                            // make a duel do duelID stuff
                            var packet = new Online.Requests.DuelCreate()
                            {
                                level_id = Rooms.raceLevel.levelID,
                                users = [..
                                        Rooms.inRoom.Keys
                                        .Append(Online.steamID.m_SteamID)
                                        .Select(x => x.ToString())],
                                length = Rooms.raceTimeLim,
                                tourney_id = tourneyID
                            };

                            Online.Post("/duels/create", packet, req =>
                            {
                                if (req.result != UnityWebRequest.Result.Success)
                                    return;

                                var variant = JSON.Load(req.downloadHandler.text);
                                duelID = variant["duel_id"];

                                // writer should totally still b valid
                                writer.Write((byte)Rooms.LobbyChatOp.Custom);
                                writer.Write(HID);
                                writer.Write((byte)NetworkOpcodes.DuelID);
                                writer.Write(duelID);
                                Rooms.SendLobbyMsg();
                            });
                            break;
                        }

                        if (!fromOwner)
                            break;

                        writer.Write((byte)Rooms.LobbyChatOp.Custom);
                        writer.Write(HID);
                        writer.Write((byte)NetworkOpcodes.HasNMO);
                        writer.Write(NMOConnect.Settings.always.Value);

                        Rooms.SendLobbyMsg();
                        break;
                    }
                case NetworkOpcodes.DuelID:
                    {
                        if (!fromOwner)
                            break;
                        duelID = reader.ReadString();
                        if (roomsC)
                            roomsC.text = NMOConnect.LC.T("ROOMS_DUELID").Replace("{0}", duelID);
                        break;
                    }
            }
        }

        static void Reset()
        {
            tourneyID = null;
            players.Clear();
            lastSentPB = long.MaxValue;
            duelID = null;
            if (roomsC)
                roomsC.GetComponent<AxKLocalizedText>().Localize();
        }

        static bool tidOutgoing = false;
        static bool didBuffer = false;

        static void OnRaceStart()
        {
            NMOConnect.Log.DebugMsg("OnRaceStart");

            if (Rooms.owner != Online.steamID)
                return;
#if !DEBUG
            if (Rooms.inRoom.Count == 0)
                return; // don't do this for empty rooms bro
#endif

            if (tourneyID == null)
            {
                if (tidOutgoing)
                {
                    didBuffer = true;
                    return;
                }
                if (!NMOConnect.Settings.always.Value)
                    return;
            }
            didBuffer = false;

            players.UnionWith(Rooms.inRoom.Keys);

            Rooms.chatMsgWriter.Write((byte)Rooms.LobbyChatOp.Custom);
            Rooms.chatMsgWriter.Write(HID);
            Rooms.chatMsgWriter.Write((byte)NetworkOpcodes.HasNMO);
            Rooms.chatMsgWriter.Write(NMOConnect.Settings.always.Value);
            Rooms.SendLobbyMsg();
        }

        static void OnRaceCall()
        {
            NMOConnect.Log.DebugMsg("OnRaceCall");

            if (Rooms.owner != Online.steamID)
                return;
#if !DEBUG
            if (Rooms.inRoom.Count == 0)
                return; // don't do this for empty rooms bro
#endif
            Reset();


            tidOutgoing = true;
            Online.Get("/tourney/current", req =>
            {
                tidOutgoing = false;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    tourneyID = null;
                    if (!NMOConnect.Settings.always.Value)
                        return;
                }
                else
                {
                    var variant = JSON.Load(req.downloadHandler.text);
                    tourneyID = variant["tourney_id"];
                }
                if (didBuffer)
                    OnRaceStart();
            });
        }

        static void OnRacePB(BinaryReader reader, BinaryWriter _, ulong id)
        {
            NMOConnect.Log.DebugMsg("OnRacePB");

            if (id != Online.steamID.m_SteamID)
                return;
            if (duelID == null)
                return;

            var opcode = (Rooms.LobbyChatOp)reader.ReadByte();
            var readTime = reader.ReadInt64();

            var packet = new Online.Requests.DuelTime()
            {
                igt = Rooms.racingIGT,
                rta = Rooms.racingRTA
            };

            if (opcode == Rooms.LobbyChatOp.RaceLastRun)
            {
                packet.final = true;
                if (lastSentPB == readTime)
                    packet.time = null;
                else
                    packet.time = readTime.ToString();
            }
            else
                packet.time = readTime.ToString();

            Online.Post($"/duels/{duelID}/time", packet, null);
            lastSentPB = readTime;
        }

        static void OnPlayerExit(BinaryReader reader, BinaryWriter _, ulong id)
        {
            var opcode = (Rooms.LobbyChatOp)reader.ReadByte();

            if (opcode == Rooms.LobbyChatOp.Kick)
                id = reader.ReadUInt64();

            if (!players.Contains(id))
                return;

            var packet = new Online.Requests.DuelDeleteRunner()
            {
                steam_id = id.ToString(),
            };

            players.Remove(id);
            if (duelID != null)
                Online.Delete($"/duels/{duelID}/runner", null, packet);
        }

        static void OnRaceCancel()
        {
            var wasduel = duelID;
            Reset();

            if (Rooms.owner != Online.steamID)
                return;
            if (wasduel == null)
                return;

            Online.Delete($"/duels/{wasduel}", null);
        }

        static void SetRoomsC(TextMeshProUGUI ___roomCode) => roomsC = ___roomCode;
        static void SetBuildT(MonoBehaviour __instance)
        {
            buildText = __instance.transform.Find("Build").GetComponent<TextMeshProUGUI>();
            buildText.overflowMode = TextOverflowModes.Overflow;
            buildText.enableWordWrapping = false;
            if (Online.userID != null && !buildText.text.Contains("|"))
                buildText.text = $"NMO User ID: {Online.userID} | " + buildText.text;
        }
    }
}
