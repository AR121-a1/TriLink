using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TriLink.Plugin;

namespace TriLink.MinClient
{
    // UI owns windows only. The management service owns configuration, validation and file I/O.
    internal sealed class ModulesForm : Form
    {
        private readonly IModuleManagementService _manager;
        private readonly IModuleFeatureRegistry _features;
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _details = new TextBox();
        private readonly Label _status = new Label();
        private readonly Button _toggle = new Button();
        private readonly Button _open = new Button();
        private readonly Button _import = new Button();
        private readonly Button _cancel = new Button();
        private readonly Button _restore = new Button();
        private readonly ComboBox _featureChoices = new ComboBox();
        private ModuleSnapshot _snapshot;
        private bool _refreshing;
        private bool _busy;

        public ModulesForm(IModuleManagementService manager, IModuleFeatureRegistry features)
        {
            _manager = manager;
            _features = features;
            Text = "TriLink · 扩展模块";
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = new Size(1060, 780);
            MinimumSize = new Size(960, 700);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(245, 247, 250);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.Controls.Add(new Label { Text = "扩展模块\n管理本机功能包：查看详情 → 启用 / 禁用 → 从托盘退出并重新启动。",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(31, 90, 150) }, 0, 0);
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = Color.White;
            foreach (var column in new[] { "模块", "模块 ID", "包版本", "本次运行", "下次启动", "变更" })
            { _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column, HeaderText = column, SortMode = DataGridViewColumnSortMode.NotSortable }); }
            _grid.Columns[0].FillWeight = 160;
            _grid.Columns[1].FillWeight = 140;
            _grid.Columns[2].FillWeight = 65;
            // SelectionChanged fires before CurrentRow changes when navigating programmatically.
            _grid.CurrentCellChanged += (_, __) => { if (!_refreshing) { ShowSelection(); } };
            layout.Controls.Add(_grid, 0, 1);
            _details.Multiline = true;
            _details.ReadOnly = true;
            _details.ScrollBars = ScrollBars.Vertical;
            _details.Dock = DockStyle.Fill;
            _details.BackColor = Color.White;
            layout.Controls.Add(_details, 0, 2);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            _toggle.Text = "禁用（下次）";
            _toggle.AutoSize = true;
            _toggle.Click += (_, __) => Apply(() => _manager.SetEnabled(Selected.Id, !Selected.EnabledNextStart), "已保存启停配置，当前运行不变。");
            actions.Controls.Add(_toggle);
            _featureChoices.DropDownStyle = ComboBoxStyle.DropDownList;
            _featureChoices.DisplayMember = "Title";
            _featureChoices.Width = 180;
            actions.Controls.Add(_featureChoices);
            _open.Text = "打开功能";
            _open.AutoSize = true;
            _open.Click += (_, __) => OpenFeature();
            actions.Controls.Add(_open);
            var refresh = new Button { Text = "刷新状态", AutoSize = true };
            refresh.Click += (_, __) => { if (!_busy) { Apply(() => { }, "已刷新模块状态。"); } };
            actions.Controls.Add(refresh);
            layout.Controls.Add(actions, 0, 3);
            var packages = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            _import.Text = "导入 / 更新模块…";
            _import.AutoSize = true;
            _import.Click += async (_, __) => await ImportPackageAsync();
            _cancel.Text = "撤销本次配置变更";
            _cancel.AutoSize = true;
            _cancel.Click += (_, __) => Apply(_manager.CancelPendingChanges, "已还原到本次启动配置；导入的包保留但不加载。");
            _restore.Text = "恢复内置模块";
            _restore.AutoSize = true;
            _restore.Click += (_, __) =>
            {
                if (MessageBox.Show(this, "下次启动将恢复随客户端发布的内置模块。已导入的包保留但不再选用。继续？",
                    "恢复内置", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                { Apply(_manager.RestoreBuiltIns, "恢复配置已保存；请从托盘退出，再正常启动。"); }
            };
            packages.Controls.AddRange(new Control[] { _import, _cancel, _restore });
            layout.Controls.Add(packages, 0, 4);
            _status.Dock = DockStyle.Fill;
            _status.Padding = new Padding(5);
            _status.AutoEllipsis = true;
            layout.Controls.Add(_status, 0, 5);
            Controls.Add(layout);
            FormClosing += (_, e) => { if (_busy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; } };
            Apply(() => { }, null);
        }

        private ModuleInfo Selected { get { return _grid.CurrentRow?.Tag as ModuleInfo; } }

        private void RefreshSnapshot()
        {
            var selectedId = Selected?.Id;
            _snapshot = _manager.GetSnapshot();
            _refreshing = true;
            try
            {
                _grid.Rows.Clear();
                foreach (var item in _snapshot.Modules)
                {
                    var index = _grid.Rows.Add(item.Name, item.Id, item.Version,
                        item.Running ? "运行 " + item.RunningVersion : "未加载",
                        item.EnabledNextStart ? "启用" : "禁用", item.PendingChange ? "待重启" : "—");
                    _grid.Rows[index].Tag = item;
                    if (item.Id == selectedId) { _grid.CurrentCell = _grid.Rows[index].Cells[0]; }
                }
            }
            finally { _refreshing = false; }
            _import.Enabled = !_busy && !_snapshot.SafeMode;
            _cancel.Enabled = !_busy && !_snapshot.SafeMode && _snapshot.RestartRequired;
            _restore.Enabled = !_busy;
            ShowSelection();
        }

        private void ShowSelection()
        {
            var item = Selected;
            _toggle.Enabled = !_busy && !_snapshot.SafeMode && item != null
                && (!item.EnabledNextStart || string.IsNullOrEmpty(item.DisableBlockedReason));
            _toggle.Text = item != null && item.EnabledNextStart ? "禁用（下次）" : "启用（下次）";
            _featureChoices.Items.Clear();
            if (item != null)
            {
                _details.Text = item.Name + "  |  " + item.Id + "\r\n"
                    + "模块能力：" + string.Join(", ", item.Capabilities) + "\r\n"
                    + "依赖模块：" + List(item.Dependencies) + "\r\n"
                    + "依赖服务：" + List(item.RequiredServices) + "\r\n"
                    + "提供服务：" + List(item.ProvidedServices) + "\r\n"
                    + "下次加载位置：" + item.SourceDirectory + "\r\n"
                    + (item.DisableBlockedReason ?? "可独立启停；变更不会中断本次运行。") + "\r\n"
                    + "模块 DLL 与客户端权限相同，完整性校验不等于可信签名。";
                foreach (var feature in _features.Features.Where(value => value.ModuleId == item.Id))
                { _featureChoices.Items.Add(feature); }
            }
            else { _details.Text = "没有可显示的模块。可刷新状态或恢复内置配置。"; }
            if (_featureChoices.Items.Count > 0) { _featureChoices.SelectedIndex = 0; }
            _open.Enabled = !_busy && _featureChoices.Items.Count > 0;
        }

        private void Apply(Action action, string success)
        {
            try
            {
                action();
                RefreshSnapshot();
                SetStatus(success, false);
            }
            catch (Exception exception) { SetStatus("操作失败，未应用此操作：" + exception.Message, true); }
        }

        private void SetStatus(string message, bool error)
        {
            _status.ForeColor = error ? Color.Firebrick : Color.FromArgb(31, 90, 150);
            _status.Text = (error ? "错误 · " : _snapshot?.RestartRequired == true ? "待重启 · " : "就绪 · ")
                + (message ?? "扩展管理已就绪。") + "\r\n" + (_snapshot?.Notice ?? "可尝试恢复内置模块。")
                + "\r\n包格式：plugin.json + 单个 DLL；没有热卸载、在线下载或自动执行安装脚本。";
        }

        private async Task ImportPackageAsync()
        {
            using (var picker = new OpenFileDialog { Title = "选择可信模块包的 plugin.json", Filter = "模块清单 (plugin.json)|plugin.json", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) { return; }
                _busy = true;
                ShowSelection();
                _import.Enabled = _cancel.Enabled = _restore.Enabled = false;
                try
                {
                    var preview = await Task.Run(() => _manager.PreviewPackage(picker.FileName));
                    if (IsDisposed) { return; }
                    var answer = MessageBox.Show(this, "导入 " + preview.Name + "\r\n" + preview.Id + "  v" + preview.Version
                        + "\r\nSHA-256: " + preview.Sha256
                        + "\r\n\r\n仅导入你信任的 DLL。模块启用后能以客户端的权限访问本机。哈希只能检查文件一致性，不证明代码安全。"
                        + "\r\n新增模块默认禁用；已启用模块的更新将在下次启动执行。是否导入？",
                        "确认模块来源", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) { return; }
                    await Task.Run(() => _manager.ImportPackage(picker.FileName, preview.Fingerprint));
                    if (!IsDisposed)
                    {
                        RefreshSnapshot();
                        SetStatus("模块包已导入；新增模块需选择后启用，更新需重启。", false);
                    }
                }
                catch (Exception exception) { if (!IsDisposed) { SetStatus("导入失败：" + exception.Message, true); } }
                finally
                {
                    _busy = false;
                    if (!IsDisposed)
                    {
                        try { RefreshSnapshot(); }
                        catch (Exception exception) { SetStatus("刷新失败：" + exception.Message, true); }
                    }
                }
            }
        }

        private void OpenFeature()
        {
            var feature = _featureChoices.SelectedItem as ModuleFeature;
            if (feature == null) { return; }
            Form window = null;
            try
            {
                window = new Form { Text = "TriLink · " + feature.Title, Size = new Size(780, 650),
                    MinimumSize = new Size(600, 480), Font = Font, StartPosition = FormStartPosition.CenterParent };
                var view = _features.CreateView(feature.Id);
                view.Dock = DockStyle.Fill;
                window.Controls.Add(view);
                window.Show(this);
            }
            catch (Exception exception)
            {
                window?.Dispose();
                SetStatus("功能打开失败：" + exception.Message, true);
            }
        }
        private static string List(string[] values) { return values.Length == 0 ? "（无）" : string.Join(", ", values); }
    }
}
