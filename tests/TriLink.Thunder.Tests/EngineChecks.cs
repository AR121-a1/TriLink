using System;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    public static class EngineChecks
    {
        public static void Run(Action<bool, string> check)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            CheckInitialState(check);
            CheckMovementAndFire(check);
            CheckDeterminism(check);
            CheckShotCollisions(check);
            CheckPlayerDamage(check);
            CheckCooperativeDeath(check);
            CheckFixedPools(check);
            CheckLifetimeAndWaves(check);
        }

        private static void CheckInitialState(Action<bool, string> check)
        {
            var single = new GameEngine(7, false);
            var pair = new GameEngine(7, true);
            check(single.Tick == 0 && single.Score == 0 && single.Wave == 1 && !single.GameOver,
                "engine begins at tick zero with a live first wave");
            check(single.Players[0].Active && single.Players[0].Lives == 3 && !single.Players[1].Active,
                "single player reserves the second slot without activating it");
            check(pair.Players[0].Active && pair.Players[1].Active && pair.Players[0].X != pair.Players[1].X,
                "cooperative players start separately with three lives");
            check(pair.Players.Length == 2 && pair.Enemies.Length == 24 && pair.Bullets.Length == 64
                && pair.EnemyBullets.Length == 48 && pair.Explosions.Length == 16 && GameEngine.TickRate == 30,
                "entity budgets and fixed thirty hertz contract are retained");
        }

        private static void CheckMovementAndFire(Action<bool, string> check)
        {
            var movement = new GameEngine(7, false);
            movement.Step(GameEngine.InputLeft | GameEngine.InputUp, 0);
            check(movement.Players[0].X == 157 && movement.Players[0].Y == 357,
                "integer input moves the ship three pixels per axis");
            movement.Step(GameEngine.InputMask, 0);
            check(movement.Players[0].X == 157 && movement.Players[0].Y == 357,
                "opposite directional inputs cancel");
            movement.Players[0].InvulnerableTicks = 10000;
            for (int tick = 0; tick < 150; tick++) movement.Step(GameEngine.InputLeft | GameEngine.InputUp, 0);
            check(movement.Players[0].X == 8 && movement.Players[0].Y == 10,
                "ship movement clamps to the sprite-safe world boundary");

            var automatic = new GameEngine(1, false);
            var firing = new GameEngine(1, false);
            for (int tick = 0; tick < 3; tick++) { automatic.Step(0, 0); firing.Step(GameEngine.InputFire, 0); }
            check(Count(automatic.Bullets) == 0 && Count(firing.Bullets) == 1,
                "fire input doubles the automatic shooting cadence");
            for (int tick = 0; tick < 3; tick++) automatic.Step(0, 0);
            check(Count(automatic.Bullets) == 1 && automatic.Bullets[0].Kind == 0,
                "ship auto-fires without a fire input and identifies the bullet owner");
        }

        private static void CheckDeterminism(Action<bool, string> check)
        {
            var first = new GameEngine(0x12345678, true);
            var replay = new GameEngine(0x12345678, true);
            first.Players[0].InvulnerableTicks = replay.Players[0].InvulnerableTicks = 12000;
            first.Players[1].InvulnerableTicks = replay.Players[1].InvulnerableTicks = 12000;
            bool identical = true;
            for (uint tick = 0; tick < 10000; tick++)
            {
                byte input0 = (byte)((tick * 13 + tick / 37) & GameEngine.InputMask);
                byte input1 = (byte)((tick * 7 + tick / 19) & GameEngine.InputMask);
                first.Step(input0, input1);
                replay.Step(input0, input1);
                if (!SameWorld(first, replay)) { identical = false; break; }
            }
            check(identical && first.Tick == 10000,
                "ten thousand seeded cooperative ticks reproduce every field exactly");

            var masked = new GameEngine(3, true);
            var noisy = new GameEngine(3, true);
            for (int tick = 0; tick < 120; tick++) { masked.Step(31, 31); noisy.Step(255, 255); }
            check(SameWorld(masked, noisy), "unknown input bits cannot change deterministic simulation");
            var seedA = new GameEngine(1, false);
            var seedB = new GameEngine(2, false);
            seedA.Step(0, 0); seedB.Step(0, 0);
            check(!SameEntity(seedA.Enemies[0], seedB.Enemies[0]), "enemy generation responds to the selected seed");
        }

        private static void CheckShotCollisions(Action<bool, string> check)
        {
            var scout = new GameEngine(3, false);
            scout.Enemies[0] = Entity(100, 100, 1, GameEngine.EnemyScout);
            scout.Bullets[0] = Entity(100, 100, 1, 0);
            scout.Step(0, 0);
            check(!scout.Enemies[0].Active && !scout.Bullets[0].Active && scout.Score == 10,
                "friendly collision consumes one shot and scores a scout once");
            check(Count(scout.Explosions) == 1 && scout.Explosions[0].Age == 0,
                "enemy death creates a fresh pooled explosion");

            var armor = new GameEngine(3, false);
            armor.Enemies[0] = Entity(100, 100, 2, GameEngine.EnemyArmored);
            armor.Bullets[0] = Entity(100, 100, 1, 0);
            armor.Step(0, 0);
            check(armor.Enemies[0].Active && armor.Enemies[0].Health == 1 && armor.Score == 0,
                "armored enemies retain health and score only after destruction");
            armor.Bullets[0] = Entity(100, 100, 1, 0);
            armor.Step(0, 0);
            check(!armor.Enemies[0].Active && armor.Score == 50, "a final armored hit earns its distinct score");

            var overlapping = new GameEngine(3, false);
            overlapping.Enemies[0] = Entity(100, 100, 1, GameEngine.EnemyScout);
            overlapping.Enemies[1] = Entity(100, 100, 1, GameEngine.EnemyScout);
            overlapping.Bullets[0] = Entity(100, 100, 1, 0);
            overlapping.Step(0, 0);
            check(overlapping.Score == 10 && overlapping.Enemies[1].Active,
                "a shot cannot damage multiple overlapping enemies");
        }

        private static void CheckPlayerDamage(Action<bool, string> check)
        {
            var damage = new GameEngine(3, false);
            damage.Players[0].InvulnerableTicks = 0;
            for (int shot = 0; shot < 3; shot++)
                damage.EnemyBullets[shot] = Entity(damage.Players[0].X, damage.Players[0].Y, 1, 0);
            damage.Step(0, 0);
            check(damage.Players[0].Lives == 2 && damage.Players[0].Active
                && damage.Players[0].InvulnerableTicks == 75 && Count(damage.EnemyBullets) == 0,
                "multiple simultaneous hits cost one life and install a respawn shield");
            damage.EnemyBullets[0] = Entity(damage.Players[0].X, damage.Players[0].Y, 1, 0);
            damage.Step(0, 0);
            check(damage.Players[0].Lives == 2 && !damage.EnemyBullets[0].Active,
                "respawn shield absorbs subsequent bullets without another lost life");
            damage.Players[0].InvulnerableTicks = 0;
            damage.Enemies[0] = Entity(damage.Players[0].X, damage.Players[0].Y, 1, GameEngine.EnemyScout);
            damage.Step(0, 0);
            check(damage.Players[0].Lives == 1 && !damage.Enemies[0].Active && damage.Score == 0,
                "ramming consumes an enemy and a life without awarding a shooting score");
        }

        private static void CheckCooperativeDeath(Action<bool, string> check)
        {
            var pair = new GameEngine(3, true);
            FatalHit(pair, 0);
            pair.Step(0, 0);
            check(!pair.Players[0].Active && pair.Players[0].Lives == 0 && pair.Players[1].Active && !pair.GameOver,
                "dead cooperative player spectates while their teammate continues");
            int deadX = pair.Players[0].X;
            pair.Step(GameEngine.InputRight | GameEngine.InputFire, GameEngine.InputLeft);
            check(pair.Players[0].X == deadX && pair.Players[1].X == 197,
                "spectator inputs are ignored and surviving player inputs still advance");
            FatalHit(pair, 1);
            pair.Step(0, 0);
            check(pair.GameOver && pair.Tick == 3 && !pair.Players[1].Active,
                "last cooperative death commits its tick and ends the game");
            uint digest = Digest(pair);
            pair.Step(31, 31);
            check(Digest(pair) == digest, "finished game freezes all public world state");
            var single = new GameEngine(3, false);
            FatalHit(single, 0);
            single.Step(0, 31);
            check(single.GameOver && !single.Players[1].Active,
                "inactive second slot never prevents single-player game over");
        }

        private static void CheckFixedPools(Action<bool, string> check)
        {
            var full = new GameEngine(3, false);
            GameEntity[] enemies = full.Enemies, shots = full.Bullets, hostile = full.EnemyBullets, effects = full.Explosions;
            for (int index = 0; index < enemies.Length; index++)
            { enemies[index] = Entity(20, 100, 99, 0); enemies[index].Age = 83; }
            for (int index = 0; index < shots.Length; index++) shots[index] = Entity(300, 50, 1, 77);
            for (int index = 0; index < hostile.Length; index++) hostile[index] = Entity(300, 250, 1, 77);
            for (int tick = 0; tick < 6; tick++) full.Step(0, 0);
            check(Count(enemies) == 24 && Count(shots) == 64 && Count(hostile) == 48
                && shots[0].Kind == 77 && hostile[0].Kind == 77,
                "full pools drop new enemies and bullets without replacing live entities");
            check(ReferenceEquals(enemies, full.Enemies) && ReferenceEquals(shots, full.Bullets)
                && ReferenceEquals(hostile, full.EnemyBullets) && ReferenceEquals(effects, full.Explosions),
                "simulation retains its original fixed pool arrays");

            var crowdedEffects = new GameEngine(3, false);
            for (int index = 0; index < crowdedEffects.Explosions.Length; index++)
                crowdedEffects.Explosions[index] = Entity(0, 0, 0, 77);
            crowdedEffects.Enemies[0] = Entity(100, 100, 1, 0);
            crowdedEffects.Bullets[0] = Entity(100, 100, 1, 0);
            crowdedEffects.Step(0, 0);
            check(crowdedEffects.Score == 10 && Count(crowdedEffects.Explosions) == 16
                && crowdedEffects.Explosions[0].Kind == 77,
                "full effect pool never blocks game damage or scoring");
        }

        private static void CheckLifetimeAndWaves(Action<bool, string> check)
        {
            var lifetime = new GameEngine(3, false);
            lifetime.Enemies[0] = Entity(20, 421, 1, 0);
            lifetime.Bullets[0] = Entity(-9, 100, 1, 0);
            lifetime.EnemyBullets[0] = Entity(20, 100, 1, 0);
            lifetime.EnemyBullets[0].Age = 240;
            lifetime.Explosions[0] = Entity(20, 100, 0, 0);
            lifetime.Explosions[0].Age = 14;
            lifetime.Step(0, 0);
            check(!lifetime.Enemies[0].Active && !lifetime.Bullets[0].Active
                && !lifetime.EnemyBullets[0].Active && !lifetime.Explosions[0].Active,
                "off-world entities and bounded-age effects release their slots");
            var waves = new GameEngine(3, true);
            waves.Players[0].InvulnerableTicks = waves.Players[1].InvulnerableTicks = 10000;
            for (int tick = 0; tick < 900; tick++) waves.Step(0, 0);
            check(waves.Tick == 900 && waves.Wave == 2 && !waves.GameOver,
                "wave difficulty advances after thirty seconds of logical ticks");
        }

        private static void FatalHit(GameEngine engine, int player)
        {
            engine.Players[player].Lives = 1;
            engine.Players[player].InvulnerableTicks = 0;
            engine.EnemyBullets[0] = Entity(engine.Players[player].X, engine.Players[player].Y, 1, 0);
        }

        private static GameEntity Entity(int x, int y, int health, int kind)
        { return new GameEntity { Active = true, X = x, Y = y, Health = health, Kind = kind }; }
        private static int Count(GameEntity[] pool)
        { int count = 0; for (int index = 0; index < pool.Length; index++) if (pool[index].Active) count++; return count; }

        private static bool SameWorld(GameEngine first, GameEngine second)
        {
            if (first.Seed != second.Seed || first.Cooperative != second.Cooperative || first.Tick != second.Tick
                || first.Score != second.Score || first.Wave != second.Wave || first.GameOver != second.GameOver) return false;
            for (int index = 0; index < first.Players.Length; index++)
            {
                PlayerState a = first.Players[index], b = second.Players[index];
                if (a.Active != b.Active || a.X != b.X || a.Y != b.Y || a.Lives != b.Lives
                    || a.InvulnerableTicks != b.InvulnerableTicks) return false;
            }
            return SamePool(first.Enemies, second.Enemies) && SamePool(first.Bullets, second.Bullets)
                && SamePool(first.EnemyBullets, second.EnemyBullets) && SamePool(first.Explosions, second.Explosions);
        }
        private static bool SamePool(GameEntity[] first, GameEntity[] second)
        {
            if (first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++) if (!SameEntity(first[index], second[index])) return false;
            return true;
        }
        private static bool SameEntity(GameEntity a, GameEntity b)
        {
            return a.Active == b.Active && a.X == b.X && a.Y == b.Y && a.Vx == b.Vx && a.Vy == b.Vy
                && a.Health == b.Health && a.Kind == b.Kind && a.Age == b.Age;
        }
        private static uint Digest(GameEngine engine)
        {
            uint result = engine.Tick ^ engine.Seed;
            Mix(ref result, engine.Score); Mix(ref result, engine.Wave);
            Mix(ref result, engine.Cooperative ? 1 : 0); Mix(ref result, engine.GameOver ? 1 : 0);
            for (int index = 0; index < engine.Players.Length; index++)
            {
                PlayerState player = engine.Players[index];
                Mix(ref result, player.Active ? 1 : 0); Mix(ref result, player.X); Mix(ref result, player.Y);
                Mix(ref result, player.Lives); Mix(ref result, player.InvulnerableTicks);
            }
            MixPool(ref result, engine.Enemies); MixPool(ref result, engine.Bullets);
            MixPool(ref result, engine.EnemyBullets); MixPool(ref result, engine.Explosions);
            return result;
        }
        private static void MixPool(ref uint result, GameEntity[] pool)
        {
            for (int index = 0; index < pool.Length; index++)
            {
                GameEntity entity = pool[index];
                Mix(ref result, entity.Active ? 1 : 0); Mix(ref result, entity.X); Mix(ref result, entity.Y);
                Mix(ref result, entity.Vx); Mix(ref result, entity.Vy); Mix(ref result, entity.Health);
                Mix(ref result, entity.Kind); Mix(ref result, entity.Age);
            }
        }
        private static void Mix(ref uint result, int value) { result = unchecked((result ^ (uint)value) * 16777619U); }
    }
}
