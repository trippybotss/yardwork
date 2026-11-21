using System.Globalization;
using Yardwork.Processes;
using Yardwork.UI.Controls;

namespace Yardwork.UI.Pages;

/// <summary>
/// Task Manager-style list of every running process with icon, description, PID, CPU, memory
/// and main window title. Pick one and attach the overlay to it.
/// </summary>
internal sealed class ProcessesPage : Panel
{
    private const int ColIcon = 0, ColName = 1, ColPid = 2, ColCpu = 3, ColMemory = 4, ColTitle = 5;

    private enum Filter
    {
        Apps,
        Background,
        All,
    }

    private readonly ProcessScanner _scanner = new();
    private readonly DarkGrid _grid = new();
    private readonly SearchBox _search = new();
    private readonly FlatButton _chipApps, _chipBackground, _chipAll, _attach, _detach;
    private readonly ToggleSwitch _autoRefresh = new() { Checked = true };
    private readonly GlyphButton _refresh = new(Glyphs.Refresh) { GlyphSize = 12f };
    private readonly Label _selection;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    private List<ProcessEntry> _all = new();
    private List<ProcessEntry> _view = new();
    private Filter _filter = Filter.Apps;
    private int _sortColumn = ColName;
    private bool _ascending = true;
    private bool _scanning;
    private int? _attachedPid;

    public ProcessesPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Surface;
        Padding = Ui.Pad(22, 16, 22, 10);

        // Toolbar: search + filter chips on the left, refresh controls on the right.
        var toolbar = new Panel { Dock = DockStyle.Top, Height = Ui.S(50), BackColor = Theme.Surface };
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
        };
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            BackColor = Theme.Surface,
        };

        _search.Margin = new Padding(0, 0, Ui.S(14), 0);
        _chipApps = CreateChip("Apps", Filter.Apps);
        _chipBackground = CreateChip("Background", Filter.Background);
        _chipAll = CreateChip("All", Filter.All);
        left.Controls.AddRange([_search, _chipApps, _chipBackground, _chipAll]);

        _refresh.Size = Ui.S(36, 34);
        _refresh.Margin = Padding.Empty;
        var autoLabel = new Label
        {
            Text = "Auto refresh",
            AutoSize = false,
            Size = Ui.S(92, 34),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextDim,
            Font = Theme.Body,
            Margin = Padding.Empty,
        };
        _autoRefresh.Margin = new Padding(Ui.S(8), Ui.S(4), Ui.S(10), 0);
        right.Controls.AddRange([_refresh, _autoRefresh, autoLabel]);

        toolbar.Controls.Add(left);
        toolbar.Controls.Add(right);

        // Grid with a 1px border.
        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
        _grid.Dock = DockStyle.Fill;
        gridHost.Controls.Add(_grid);
        BuildColumns();

        // Bottom bar: selection summary + attach/detach.
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(58), BackColor = Theme.Surface, Padding = new Padding(0, Ui.S(12), 0, 0) };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Surface,
        };
        _attach = new FlatButton("Attach Overlay", ButtonStyle.Primary, Glyphs.Link) { Margin = Padding.Empty, Enabled = false };
        _detach = new FlatButton("Detach") { Margin = new Padding(0, 0, Ui.S(10), 0), Enabled = false };
        buttons.Controls.AddRange([_attach, _detach]);
        _selection = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextDim,
            Font = Theme.Body,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Text = "Select a process to attach the overlay.",
        };
        bottom.Controls.Add(_selection);
        bottom.Controls.Add(buttons);

        Controls.Add(gridHost);
        Controls.Add(bottom);
        Controls.Add(toolbar);

        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellFormatting += OnCellFormatting;
        _grid.ColumnHeaderMouseClick += OnHeaderClick;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) RequestAttach();
        };
        _grid.EnterPressed += (_, _) => RequestAttach();
        _grid.SelectionChanged += (_, _) => UpdateSelectionInfo();
        _search.QueryChanged += (_, _) => ApplyView();
        _refresh.Click += (_, _) => RefreshNow();
        _autoRefresh.CheckedChanged += (_, _) => _timer.Enabled = _autoRefresh.Checked;
        _timer.Tick += (_, _) => RefreshNow();
        _attach.Click += (_, _) => RequestAttach();
        _detach.Click += (_, _) => DetachRequested?.Invoke();
        UpdateSortGlyphs();
    }

    public event Action<ProcessEntry>? AttachRequested;
    public event Action? DetachRequested;

    public int? AttachedPid
    {
        get => _attachedPid;
        set
        {
            _attachedPid = value;
            _detach.Enabled = value is not null;
            _grid.Invalidate();
        }
    }

    public void FocusSearch() => _search.FocusInput();

    public void ApplyAccent() => _grid.ApplyAccent();

    public async void RefreshNow()
    {
        if (_scanning || IsDisposed) return;
        _scanning = true;
        try
        {
            var processes = await Task.Run(() =>
            {
                var result = _scanner.Scan();
                IconCache.Warm(result.Select(p => p.Path));
                return result;
            });
            if (IsDisposed) return;
            _all = processes;
            ApplyView();
        }
        catch (Exception ex)
        {
            Log.Error("process scan", ex);
        }
        finally
        {
            _scanning = false;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshNow();
        _timer.Enabled = _autoRefresh.Checked;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private FlatButton CreateChip(string text, Filter filter)
    {
        var chip = new FlatButton(text, ButtonStyle.Chip)
        {
            Selected = filter == _filter,
            Margin = new Padding(0, 0, Ui.S(8), 0),
            Tag = filter,
        };
        chip.Click += (_, _) =>
        {
            _filter = filter;
            foreach (var c in new[] { _chipApps, _chipBackground, _chipAll })
                c.Selected = c.Tag is Filter f && f == _filter;
            ApplyView();
        };
        return chip;
    }

    private void BuildColumns()
    {
        _grid.VirtualMode = true;

        var icon = new DataGridViewImageColumn
        {
            HeaderText = "",
            Width = Ui.S(36),
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ImageLayout = DataGridViewImageCellLayout.Normal,
        };
        icon.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        icon.DefaultCellStyle.Padding = Padding.Empty;
        icon.DefaultCellStyle.NullValue = null;
        _grid.Columns.Add(icon);

        AddTextColumn("Name", fill: 38, minWidth: 180);
        AddTextColumn("PID", width: 80, alignRight: true);
        AddTextColumn("CPU", width: 80, alignRight: true);
        AddTextColumn("Memory", width: 110, alignRight: true);
        AddTextColumn("Window title", fill: 62, minWidth: 160);
    }

    private void AddTextColumn(string header, int width = 0, float fill = 0, int minWidth = 40, bool alignRight = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.Programmatic,
            MinimumWidth = Ui.S(minWidth),
        };
        if (fill > 0)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.FillWeight = fill;
        }
        else
        {
            column.Width = Ui.S(width);
        }
        if (alignRight)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        _grid.Columns.Add(column);
    }

    private ProcessEntry? SelectedEntry
    {
        get
        {
            if (_grid.SelectedRows.Count == 0) return null;
            int index = _grid.SelectedRows[0].Index;
            return index >= 0 && index < _view.Count ? _view[index] : null;
        }
    }

    private void ApplyView()
    {
        int? selectedPid = SelectedEntry?.Pid;
        int firstRow = _grid.RowCount > 0 ? Math.Max(0, _grid.FirstDisplayedScrollingRowIndex) : 0;
        string query = _search.Query.Trim();

        int apps = 0, background = 0;
        var view = new List<ProcessEntry>(_all.Count);
        foreach (var process in _all)
        {
            if (query.Length > 0 && !Matches(process, query)) continue;
            if (process.IsApp) apps++;
            else background++;

            if (_filter == Filter.Apps && !process.IsApp) continue;
            if (_filter == Filter.Background && process.IsApp) continue;
            view.Add(process);
        }
        view.Sort(Compare);
        _view = view;

        _chipApps.Text = $"Apps  {apps}";
        _chipBackground.Text = $"Background  {background}";
        _chipAll.Text = $"All  {apps + background}";

        _grid.RowCount = _view.Count;
        _grid.Invalidate();

        try
        {
            int index = selectedPid is int pid ? _view.FindIndex(p => p.Pid == pid) : -1;
            if (index >= 0)
            {
                _grid.CurrentCell = _grid[ColName, index];
                _grid.Rows[index].Selected = true;
            }
            else
            {
                _grid.ClearSelection();
            }

            if (_view.Count > 0)
                _grid.FirstDisplayedScrollingRowIndex = Math.Clamp(firstRow, 0, _view.Count - 1);
        }
        catch (InvalidOperationException)
        {
            // Not enough room to show rows yet; selection and scroll are cosmetic.
        }

        UpdateSelectionInfo();
    }

    private static bool Matches(ProcessEntry process, string query) =>
        process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        process.ImageName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        process.WindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        process.Pid.ToString(CultureInfo.InvariantCulture).StartsWith(query, StringComparison.Ordinal);

    private int Compare(ProcessEntry a, ProcessEntry b)
    {
        // In "All", apps come first like Task Manager's groups.
        if (_filter == Filter.All && a.IsApp != b.IsApp) return a.IsApp ? -1 : 1;

        int result = _sortColumn switch
        {
            ColPid => a.Pid.CompareTo(b.Pid),
            ColCpu => (a.Cpu ?? -1).CompareTo(b.Cpu ?? -1),
            ColMemory => a.Memory.CompareTo(b.Memory),
            ColTitle => string.Compare(a.WindowTitle, b.WindowTitle, StringComparison.CurrentCultureIgnoreCase),
            _ => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
        };
        if (!_ascending) result = -result;
        return result != 0 ? result : a.Pid.CompareTo(b.Pid);
    }

    private void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex <= ColIcon) return;
        if (_sortColumn == e.ColumnIndex)
        {
            _ascending = !_ascending;
        }
        else
        {
            _sortColumn = e.ColumnIndex;
            // Text columns start A→Z; CPU and memory start with the biggest.
            _ascending = e.ColumnIndex is ColName or ColTitle or ColPid;
        }
        UpdateSortGlyphs();
        ApplyView();
    }

    private void UpdateSortGlyphs()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column.SortMode == DataGridViewColumnSortMode.NotSortable) continue;
            column.HeaderCell.SortGlyphDirection = column.Index != _sortColumn ? SortOrder.None
                : _ascending ? SortOrder.Ascending : SortOrder.Descending;
        }
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        var process = _view[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            ColIcon => IconCache.Get(process.Path),
            ColName => process.Name,
            ColPid => process.Pid.ToString(CultureInfo.InvariantCulture),
            ColCpu => process.Cpu is double cpu ? $"{cpu:0.0} %" : "—",
            ColMemory => FormatBytes(process.Memory),
            ColTitle => process.WindowTitle,
            _ => null,
        };
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count || _view[e.RowIndex].Pid != _attachedPid) return;
        if (e.CellStyle is { } style)
        {
            style.ForeColor = Theme.AccentBright;
            style.SelectionForeColor = Color.White;
            style.Font = Theme.BodySemibold;
        }
        if (e.ColumnIndex == ColName && e.Value is string name)
        {
            e.Value = "●  " + name;
            e.FormattingApplied = true;
        }
    }

    private void UpdateSelectionInfo()
    {
        var process = SelectedEntry;
        _attach.Enabled = process is not null;
        _selection.Text = process switch
        {
            null => "Select a process to attach the overlay.",
            { IsApp: true } => $"{process.ImageName}  ·  PID {process.Pid}  ·  “{process.WindowTitle}”",
            _ => $"{process.ImageName}  ·  PID {process.Pid}  ·  no window, so the overlay will cover your primary monitor",
        };
    }

    private void RequestAttach()
    {
        if (SelectedEntry is { } process) AttachRequested?.Invoke(process);
    }

    private static string FormatBytes(long bytes)
    {
        double megabytes = bytes / 1048576.0;
        return megabytes >= 1024 ? $"{megabytes / 1024:0.00} GB" : $"{megabytes:0.0} MB";
    }
}
