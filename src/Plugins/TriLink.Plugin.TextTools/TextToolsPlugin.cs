using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using TriLink.Plugin;

namespace TriLink.Plugins.TextTools
{
    public sealed class TextToolsPlugin : ITriLinkPlugin
    {
        public void Configure(IPluginContext context)
        {
            var lease = context.GetRequired<IModuleFeatureRegistry>().Register(new ModuleFeature {
                Id = "trilink.text-tools.inspect", ModuleId = context.PluginId,
                Title = "文本数据检查", Description = "本地计算 UTF-8 字节数、SHA-256 和 Base64；不发送网络数据。" },
                () => new TextToolsView());
            context.Defer(lease.Dispose);
        }
        public void Start() { }
        public void Stop() { }
    }

    public sealed class TextToolsView : UserControl
    {
        private readonly TextBox _input = new TextBox { Multiline = true, MaxLength = 16384,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Name = "TextInput" };
        private readonly TextBox _output = new TextBox { Multiline = true, ReadOnly = true,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Name = "TextOutput" };
        public TextToolsView()
        {
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            layout.Controls.Add(new Label { Text = "文本数据检查 · 独立扩展模块\n输入上限 16,384 个 UTF-16 代码单元；仅在点击时计算，不轮询、不联网。",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(31, 90, 150) }, 0, 0);
            layout.Controls.Add(_input, 0, 1);
            var calculate = new Button { Text = "计算数据", AutoSize = true, Name = "Calculate", Margin = new Padding(0, 6, 0, 6) };
            calculate.Click += (_, __) => _output.Text = Inspect(_input.Text);
            layout.Controls.Add(calculate, 0, 2);
            layout.Controls.Add(_output, 0, 3);
            Controls.Add(layout);
        }
        public static string Inspect(string text)
        {
            text = text ?? string.Empty;
            if (text.Length > 16384) { throw new ArgumentException("文本超过 16,384 输入上限。"); }
            var bytes = Encoding.UTF8.GetBytes(text);
            using (var hash = SHA256.Create())
            {
                return "UTF-8 字节数：" + bytes.Length + "\r\nSHA-256：\r\n"
                    + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
                    + "\r\n\r\nBase64：\r\n" + Convert.ToBase64String(bytes);
            }
        }
    }
}
