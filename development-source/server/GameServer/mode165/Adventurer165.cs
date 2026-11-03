using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI;
using DOL.AI.Brain;
using DOL.GS.Movement;

namespace DOL.GS
{
    /// <summary>
    /// The traveler's inns' adventurers (BotRealm; owner 2026-10-30, as in the old Rebuilt project): three per realm
    /// ("albion adventurer", "midgard adventurer", "hibernia adventurer"), level-15 guards of their realm with basic
    /// weapons, patrolling the road between the inn yard and the realm's nearby town (Hibernia: Connla; Albion: the near
    /// edge of the Marsh Town bridge; Midgard: Huginfell). Each idles around the yard, walks the road to the town, idles
    /// there a while and walks back; each at its own pace and times, so they never all move at once.
    /// They walk (never run), one road point after the next without stopping, in straight lines between the road's dense
    /// points so they keep to it. A fight comes first: they fight aggressive monsters like guards and afterwards walk back
    /// onto their route where they left it, as patrolling guards do (owner 2026-10-30). The guard brain's own "return
    /// to spawn" is switched off for them (it kept pulling them back at a run: the stop-start the owner saw).
    /// The route is the Path "true165_adv_&lt;Mob_ID&gt;" written by the inn builder: the yard first, the town end last.
    /// </summary>
    public class Adventurer165 : GameGuard
    {
        private enum Phase { Lounging, Out, Idling, Back }

        private const int TickMs = 400;
        private readonly Random _rng = new();
        private List<Vector3> _route = [];
        private Phase _phase = Phase.Lounging;
        private int _leg;
        private long _until;
        private long _nextWander;
        private short _pace;
        private bool _fought;
        private ECSGameTimer _timer;

        /// <summary>A basic weapon by realm when the borrowed look carries none.</summary>
        private static (eInventorySlot Slot, int Model, eActiveWeaponSlot Active) BasicWeapon(eRealm realm) => realm switch
        {
            eRealm.Midgard => (eInventorySlot.TwoHandWeapon, 575, eActiveWeaponSlot.TwoHanded),
            eRealm.Hibernia => (eInventorySlot.RightHandWeapon, 473, eActiveWeaponSlot.Standard),
            _ => (eInventorySlot.RightHandWeapon, 1, eActiveWeaponSlot.Standard),
        };

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;
            if (!Classic165Mode.IsOn)
                return true;
            // once: a respawn comes back through AddToWorld with this brain already running, and setting a running brain
            // again threw and removed the adventurer from the world (2026-10-30 09:16-09:18)
            if (Brain is not AdventurerBrain)
                SetOwnBrain(new AdventurerBrain { Body = this });
            EnsureWeapon();
            _route = LoadRoute();
            // a walk: about the speed a player walks, a little different for each
            _pace = (short)_rng.Next(75, 96);
            if (_route.Count >= 2 && Vector2.Distance(new(X, Y), Flat(_route[0])) > 300)
            {
                // respawned out on its patrol: head home from the nearest point of the route
                _phase = Phase.Back;
                _leg = Nearest();
                Go();
            }
            else
            {
                _phase = Phase.Lounging;
                _until = GameLoop.GameLoopTime + _rng.Next(20_000, 150_000);   // each starts at its own time
            }
            _timer ??= new ECSGameTimer(this, _ => Tick());
            _timer.Start(TickMs);
            return true;
        }

        public override bool RemoveFromWorld()
        {
            _timer?.Stop();
            _timer = null;
            return base.RemoveFromWorld();
        }

        private void EnsureWeapon()
        {
            var items = Inventory?.AllItems ?? [];
            if (items.Any(i => i != null && i.SlotPosition is >= (int)eInventorySlot.RightHandWeapon and <= (int)eInventorySlot.DistanceWeapon))
                return;
            var template = new GameNpcInventoryTemplate();
            foreach (var item in items.Where(i => i != null))
                template.AddNPCEquipment((eInventorySlot)item.SlotPosition, item.Model, item.Color, item.Effect, item.Extension);
            (eInventorySlot slot, int model, eActiveWeaponSlot active) = BasicWeapon(Realm);
            template.AddNPCEquipment(slot, model);
            Inventory = template.CloseTemplate();
            SwitchWeapon(active);
            BroadcastLivingEquipmentUpdate();
        }

        private List<Vector3> LoadRoute()
        {
            var points = new List<Vector3>();
            for (PathPoint p = MovementMgr.LoadPath("true165_adv_" + InternalID); p != null && points.Count < 1024; p = p.Next)
                points.Add(new Vector3(p.X, p.Y, p.Z));
            return points;
        }

        private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
        private float DistanceTo(Vector3 v) => Vector2.Distance(new(X, Y), Flat(v));

        /// <summary>The route point closest to where it stands.</summary>
        private int Nearest() => Enumerable.Range(0, _route.Count).OrderBy(i => DistanceTo(_route[i])).First();

        private void Go() => WalkTo(_route[_leg], _pace);   // a straight line along the road to its next point

        private bool Fighting => InCombat || IsAttacking || Brain is StandardMobBrain { HasAggro: true };

        private int Tick()
        {
            if (!IsAlive || ObjectState != eObjectState.Active || _route.Count < 2)
                return TickMs;
            if (Fighting)
            {
                _fought = true;   // a guard first: the fight decides
                return TickMs;
            }
            long now = GameLoop.GameLoopTime;
            if (_fought)
            {
                // back onto the route where it left it (as patrolling guards do), then on in the same direction
                _fought = false;
                if (_phase is Phase.Out or Phase.Back)
                {
                    _leg = Nearest();
                    Go();
                }
                else
                {
                    WalkTo(_phase == Phase.Lounging ? _route[0] : _route[^1], _pace);
                    _nextWander = now + _rng.Next(8_000, 20_000);
                }
                return TickMs;
            }
            switch (_phase)
            {
                case Phase.Lounging:
                case Phase.Idling:
                    if (now >= _until)
                    {
                        _phase = _phase == Phase.Lounging ? Phase.Out : Phase.Back;
                        _leg = _phase == Phase.Out ? 1 : _route.Count - 2;
                        Go();
                        return TickMs;
                    }
                    // idle around the spot: a few steps now and then
                    if (now >= _nextWander && !IsMoving)
                    {
                        Vector3 home = _phase == Phase.Lounging ? _route[0] : _route[^1];
                        double a = _rng.NextDouble() * Math.PI * 2;
                        double r = _rng.NextDouble() * 110;
                        WalkTo(new Vector3(home.X + (float)(Math.Cos(a) * r), home.Y + (float)(Math.Sin(a) * r), home.Z), 60);
                        _nextWander = now + _rng.Next(10_000, 30_000);
                    }
                    return TickMs;
                default:
                    // the next point is taken while still walking toward this one, so the walk never stops between
                    // points (a stop and a restart at every road point was the stutter)
                    float reach = Math.Max(30f, _pace * 0.6f);
                    if (DistanceTo(_route[_leg]) > reach)
                    {
                        if (!IsMoving)
                            Go();   // a blocked step: try again
                        return TickMs;
                    }
                    bool outward = _phase == Phase.Out;
                    _leg += outward ? 1 : -1;
                    if (_leg >= _route.Count || _leg < 0)
                    {
                        // arrived: idle at the town end for a while, or around the inn yard
                        _phase = outward ? Phase.Idling : Phase.Lounging;
                        _until = now + (outward ? _rng.Next(40_000, 150_000) : _rng.Next(60_000, 240_000));
                        _nextWander = now + _rng.Next(5_000, 12_000);
                        return TickMs;
                    }
                    Go();
                    return TickMs;
            }
        }
    }

    /// <summary>The adventurers' guard brain: enemies of the realm as a guard, but among monsters only the aggressive
    /// ones (or one already fighting someone of the realm), so a long road patrol isn't spent on rabbits. It never sends
    /// the adventurer back to its spawn point: the adventurer walks its own route (Adventurer165.Tick).</summary>
    public class AdventurerBrain : GuardBrain
    {
        public override int AggroRange => 500;

        public AdventurerBrain()
        {
            FSM.Add(new OwnRouteIdleState165(this));
            FSM.Add(new OwnRouteReturnState165(this));
        }

        protected override void CheckNpcAggro()
        {
            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)AggroRange))
            {
                if (npc is GameGuard || !CanAggroTarget(npc) || (npc.Flags & GameNPC.eFlags.FLYING) != 0)
                    continue;
                bool aggressive = npc.Brain is IOldAggressiveBrain { AggroLevel: > 0 } ||
                                  npc.InCombat && npc.TargetObject is GameLiving { } target && target.Realm == Body.Realm;
                if (!aggressive)
                    continue;
                AddToAggroList(npc);
                return;
            }
        }
    }

    /// <summary>Idle for an NPC that walks its own route (Adventurer165, Traveler165): never stops the walk or heads for
    /// the spawn point; only watches for enemies when the brain has aggro.</summary>
    public sealed class OwnRouteIdleState165 : StandardMobState
    {
        public OwnRouteIdleState165(StandardMobBrain brain) : base(brain) => StateType = eFSMStateType.IDLE;

        public override void Think()
        {
            if (_brain.AggroLevel > 0 && _brain.CheckProximityAggro())
                _brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
        }
    }

    /// <summary>"Return to spawn" for an NPC that walks its own route: straight back to idle (it returns to its route by
    /// itself).</summary>
    public sealed class OwnRouteReturnState165 : StandardMobState
    {
        public OwnRouteReturnState165(StandardMobBrain brain) : base(brain) => StateType = eFSMStateType.RETURN_TO_SPAWN;

        public override void Enter()
        {
            _brain.ClearAggroList();
            _brain.FSM.SetCurrentState(eFSMStateType.IDLE);
        }
    }

    /// <summary>The travelers' brain: peaceful, and no walking back to the spawn point (Traveler165 picks its own spots).</summary>
    public class TravelerBrain165 : StandardMobBrain
    {
        public override int AggroLevel => 0;
        public override int AggroRange => 0;

        public TravelerBrain165()
        {
            FSM.Add(new OwnRouteIdleState165(this));
            FSM.Add(new OwnRouteReturnState165(this));
        }
    }

    /// <summary>
    /// The inns' travelers (BotRealm; owner 2026-10-30): three peaceful townsfolk per inn ("albion traveler",
    /// "midgard traveler", "hibernia traveler") for ambience, wandering the inn's rooms, downstairs and up the stairs to
    /// the upstairs hallways, stopping a while at each spot. Spots come from the navmesh around the inn: reachable from
    /// where they stand (so never the roof), indoors (a floor or roof above them) and on the inn's floors, so they don't
    /// wander out into the yard. A spot is on a floor (the ground floor or upstairs), never on furniture, and not behind
    /// the bar; a walk on one floor must stay on that floor the whole way (owner 2026-10-30: one stood on the counter).
    /// Without a navmesh they take a few steps around their spot.
    /// </summary>
    public class Traveler165 : GameNPC
    {
        private const int Reach = 420;        // how far from its spawn spot a traveler roams (the inn's rooms)
        private readonly Random _rng = new();
        private ECSGameTimer _timer;
        private long _next;
        private short _pace;
        private Vector3 _home;

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;
            if (!Classic165Mode.IsOn)
                return true;
            if (Brain is not TravelerBrain165)
                SetOwnBrain(new TravelerBrain165 { Body = this });   // once (see Adventurer165.AddToWorld)
            _home = new Vector3(SpawnPoint.X, SpawnPoint.Y, SpawnPoint.Z);
            _pace = (short)_rng.Next(55, 76);
            _next = GameLoop.GameLoopTime + _rng.Next(5_000, 40_000);
            _timer ??= new ECSGameTimer(this, _ => Tick());
            _timer.Start(1000);
            return true;
        }

        public override bool RemoveFromWorld()
        {
            _timer?.Stop();
            _timer = null;
            return base.RemoveFromWorld();
        }

        private int Tick()
        {
            if (!IsAlive || ObjectState != eObjectState.Active || IsMoving || InCombat)
                return 1000;
            long now = GameLoop.GameLoopTime;
            if (now < _next)
                return 1000;
            Vector3? spot = PickSpot();
            if (spot.HasValue)
                PathTo(spot.Value, _pace);
            else
            {
                double a = _rng.NextDouble() * Math.PI * 2;
                double r = _rng.NextDouble() * 70;
                WalkTo(new Vector3(_home.X + (float)(Math.Cos(a) * r), _home.Y + (float)(Math.Sin(a) * r), _home.Z), _pace);
            }
            // a while at each spot: a short look around, now and then a long stay
            _next = now + (_rng.Next(4) == 0 ? _rng.Next(60_000, 150_000) : _rng.Next(12_000, 45_000));
            return 1000;
        }

        private Vector3? PickSpot()
        {
            Zone zone = CurrentZone;
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.HasNavmesh(zone))
                return null;
            Vector3 here = new(X, Y, Z);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // from where it stands, so the spot is reachable from here (Detour's random point keeps to the
                // walkable area connected to its start: the stairs, not the roof)
                Vector3? p = nav.GetRandomPoint(zone, here, 320, nav.DefaultFilters);
                if (!p.HasValue)
                    continue;
                Vector3 v = p.Value;
                if (Vector2.Distance(new(v.X, v.Y), new(_home.X, _home.Y)) > Reach || v.Z < _home.Z - 15 || v.Z > _home.Z + 400)
                    continue;
                // indoors: another floor or the roof above (the yard outside has open sky)
                if (nav.GetRoofAbove(zone, v, 900, nav.DefaultFilters) == null)
                    continue;
                if (Vector2.Distance(new(v.X, v.Y), new(X, Y)) < 60)
                    continue;
                // on a floor: the ground floor, or upstairs (counters, tables and stair steps are in between)
                bool ground = Math.Abs(v.Z - _home.Z) <= 12;
                if (!ground && v.Z < _home.Z + 150)
                    continue;
                // not behind the bar
                if (GetNPCsInRadius(900).OfType<Innkeeper165>().Any(k => Vector2.Distance(new(v.X, v.Y), new(k.X, k.Y)) < 140))
                    continue;
                if (!WalkStaysOnFloor(nav, zone, here, v, ground))
                    continue;
                return v;
            }
            return null;
        }

        /// <summary>The navmesh walk there: complete, and on one floor when both ends are on it (no climbing over the
        /// counter on the way); between floors it takes the stairs.</summary>
        private bool WalkStaysOnFloor(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to, bool toGround)
        {
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[128];
            PathfindingResult result;
            try
            {
                result = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
            }
            catch
            {
                return false;
            }
            if (result.Status != PathfindingStatus.PathFound || result.NodeCount <= 0)
                return false;
            bool fromGround = Math.Abs(from.Z - _home.Z) <= 12;
            if (fromGround != toGround)
                return true;
            for (int i = 0; i < result.NodeCount; i++)
                if (Math.Abs(nodes[i].Position.Z - to.Z) > 20)
                    return false;
            return true;
        }
    }
}
