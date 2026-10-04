using System;

namespace TriLink.Plugins.Thunder
{
    // Call Step serially once per logical tick. No clock, floating point, or
    // per-tick allocation. Random choices derive from Seed/Tick, without PRNG state.
    public sealed class GameEngine
    {
        public const int WorldWidth = 320;
        public const int WorldHeight = 400;
        public const int TickRate = 30;
        public const byte InputLeft = 1;
        public const byte InputRight = 2;
        public const byte InputUp = 4;
        public const byte InputDown = 8;
        public const byte InputFire = 16;
        public const byte InputMask = 31;
        public const int EnemyScout = 0;
        public const int EnemyWeaver = 1;
        public const int EnemyArmored = 2;
        public const int ExplosionEnemy = 0;
        public const int ExplosionPlayer = 1;

        public GameEngine(uint seed, bool cooperative)
        {
            Seed = seed;
            Cooperative = cooperative;
            Wave = 1;
            Players = new PlayerState[2];
            Enemies = new GameEntity[24];
            Bullets = new GameEntity[64];
            EnemyBullets = new GameEntity[48];
            Explosions = new GameEntity[16];
            Players[0] = NewPlayer(0);
            if (cooperative) Players[1] = NewPlayer(1);
        }

        public uint Seed { get; private set; }
        public bool Cooperative { get; private set; }
        public uint Tick { get; private set; }
        public int Score { get; private set; }
        public int Wave { get; private set; }
        public PlayerState[] Players { get; private set; }
        public GameEntity[] Enemies { get; private set; }
        public GameEntity[] Bullets { get; private set; }
        public GameEntity[] EnemyBullets { get; private set; }
        public GameEntity[] Explosions { get; private set; }
        public bool GameOver { get; private set; }

        public void Step(byte player0Input, byte player1Input)
        {
            if (GameOver) return;
            Tick = unchecked(Tick + 1);
            Wave = 1 + (int)(Tick / 900);
            UpdateExplosions();
            UpdatePlayer(0, (byte)(player0Input & InputMask));
            UpdatePlayer(1, (byte)(player1Input & InputMask));

            int spawnInterval = Math.Max(18, 48 - Math.Min(Wave, 15) * 2);
            if (Tick == 1 || Tick % (uint)spawnInterval == 0)
            {
                SpawnEnemy(RandomForTick(0));
                if (Cooperative && Tick != 1 && (Tick / (uint)spawnInterval) % 4 == 0)
                    SpawnEnemy(RandomForTick(1));
            }

            MoveBullets(Bullets, false);
            UpdateEnemies();
            MoveBullets(EnemyBullets, true);
            ResolveFriendlyHits();
            ResolvePlayerHits();
            GameOver = !Players[0].Active && !Players[1].Active;
        }

        private PlayerState NewPlayer(int player)
        {
            return new PlayerState
            {
                Active = true,
                X = StartX(player),
                Y = WorldHeight - 40,
                Lives = 3,
                InvulnerableTicks = 60,
            };
        }

        private int StartX(int player)
        {
            return Cooperative ? (player == 0 ? 120 : 200) : WorldWidth / 2;
        }

        private void UpdatePlayer(int index, byte input)
        {
            PlayerState player = Players[index];
            if (!player.Active || player.Lives <= 0)
            {
                player.Active = false;
                Players[index] = player;
                return;
            }

            if (player.InvulnerableTicks > 0) player.InvulnerableTicks--;
            int dx = ((input & InputRight) != 0 ? 3 : 0) - ((input & InputLeft) != 0 ? 3 : 0);
            int dy = ((input & InputDown) != 0 ? 3 : 0) - ((input & InputUp) != 0 ? 3 : 0);
            player.X = Clamp(player.X + dx, 8, WorldWidth - 8);
            player.Y = Clamp(player.Y + dy, 10, WorldHeight - 10);
            Players[index] = player;

            // Ships always fire. Holding fire doubles the cadence.
            uint cadence = (input & InputFire) != 0 ? 3U : 6U;
            if (Tick % cadence == 0)
                SpawnBullet(Bullets, player.X, player.Y - 12, 0, -7, index);
        }

        private uint RandomForTick(uint salt)
        {
            unchecked
            {
                uint value = Seed ^ (Tick * 0x9E3779B9U) ^ (salt * 0x85EBCA6BU);
                value ^= value >> 16;
                value *= 0x7FEB352DU;
                value ^= value >> 15;
                value *= 0x846CA68BU;
                return value ^ (value >> 16);
            }
        }

        private void SpawnEnemy(uint random)
        {
            int slot = FreeSlot(Enemies);
            if (slot < 0) return;
            int roll = (int)(random % 100);
            int kind = Wave >= 2 && roll < 18 ? EnemyArmored
                : roll < 48 ? EnemyWeaver : EnemyScout;
            int speed = Math.Min(4, 1 + (Wave - 1) / 4);
            Enemies[slot] = new GameEntity
            {
                Active = true,
                X = 16 + (int)((random >> 8) % (WorldWidth - 32)),
                Y = -12,
                Vx = kind == EnemyWeaver ? ((random & 1) == 0 ? -1 : 1) : 0,
                Vy = kind == EnemyArmored ? Math.Max(1, speed - 1) : speed,
                Health = kind == EnemyArmored ? 4 : kind == EnemyWeaver ? 2 : 1,
                Kind = kind,
            };
        }

        private void UpdateEnemies()
        {
            for (int index = 0; index < Enemies.Length; index++)
            {
                GameEntity enemy = Enemies[index];
                if (!enemy.Active) continue;
                enemy.Age++;
                if (enemy.Kind == EnemyWeaver && enemy.Age % 40 == 0) enemy.Vx = -enemy.Vx;
                enemy.X += enemy.Vx;
                enemy.Y += enemy.Vy;
                if (enemy.X < 14 || enemy.X > WorldWidth - 14)
                {
                    enemy.X = Clamp(enemy.X, 14, WorldWidth - 14);
                    enemy.Vx = -enemy.Vx;
                }
                if (enemy.Y > WorldHeight + 20) enemy.Active = false;
                Enemies[index] = enemy;
                int cadence = enemy.Kind == EnemyArmored ? 42 : enemy.Kind == EnemyWeaver ? 66 : 84;
                if (enemy.Active && enemy.Y >= 0 && enemy.Y < WorldHeight - 50
                    && enemy.Age % cadence == 0)
                {
                    int target = NearestPlayer(enemy.X, enemy.Y);
                    if (target >= 0)
                    {
                        int dx = Players[target].X - enemy.X;
                        int vx = dx < -24 ? -1 : dx > 24 ? 1 : 0;
                        SpawnBullet(EnemyBullets, enemy.X, enemy.Y + 10, vx,
                            Math.Min(4, 2 + (Wave - 1) / 4), enemy.Kind);
                    }
                }
            }
        }

        private int NearestPlayer(int x, int y)
        {
            int selected = -1;
            int bestDistance = int.MaxValue;
            for (int index = 0; index < Players.Length; index++)
            {
                if (!Players[index].Active) continue;
                int distance = Math.Abs(Players[index].X - x) + Math.Abs(Players[index].Y - y);
                if (distance < bestDistance)
                {
                    selected = index;
                    bestDistance = distance;
                }
            }
            return selected;
        }

        private static void SpawnBullet(GameEntity[] pool, int x, int y, int vx, int vy, int kind)
        {
            int slot = FreeSlot(pool);
            if (slot < 0) return;
            pool[slot] = new GameEntity
            {
                Active = true, X = x, Y = y, Vx = vx, Vy = vy, Health = 1, Kind = kind,
            };
        }

        private static void MoveBullets(GameEntity[] pool, bool enemyBullet)
        {
            for (int index = 0; index < pool.Length; index++)
            {
                GameEntity bullet = pool[index];
                if (!bullet.Active) continue;
                bullet.X += bullet.Vx;
                bullet.Y += bullet.Vy;
                bullet.Age++;
                if (bullet.X < -8 || bullet.X > WorldWidth + 8 || bullet.Y < -8
                    || bullet.Y > WorldHeight + 8 || bullet.Age > (enemyBullet ? 240 : 120))
                    bullet.Active = false;
                pool[index] = bullet;
            }
        }

        private void ResolveFriendlyHits()
        {
            for (int shot = 0; shot < Bullets.Length; shot++)
            {
                if (!Bullets[shot].Active) continue;
                for (int target = 0; target < Enemies.Length; target++)
                {
                    GameEntity enemy = Enemies[target];
                    if (!enemy.Active || !Overlaps(Bullets[shot].X, Bullets[shot].Y, 2, 4,
                        enemy.X, enemy.Y, EnemyHalfWidth(enemy.Kind), EnemyHalfHeight(enemy.Kind))) continue;
                    Bullets[shot].Active = false;
                    enemy.Health--;
                    if (enemy.Health <= 0)
                    {
                        enemy.Active = false;
                        SpawnExplosion(enemy.X, enemy.Y, ExplosionEnemy);
                        int points = enemy.Kind == EnemyArmored ? 50 : enemy.Kind == EnemyWeaver ? 20 : 10;
                        Score = Score > int.MaxValue - points ? int.MaxValue : Score + points;
                    }
                    Enemies[target] = enemy;
                    break;
                }
            }
        }

        private void ResolvePlayerHits()
        {
            for (int playerIndex = 0; playerIndex < Players.Length; playerIndex++)
            {
                if (!Players[playerIndex].Active) continue;
                for (int shot = 0; shot < EnemyBullets.Length; shot++)
                {
                    PlayerState player = Players[playerIndex];
                    if (!player.Active) break;
                    if (!EnemyBullets[shot].Active || !Overlaps(player.X, player.Y, 7, 8,
                        EnemyBullets[shot].X, EnemyBullets[shot].Y, 3, 3)) continue;
                    EnemyBullets[shot].Active = false;
                    if (player.InvulnerableTicks == 0) HurtPlayer(playerIndex);
                }
                for (int target = 0; target < Enemies.Length; target++)
                {
                    PlayerState player = Players[playerIndex];
                    if (!player.Active) break;
                    GameEntity enemy = Enemies[target];
                    if (!enemy.Active || !Overlaps(player.X, player.Y, 7, 8,
                        enemy.X, enemy.Y, EnemyHalfWidth(enemy.Kind), EnemyHalfHeight(enemy.Kind))) continue;
                    Enemies[target].Active = false;
                    SpawnExplosion(enemy.X, enemy.Y, ExplosionEnemy);
                    if (player.InvulnerableTicks == 0) HurtPlayer(playerIndex);
                }
            }
        }

        private void HurtPlayer(int index)
        {
            PlayerState player = Players[index];
            SpawnExplosion(player.X, player.Y, ExplosionPlayer);
            player.Lives--;
            player.Active = player.Lives > 0;
            player.InvulnerableTicks = player.Active ? 75 : 0;
            if (player.Active)
            {
                player.X = StartX(index);
                player.Y = WorldHeight - 40;
            }
            Players[index] = player;
        }

        private void SpawnExplosion(int x, int y, int kind)
        {
            int slot = FreeSlot(Explosions);
            if (slot < 0) return;
            Explosions[slot] = new GameEntity { Active = true, X = x, Y = y, Kind = kind };
        }

        private void UpdateExplosions()
        {
            for (int index = 0; index < Explosions.Length; index++)
            {
                if (!Explosions[index].Active) continue;
                Explosions[index].Age++;
                if (Explosions[index].Age >= 15) Explosions[index].Active = false;
            }
        }

        private static int FreeSlot(GameEntity[] pool)
        {
            for (int index = 0; index < pool.Length; index++)
                if (!pool[index].Active) return index;
            return -1;
        }

        private static int EnemyHalfWidth(int kind) { return kind == EnemyArmored ? 13 : kind == EnemyWeaver ? 8 : 7; }
        private static int EnemyHalfHeight(int kind) { return kind == EnemyArmored ? 11 : 8; }
        private static int Clamp(int value, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
        private static bool Overlaps(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        {
            return Math.Abs(ax - bx) <= aw + bw && Math.Abs(ay - by) <= ah + bh;
        }
    }
}
