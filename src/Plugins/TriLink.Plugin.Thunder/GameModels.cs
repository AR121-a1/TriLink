namespace TriLink.Plugins.Thunder
{
    // Coordinates are sprite centres in a 320 x 400 integer world.
    public struct PlayerState
    {
        public bool Active;
        public int X;
        public int Y;
        public int Lives;
        public int InvulnerableTicks;
    }

    // Enemy Kind: scout=0, weaver=1, armoured=2. Friendly bullet Kind is its
    // owner (0/1); explosion Kind: enemy=0, player=1. Age is measured in ticks.
    public struct GameEntity
    {
        public bool Active;
        public int X;
        public int Y;
        public int Vx;
        public int Vy;
        public int Health;
        public int Kind;
        public int Age;
    }
}
