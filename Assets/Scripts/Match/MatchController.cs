using UnityEngine;

namespace Vanguard.Match
{
    public enum MatchTeam
    {
        None,
        Alpha,
        Bravo
    }

    public enum MatchPhase
    {
        Matchmaking,
        Loading,
        Introduction,
        Playing,
        Ended
    }

    public enum GameMode
    {
        TeamDeathmatch,
        Domination,
        FreeForAll,
        SearchAndDestroy,
        GunGame
    }

    public sealed class MatchPlayer
    {
        public string Id;
        public string DisplayName;
        public MatchTeam Team;
        public int Score;
        public int Kills;
        public int Deaths;
        public int Assists;
        public int Ping;
        public bool IsLocal;
    }

    public sealed class MatchController : MonoBehaviour
    {
        [Header("Match Rules")]
        [SerializeField] private GameMode mode = GameMode.TeamDeathmatch;
        [SerializeField] private int scoreLimit = 50;
        [SerializeField] private float matchDurationSeconds = 600f;
        [SerializeField] private float introductionDuration = 3f;
        [SerializeField] private float respawnDelay = 2.5f;

        [Header("Scene References")]
        [SerializeField] private Transform[] alphaSpawns;
        [SerializeField] private Transform[] bravoSpawns;
        [SerializeField] private MatchPlayer[] remotePlayers;
        [SerializeField] private MatchHud hud;
        [SerializeField] private EndOfMatchPanel endPanel;

        private MatchPhase _phase;
        private float _elapsed;
        private readonly System.Collections.Generic.List<MatchPlayer> _players = new System.Collections.Generic.List<MatchPlayer>();
        private MatchPlayer _local;
        private Damageable _localDamageable;
        private FirstPerson.Player.FirstPersonController _localController;
        private bool _waitingToRespawn;
        private float _respawnAt;

        public MatchPhase Phase => _phase;
        public GameMode Mode => mode;
        public MatchPlayer LocalPlayer => _local;
        public float Elapsed => _elapsed;
        public float Remaining => Mathf.Max(0f, matchDurationSeconds - _elapsed);

        private void Awake()
        {
            _local = new MatchPlayer
            {
                Id = "local",
                DisplayName = "Operative",
                Team = MatchTeam.Alpha,
                IsLocal = true
            };
            _players.Add(_local);
            foreach (MatchPlayer player in remotePlayers)
            {
                if (player != null)
                    _players.Add(player);
            }
        }

        private void OnEnable()
        {
            SetPhase(MatchPhase.Matchmaking);
        }

        private void Update()
        {
            switch (_phase)
            {
                case MatchPhase.Matchmaking:
                    SetPhase(MatchPhase.Loading);
                    break;
                case MatchPhase.Loading:
                    SetPhase(MatchPhase.Introduction);
                    break;
                case MatchPhase.Introduction:
                    _elapsed += Time.deltaTime;
                    if (_elapsed >= introductionDuration)
                    {
                        _elapsed = 0f;
                        SetPhase(MatchPhase.Playing);
                    }
                    break;
                case MatchPhase.Playing:
                    UpdatePlaying();
                    break;
            }
        }

        private void UpdatePlaying()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= matchDurationSeconds)
            {
                EndMatch(GetLeadingTeam());
                return;
            }

            if (_waitingToRespawn && Time.time >= _respawnAt)
                RespawnLocal();

            if (mode == GameMode.TeamDeathmatch)
            {
                if (GetTeamScore(MatchTeam.Alpha) >= scoreLimit || GetTeamScore(MatchTeam.Bravo) >= scoreLimit)
                    EndMatch(GetLeadingTeam());
            }
        }

        public void RegisterLocalPlayer(Damageable damageable, FirstPerson.Player.FirstPersonController controller)
        {
            _localDamageable = damageable;
            _localController = controller;
            if (damageable != null)
                damageable.Killed += HandleLocalDeath;
            RespawnLocal();
        }

        public void UnregisterLocalPlayer()
        {
            if (_localDamageable != null)
                _localDamageable.Killed -= HandleLocalDeath;
            _localDamageable = null;
            _localController = null;
        }

        public void AwardKill(MatchPlayer killer, MatchPlayer victim, DamageLocation location, float distance)
        {
            if (killer == null || victim == null)
                return;
            killer.Kills++;
            victim.Deaths++;
            killer.Score += mode == GameMode.FreeForAll ? 1 : 1;
            if (_local != null && killer != _local && victim == _local)
                _local.Assists++;

            if (hud != null)
                hud.ShowKillFeed(killer.DisplayName, victim.DisplayName, location == DamageLocation.Head, distance > 45f);
        }

        public void AwardAssist(MatchPlayer assister, MatchPlayer victim)
        {
            if (assister == null || victim == null)
                return;
            assister.Assists++;
            assister.Score++;
        }

        public int GetTeamScore(MatchTeam team)
        {
            int score = 0;
            foreach (MatchPlayer player in _players)
            {
                if (player.Team == team)
                    score += player.Score;
            }
            return score;
        }

        public MatchTeam GetLeadingTeam()
        {
            int alpha = GetTeamScore(MatchTeam.Alpha);
            int bravo = GetTeamScore(MatchTeam.Bravo);
            return alpha >= bravo ? MatchTeam.Alpha : MatchTeam.Bravo;
        }

        public Transform GetSpawn(MatchTeam team)
        {
            Transform[] spawns = team == MatchTeam.Bravo ? bravoSpawns : alphaSpawns;
            if (spawns == null || spawns.Length == 0)
                return null;
            return spawns[Random.Range(0, spawns.Length)];
        }

        public void RespawnLocal()
        {
            _waitingToRespawn = false;
            if (_localController == null)
                return;
            Transform spawn = GetSpawn(_local.Team);
            if (spawn != null)
            {
                _localController.transform.position = spawn.position;
                _localController.transform.rotation = spawn.rotation;
            }
            if (_localDamageable != null)
                _localDamageable.Heal(_localDamageable.MaxHealth);
        }

        private void HandleLocalDeath(GameObject source)
        {
            _local.Deaths++;
            _waitingToRespawn = true;
            _respawnAt = Time.time + respawnDelay;
            if (hud != null)
                hud.ShowDeathNotice();
        }

        private void EndMatch(MatchTeam winner)
        {
            SetPhase(MatchPhase.Ended);
            if (endPanel != null)
                endPanel.Show(winner, _local, _players.ToArray());
        }

        private void SetPhase(MatchPhase phase)
        {
            _phase = phase;
            if (hud != null)
                hud.SetMatchPhase(phase);
        }
    }
}
