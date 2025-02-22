﻿// #define DISABLE_MISSIONS

using System;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
   partial class TraitorManager
    {
        public readonly Dictionary<Character.TeamType, Traitor.TraitorMission> Missions = new Dictionary<Character.TeamType, Traitor.TraitorMission>();

        public string GetCodeWords(Character.TeamType team) => Missions.TryGetValue(team, out var mission) ? mission.CodeWords : "";
        public string GetCodeResponse(Character.TeamType team) => Missions.TryGetValue(team, out var mission) ? mission.CodeResponse : "";

        public IEnumerable<Traitor> Traitors => Missions.Values.SelectMany(mission => mission.Traitors.Values);

        private float startCountdown = 0.0f;
        private GameServer server;

        private readonly Dictionary<ulong, int> traitorCountsBySteamId = new Dictionary<ulong, int>();
        private readonly Dictionary<string, int> traitorCountsByEndPoint = new Dictionary<string, int>();

        public int GetTraitorCount(Tuple<ulong, string> steamIdAndEndPoint)
        {
            if (steamIdAndEndPoint.Item1 > 0 && traitorCountsBySteamId.TryGetValue(steamIdAndEndPoint.Item1, out var steamIdResult))
            {
                return steamIdResult;
            }
            return traitorCountsByEndPoint.TryGetValue(steamIdAndEndPoint.Item2, out var endPointResult) ? endPointResult : 0;
        }

        public void SetTraitorCount(Tuple<ulong, string> steamIdAndEndPoint, int count)
        {
            if (steamIdAndEndPoint.Item1 > 0)
            {
                traitorCountsBySteamId[steamIdAndEndPoint.Item1] = count;
            }
            traitorCountsByEndPoint[steamIdAndEndPoint.Item2] = count;
        }

        public bool IsTraitor(Character character)
        {
            if (Traitors == null)
            {
                return false;
            }
            return Traitors.Any(traitor => traitor.Character == character);
        }

        public TraitorManager()
        {
        }

        public void Start(GameServer server)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (server == null) return;

            Traitor.TraitorMission.InitializeRandom();
            this.server = server;
            //TODO: configure countdowns in xml
            startCountdown = MathHelper.Lerp(90.0f, 180.0f, (float)Traitor.TraitorMission.RandomDouble());
            traitorCountsBySteamId.Clear();
            traitorCountsByEndPoint.Clear();
        }

        public void Update(float deltaTime)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (Missions.Any())
            {
                bool missionCompleted = false;
                bool gameShouldEnd = false;
                Character.TeamType winningTeam = Character.TeamType.None;
                foreach (var mission in Missions)
                {
                    mission.Value.Update(deltaTime, () =>
                    {
                        switch (mission.Key)
                        {
                            case Character.TeamType.Team1:
                                winningTeam = (winningTeam == Character.TeamType.None) ? Character.TeamType.Team2 : Character.TeamType.None;
                                break;
                            case Character.TeamType.Team2:
                                winningTeam = (winningTeam == Character.TeamType.None) ? Character.TeamType.Team1 : Character.TeamType.None;
                                break;
                            default:
                                break;
                        }
                        gameShouldEnd = true;
                    });
                    if (!gameShouldEnd && mission.Value.IsCompleted)
                    {
                        missionCompleted = true;
                        foreach (var traitor in mission.Value.Traitors.Values)
                        {
                            traitor.UpdateCurrentObjective("");
                        }
                    }
                }
                if (gameShouldEnd)
                {
                    GameMain.GameSession.WinningTeam = winningTeam;
                    GameMain.Server.EndGame();
                    return;
                }
                if (missionCompleted)
                {
                    Missions.Clear();
                    //TODO: configure countdowns in xml
                    startCountdown = MathHelper.Lerp(90.0f, 180.0f, (float)Traitor.TraitorMission.RandomDouble());
                }
            }
            else if (startCountdown > 0.0f && server.GameStarted)
            {
                startCountdown -= deltaTime;
                if (startCountdown <= 0.0f)
                {
                    int playerCharactersCount = server.ConnectedClients.Sum(client => client.Character != null && !client.Character.IsDead ? 1 : 0);
                    if (playerCharactersCount < server.ServerSettings.TraitorsMinPlayerCount)
                    {
                        startCountdown = 60.0f;
                        return;
                    }
                    if (GameMain.GameSession.Mission is CombatMission)
                    {
                        var teamIds = new[] { Character.TeamType.Team1, Character.TeamType.Team2 };
                        foreach (var teamId in teamIds)
                        {
                            var mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
                            if (mission != null)
                            {
                                Missions.Add(teamId, mission);
                            }
                        }
                        var canBeStartedCount = Missions.Sum(mission => mission.Value.CanBeStarted(server, this, mission.Key, "traitor") ? 1 : 0);
                        if (canBeStartedCount >= Missions.Count)
                        {
                            var startSuccessCount = Missions.Sum(mission => mission.Value.Start(server, this, mission.Key, "traitor") ? 1 : 0);
                            if (startSuccessCount >= Missions.Count)
                            {
                                return;
                            }
                        }
                    }
                    else
                    {
                        var mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
                        if (mission != null) {
                            if (mission.CanBeStarted(server, this, Character.TeamType.None, "traitor"))
                            {
                                if (mission.Start(server, this, Character.TeamType.None, "traitor"))
                                {
                                    Missions.Add(Character.TeamType.None, mission);
                                    return;
                                }
                            }
                        }
                    }
                    Missions.Clear();
                    startCountdown = 60.0f;
                }
            }
        }

        public string GetEndMessage()
        {
#if DISABLE_MISSIONS
            return "";
#endif
            if (GameMain.Server == null || !Missions.Any()) return "";

            return string.Join("\n\n", Missions.Select(mission => mission.Value.GlobalEndMessage));
        }

        public static T WeightedRandom<T>(ICollection<T> collection, Func<int, int> random, Func<T, int> readSelectedWeight, Action<T, int> writeSelectedWeight, int entryWeight, int selectionWeight) where T : class
        {
            var count = collection.Count;
            if (count <= 0)
            {
                return null;
            }
            var maxCount = entryWeight + collection.Max(readSelectedWeight);
            var totalWeight = collection.Sum(entry => maxCount - readSelectedWeight(entry));
            var selected = random(totalWeight);
            foreach (var entry in collection)
            {
                var weight = readSelectedWeight(entry);
                selected -= maxCount;
                selected += weight;
                if (selected <= 0)
                {
                    writeSelectedWeight(entry, weight + selectionWeight);
                    return entry;
                }
            }
            return null;
        }
    }
}
