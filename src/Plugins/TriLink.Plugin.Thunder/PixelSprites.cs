using System;
using System.Drawing;

namespace TriLink.Plugins.Thunder
{
    // All art is built from integer rectangles. The palette is allocated once per view.
    internal sealed class PixelSprites : IDisposable
    {
        private readonly SolidBrush _navy = new SolidBrush(Color.FromArgb(6, 12, 29));
        private readonly SolidBrush _panel = new SolidBrush(Color.FromArgb(13, 27, 48));
        private readonly SolidBrush _faint = new SolidBrush(Color.FromArgb(30, 54, 83));
        private readonly SolidBrush _star = new SolidBrush(Color.FromArgb(102, 139, 172));
        private readonly SolidBrush _white = new SolidBrush(Color.FromArgb(224, 247, 255));
        private readonly SolidBrush _cyan = new SolidBrush(Color.FromArgb(54, 226, 255));
        private readonly SolidBrush _blue = new SolidBrush(Color.FromArgb(32, 121, 213));
        private readonly SolidBrush _blueShadow = new SolidBrush(Color.FromArgb(18, 57, 120));
        private readonly SolidBrush _gold = new SolidBrush(Color.FromArgb(255, 221, 102));
        private readonly SolidBrush _orange = new SolidBrush(Color.FromArgb(244, 144, 56));
        private readonly SolidBrush _orangeShadow = new SolidBrush(Color.FromArgb(142, 61, 33));
        private readonly SolidBrush _red = new SolidBrush(Color.FromArgb(248, 81, 104));
        private readonly SolidBrush _redShadow = new SolidBrush(Color.FromArgb(113, 29, 64));
        private readonly SolidBrush _violet = new SolidBrush(Color.FromArgb(167, 105, 231));
        private readonly SolidBrush _violetShadow = new SolidBrush(Color.FromArgb(74, 46, 123));
        private readonly SolidBrush _green = new SolidBrush(Color.FromArgb(107, 223, 135));
        private readonly SolidBrush _greenShadow = new SolidBrush(Color.FromArgb(39, 100, 89));

        public Brush White { get { return _white; } }
        public Brush Cyan { get { return _cyan; } }
        public Brush Gold { get { return _gold; } }
        public Brush Star { get { return _star; } }
        public Brush Panel { get { return _panel; } }
        public Brush Red { get { return _red; } }

        public void Background(Graphics graphics, uint tick)
        {
            graphics.Clear(_navy.Color);
            // Fixed stars make rendering independent of random gameplay state.
            for (var index = 0; index < 66; index++)
            {
                var x = (index * 97 + index * index * 13 + 17) % 320;
                var y = (int)(((uint)(index * 71 + 11) + tick / (index % 3 == 0 ? 1u : 2u)) % 400u);
                var bright = index % 11 == 0;
                graphics.FillRectangle(bright ? _white : index % 3 == 0 ? _star : _faint,
                    new Rectangle(x, y, bright ? 2 : 1, bright ? 2 : 1));
            }
            graphics.FillRectangle(_faint, new Rectangle(0, 29, 320, 1));
        }

        public void Player(Graphics graphics, int centerX, int centerY, int player, uint tick, int invulnerableTicks)
        {
            if (invulnerableTicks > 0 && tick > 0 && (tick / 3) % 2 == 0) { return; }
            var x = centerX - 8;
            var y = centerY - 10;
            var body = player == 0 ? _cyan : _gold;
            var middle = player == 0 ? _blue : _orange;
            var shade = player == 0 ? _blueShadow : _orangeShadow;
            graphics.FillRectangle(shade, new Rectangle(x + 6, y, 4, 18));
            graphics.FillRectangle(body, new Rectangle(x + 7, y, 2, 13));
            graphics.FillRectangle(body, new Rectangle(x + 5, y + 4, 6, 12));
            graphics.FillRectangle(middle, new Rectangle(x + 3, y + 9, 10, 7));
            graphics.FillRectangle(body, new Rectangle(x + 1, y + 12, 14, 4));
            graphics.FillRectangle(shade, new Rectangle(x, y + 15, 16, 2));
            graphics.FillRectangle(body, new Rectangle(x + 1, y + 8, 2, 8));
            graphics.FillRectangle(body, new Rectangle(x + 13, y + 8, 2, 8));
            graphics.FillRectangle(_white, new Rectangle(x + 7, y + 5, 2, 5));
            graphics.FillRectangle(shade, new Rectangle(x + 5, y + 15, 2, 3));
            graphics.FillRectangle(shade, new Rectangle(x + 9, y + 15, 2, 3));
            var plume = tick % 3 == 0 ? _white : _orange;
            graphics.FillRectangle(plume, new Rectangle(x + 6, y + 18, 4, 2));
            graphics.FillRectangle(_gold, new Rectangle(x + 7, y + 17, 2, 2));
        }

        public void Enemy(Graphics graphics, int centerX, int centerY, int kind)
        {
            if (kind == 2)
            {
                var x = centerX - 14;
                var y = centerY - 12;
                graphics.FillRectangle(_greenShadow, new Rectangle(x + 3, y + 3, 22, 17));
                graphics.FillRectangle(_green, new Rectangle(x + 7, y, 14, 21));
                graphics.FillRectangle(_green, new Rectangle(x, y + 6, 28, 10));
                graphics.FillRectangle(_greenShadow, new Rectangle(x + 2, y + 8, 24, 4));
                graphics.FillRectangle(_greenShadow, new Rectangle(x + 9, y + 2, 10, 15));
                graphics.FillRectangle(_white, new Rectangle(x + 12, y + 6, 4, 5));
                graphics.FillRectangle(_gold, new Rectangle(x + 2, y + 14, 3, 5));
                graphics.FillRectangle(_gold, new Rectangle(x + 23, y + 14, 3, 5));
                graphics.FillRectangle(_red, new Rectangle(x + 12, y + 19, 4, 5));
                return;
            }
            var width = kind == 1 ? 18 : 14;
            var left = centerX - width / 2;
            var top = centerY - 8;
            var fill = kind == 1 ? _violet : _red;
            var shadow = kind == 1 ? _violetShadow : _redShadow;
            graphics.FillRectangle(shadow, new Rectangle(left + 3, top, width - 6, 13));
            graphics.FillRectangle(fill, new Rectangle(left, top + 3, width, 6));
            graphics.FillRectangle(fill, new Rectangle(left + 2, top + 1, 3, 11));
            graphics.FillRectangle(fill, new Rectangle(left + width - 5, top + 1, 3, 11));
            graphics.FillRectangle(fill, new Rectangle(centerX - 3, top + 4, 6, 10));
            graphics.FillRectangle(shadow, new Rectangle(centerX - 2, top + 5, 4, 6));
            graphics.FillRectangle(_gold, new Rectangle(centerX - 1, top + 7, 2, 3));
            graphics.FillRectangle(fill, new Rectangle(centerX - 1, top + 12, 2, 4));
        }

        public void Bullet(Graphics graphics, int centerX, int centerY, int player)
        {
            graphics.FillRectangle(player == 0 ? _cyan : _gold, new Rectangle(centerX - 2, centerY - 4, 4, 8));
            graphics.FillRectangle(_white, new Rectangle(centerX - 1, centerY - 4, 2, 6));
        }

        public void EnemyBullet(Graphics graphics, int centerX, int centerY)
        {
            graphics.FillRectangle(_redShadow, new Rectangle(centerX - 3, centerY - 3, 6, 6));
            graphics.FillRectangle(_red, new Rectangle(centerX - 2, centerY - 3, 4, 6));
            graphics.FillRectangle(_orange, new Rectangle(centerX - 3, centerY - 2, 6, 4));
            graphics.FillRectangle(_gold, new Rectangle(centerX - 1, centerY - 1, 2, 2));
        }

        public void Explosion(Graphics graphics, int centerX, int centerY, int age)
        {
            var radius = 3 + Math.Min(14, age);
            var thickness = age < 5 ? 4 : 2;
            var outside = age < 6 ? _orange : _redShadow;
            graphics.FillRectangle(outside, new Rectangle(centerX - radius, centerY - thickness, radius * 2, thickness * 2));
            graphics.FillRectangle(outside, new Rectangle(centerX - thickness, centerY - radius, thickness * 2, radius * 2));
            graphics.FillRectangle(outside, new Rectangle(centerX - radius + 2, centerY - radius + 2, 3, 3));
            graphics.FillRectangle(outside, new Rectangle(centerX + radius - 5, centerY + radius - 5, 3, 3));
            if (age < 7)
            {
                graphics.FillRectangle(_gold, new Rectangle(centerX - 4, centerY - 4, 8, 8));
                graphics.FillRectangle(_white, new Rectangle(centerX - 2, centerY - 2, 4, 4));
            }
        }

        public void Dispose()
        {
            _navy.Dispose(); _panel.Dispose(); _faint.Dispose(); _star.Dispose(); _white.Dispose();
            _cyan.Dispose(); _blue.Dispose(); _blueShadow.Dispose(); _gold.Dispose(); _orange.Dispose();
            _orangeShadow.Dispose(); _red.Dispose(); _redShadow.Dispose(); _violet.Dispose();
            _violetShadow.Dispose(); _green.Dispose(); _greenShadow.Dispose();
        }
    }
}
